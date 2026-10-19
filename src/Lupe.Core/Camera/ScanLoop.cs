// SPDX-License-Identifier: MIT
using System.Diagnostics;
using Lupe.Core.Model;
using Lupe.Core.Resources;

namespace Lupe.Core.Camera;

/// <summary>
/// Verbindet Bildquelle und Erkennung: drosselt (Bilder, die während einer laufenden Erkennung kommen, werden
/// verworfen), meldet Treffer mit Wiederholungssperre und reicht jedes Bild für die Vorschau weiter.
/// </summary>
/// <remarks>
/// <para>Ablauf je Bild der Quelle: Läuft gerade keine Erkennung, bekommt eine neue Erkennung (Thread-Pool) eine eigene
/// Kopie des Bildes; danach geht das Bild unverändert an <see cref="Frame"/> – immer, auch während einer Erkennung.
/// Es gibt nie mehr als eine Erkennung gleichzeitig und keine Warteschlange.</para>
/// <para>Die Wiederholungssperre gilt je Text + Format ab der <b>letzten</b> Sichtung und beginnt bei jedem Start neu.</para>
/// <para>Fällt die Quelle aus oder wirft die Erkennung (außer <see cref="OperationCanceledException"/>), kommt
/// <see cref="Failed"/> mit deutscher Meldung und die Schleife hält an (die Quelle wird angehalten); ein erneutes
/// <see cref="StartAsync"/> startet beides wieder. Ein Ausfall schon während <see cref="StartAsync"/> wird gemeldet,
/// nachdem die Schleife wieder aufgeräumt ist – noch bevor <see cref="StartAsync"/> zurückkehrt.</para>
/// <para>Start, Stopp und Freigabe sind beliebig wiederholbar und threadsicher, auch aus den eigenen Ereignissen heraus
/// (selbst synchron mit <c>.Wait()</c>, solange nicht zugleich ein anderer Thread startet oder anhält, der auf genau
/// dieses Ereignis wartet). Nach Rückkehr von <see cref="StopAsync"/> kommen keine Ereignisse mehr (aus einem eigenen
/// Ereignis heraus aufgerufen wirkt der Stopp sofort, das Aufräumen läuft im Hintergrund). Die Quelle gehört dem
/// Aufrufer: Stopp und Freigabe halten sie nur an, geben sie aber nicht frei.</para>
/// </remarks>
public sealed class ScanLoop : IAsyncDisposable
{
    private readonly IFrameSource _source;
    private readonly Func<PixelImage, IReadOnlyList<DecodedCode>> _decode;
    private readonly TimeProvider _time;
    private readonly RepeatFilter _filter;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _sync = new();

    // unter _sync
    private int _generation;
    private bool _accepting;
    /// <summary>StartAsync wartet gerade auf die Quelle; Ausfälle meldet dann StartAsync (nach dem Aufräumen).</summary>
    private bool _starting;
    private string? _startFailure;
    /// <summary>Generation, für die die Quelle schon aus ihrem eigenen Ereignis heraus angehalten wurde.</summary>
    private int _sourceStoppedFor = -1;
    // geschrieben nur unter _lifecycle und _sync, gelesen unter einem von beiden
    private bool _attached;
    private volatile bool _disposed;
    // 1, solange eine Erkennung läuft
    private int _decoding;
    private Task _decodeTask = Task.CompletedTask;

    /// <summary>Gesetzt, während dieser Thread gerade ein Ereignis dieser Schleife ausliefert (gegen Selbst-Verklemmung).</summary>
    [ThreadStatic] private static ScanLoop? t_callbackOwner;
    /// <summary>Gesetzt, während dieser Thread gerade ein Ereignis der Quelle an diese Schleife abarbeitet.</summary>
    [ThreadStatic] private static ScanLoop? t_sourceCallback;

    public ScanLoop(IFrameSource source, Func<PixelImage, IReadOnlyList<DecodedCode>> decode, int repeatSuppressMs = 3000)
        : this(source, decode, repeatSuppressMs, TimeProvider.System)
    {
    }

    /// <summary>Mit eigener Zeitquelle (Tests).</summary>
    internal ScanLoop(IFrameSource source, Func<PixelImage, IReadOnlyList<DecodedCode>> decode, int repeatSuppressMs, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(decode);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfNegative(repeatSuppressMs);
        _source = source;
        _decode = decode;
        _time = time;
        _filter = new RepeatFilter(TimeSpan.FromMilliseconds(repeatSuppressMs), time);
    }

    /// <summary>Jedes Bild der Quelle, für die Vorschau.</summary>
    public event EventHandler<PixelImage>? Frame;
    /// <summary>Neue Treffer (ohne die gerade gesperrten Wiederholungen) samt dem Bild, in dem sie gefunden wurden.</summary>
    public event EventHandler<(IReadOnlyList<DecodedCode> Codes, PixelImage Frame)>? Detected;
    public event EventHandler<string>? Failed;

    /// <summary>Läuft die Schleife gerade (gestartet und nicht ausgefallen)?</summary>
    internal bool IsRunning
    {
        get { lock (_sync) return _accepting; }
    }

    /// <summary>Die zuletzt gestartete Erkennung (Tests warten darauf).</summary>
    internal Task CurrentDecode => Volatile.Read(ref _decodeTask);

    /// <summary>
    /// Startet Quelle und Erkennung; läuft sie schon, passiert nichts. Startprobleme der Quelle kommen über
    /// <see cref="Failed"/>; nur ein Abbruch über <paramref name="ct"/> wirft.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Vor dem ersten await festhalten: aus einem eigenen Ereignis heraus nicht auf die eigene Erkennung warten
        bool inCallback = t_callbackOwner == this;
        string? failure = null;
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_sync)
            {
                if (_attached && _accepting) return;
            }
            // Reste nach einem Ausfall (Abmelden und Anhalten läuft sonst im Hintergrund)
            if (_attached) await DetachAsync(waitForDecode: !inCallback).ConfigureAwait(false);

            lock (_sync)
            {
                _generation++;
                _accepting = true;
                _starting = true;
                _startFailure = null;
                _filter.Clear();
            }
            _source.FrameArrived += OnSourceFrame;
            _source.Failed += OnSourceFailed;
            lock (_sync) _attached = true;

            Exception? startError = null;
            try
            {
                await _source.StartAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                lock (_sync)
                {
                    _starting = false;
                    _startFailure = null;
                }
                await DetachAsync(waitForDecode: !inCallback).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                startError = ex;
            }

            bool running;
            lock (_sync)
            {
                _starting = false;
                // Hat die Quelle ihren Ausfall schon selbst gemeldet, nur diese Meldung weitergeben
                failure = _startFailure;
                _startFailure = null;
                if (startError is not null && _accepting)
                    failure = string.Format(Strings.Core_Camera_CannotStart, CameraList.CleanName(SafeName()), startError.Message);
                if (startError is not null) _accepting = false;
                running = _accepting;
            }
            // Ausgefallen oder aus einem Ereignis heraus angehalten: gleich hier aufräumen
            if (!running) await DetachAsync(waitForDecode: !inCallback).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
        // Erst nach dem Freigeben der Sperre melden: Ein Empfänger darf dann sogar synchron neu starten
        if (failure is not null) RaiseFailed(failure);
    }

    /// <summary>Hält Quelle und Erkennung an und wartet eine laufende Erkennung ab. Mehrfach aufrufbar.</summary>
    public Task StopAsync()
    {
        if (t_callbackOwner == this) return StopFromCallback();
        return StopCoreAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await StopAsync().ConfigureAwait(false);
    }

    private async Task StopCoreAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_attached) await DetachAsync(waitForDecode: true).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Aus einem eigenen Ereignis heraus: sofort nichts mehr melden, das Aufräumen im Hintergrund.</summary>
    private Task StopFromCallback()
    {
        int generation;
        lock (_sync)
        {
            _accepting = false;
            generation = _generation;
            StopSourceFromItsCallbackLocked(generation);
        }
        _ = Task.Run(() => DetachInBackgroundAsync(generation));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Läuft dieser Thread gerade in einem Ereignis der Quelle (Bild oder Ausfall), wird die Quelle gleich hier angehalten:
    /// Die eingebauten Quellen erkennen den Aufruf aus ihrem eigenen Ereignis und warten nicht auf sich selbst. Hielte
    /// erst der Aufräumer im Hintergrund die Quelle an, wartete er auf das Ende genau dieses Ereignisses – und das wartet
    /// womöglich synchron auf einen Neustart der Schleife, der wiederum auf den Aufräumer wartet. Nur unter <see cref="_sync"/>.
    /// </summary>
    private void StopSourceFromItsCallbackLocked(int generation)
    {
        if (t_sourceCallback != this || !_attached || _sourceStoppedFor == generation) return;
        _sourceStoppedFor = generation;
        try
        {
            var stop = _source.StopAsync();
            _ = stop.ContinueWith(static t => Trace.TraceWarning($"Lupe: Anhalten der Bildquelle fehlgeschlagen: {t.Exception}"),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Lupe: Anhalten der Bildquelle fehlgeschlagen: {ex}");
        }
    }

    /// <summary>
    /// Hält an, falls seit <paramref name="generation"/> kein neuer Start dazwischengekommen ist. Wartet nicht auf die
    /// laufende Erkennung: Ausgelöst wurde das aus einem Ereignis heraus, das womöglich noch (synchron) auf uns wartet.
    /// </summary>
    private async Task DetachInBackgroundAsync(int generation)
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            bool same;
            lock (_sync) same = generation == _generation && _attached;
            if (same) await DetachAsync(waitForDecode: false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Lupe: Anhalten der Kamera-Schleife fehlgeschlagen: {ex}");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>
    /// Abmelden, Quelle anhalten (falls nicht schon aus ihrem eigenen Ereignis heraus geschehen), laufende Erkennung
    /// abwarten. Nur unter <see cref="_lifecycle"/>. Aus einem eigenen Ereignis heraus (<paramref name="waitForDecode"/> =
    /// false) wäre das Warten eine Verklemmung – dann genügt, dass nichts mehr angenommen wird: das Ergebnis der
    /// laufenden Erkennung wird verworfen.
    /// </summary>
    private async Task DetachAsync(bool waitForDecode)
    {
        bool stopSource;
        lock (_sync)
        {
            _accepting = false;
            _attached = false;
            stopSource = _sourceStoppedFor != _generation;
        }
        _source.FrameArrived -= OnSourceFrame;
        _source.Failed -= OnSourceFailed;
        if (stopSource)
        {
            try
            {
                await _source.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Lupe: Anhalten der Bildquelle fehlgeschlagen: {ex}");
            }
        }
        if (waitForDecode && t_callbackOwner != this)
        {
            try
            {
                await Volatile.Read(ref _decodeTask).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Fehler der Erkennung sind schon gemeldet
            }
        }
    }

    private void OnSourceFrame(object? sender, PixelImage image)
    {
        if (image is null) return;
        var previous = t_sourceCallback;
        t_sourceCallback = this;
        try
        {
            HandleFrame(image);
        }
        finally
        {
            t_sourceCallback = previous;
        }
    }

    private void HandleFrame(PixelImage image)
    {
        int generation;
        lock (_sync)
        {
            if (!_accepting) return;
            generation = _generation;
        }
        long seen = _time.GetTimestamp();

        // Höchstens eine Erkennung; während sie läuft, werden Bilder nur angezeigt, nicht gesammelt
        if (Interlocked.CompareExchange(ref _decoding, 1, 0) == 0)
        {
            try
            {
                var copy = PixelImages.Copy(image);
                var task = new Task(() => DecodeAndReport(copy, generation, seen));
                Volatile.Write(ref _decodeTask, task);
                task.Start(TaskScheduler.Default);
            }
            catch
            {
                Volatile.Write(ref _decoding, 0);
                throw;
            }
        }

        Raise(Frame, image, nameof(Frame));
    }

    private void DecodeAndReport(PixelImage frame, int generation, long seen)
    {
        try
        {
            IReadOnlyList<DecodedCode>? codes;
            try
            {
                codes = _decode(frame);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Fail(generation, string.Format(Strings.Core_Camera_DecodeFailed, ex.GetType().Name, ex.Message));
                return;
            }
            if (codes is null || codes.Count == 0) return;

            IReadOnlyList<DecodedCode> fresh;
            lock (_sync)
            {
                if (!_accepting || generation != _generation) return;
                fresh = _filter.Filter(codes, seen);
            }
            if (fresh.Count > 0) Raise(Detected, (fresh, frame), nameof(Detected));
        }
        finally
        {
            Volatile.Write(ref _decoding, 0);
        }
    }

    private void OnSourceFailed(object? sender, string message)
    {
        var previous = t_sourceCallback;
        t_sourceCallback = this;
        try
        {
            int generation;
            lock (_sync) generation = _generation;
            Fail(generation, string.IsNullOrWhiteSpace(message)
                ? string.Format(Strings.Core_Camera_Failed, CameraList.CleanName(SafeName()))
                : message);
        }
        finally
        {
            t_sourceCallback = previous;
        }
    }

    /// <summary>Einmal melden, nichts mehr annehmen, anhalten (die Quelle wenn möglich sofort, den Rest im Hintergrund).</summary>
    private void Fail(int generation, string message)
    {
        lock (_sync)
        {
            if (!_accepting || generation != _generation) return;
            _accepting = false;
            if (_starting)
            {
                // StartAsync räumt auf und meldet, sobald die Quelle zurück ist und die Sperre frei
                _startFailure = message;
                return;
            }
            StopSourceFromItsCallbackLocked(generation);
        }
        RaiseFailed(message);
        _ = Task.Run(() => DetachInBackgroundAsync(generation));
    }

    private void RaiseFailed(string message) => Raise(Failed, message, nameof(Failed));

    private void Raise<T>(EventHandler<T>? handler, T args, string eventName)
    {
        var previous = t_callbackOwner;
        t_callbackOwner = this;
        try
        {
            SafeEvents.Raise(handler, this, args, eventName);
        }
        finally
        {
            t_callbackOwner = previous;
        }
    }

    private string? SafeName()
    {
        try { return _source.Name; }
        catch (Exception) { return null; }
    }
}
