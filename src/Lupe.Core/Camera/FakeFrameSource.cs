using System.Diagnostics;
using Lupe.Core.Model;

namespace Lupe.Core.Camera;

/// <summary>Attrappe für Tests, Demo und Screenshots: spielt vorgegebene Bilder in einer Schleife ab.</summary>
/// <remarks>
/// Das erste Bild kommt sofort nach <see cref="StartAsync"/>, danach eines je Takt (1/<c>framesPerSecond</c>) auf einem
/// Hintergrund-Thread; braucht ein Empfänger länger als einen Takt, fallen Takte aus (wie bei einer echten Kamera, keine
/// Warteschlange). Jedes gelieferte Bild ist eine eigene Kopie. Nach Rückkehr von <see cref="StopAsync"/> kommen keine
/// Bilder mehr (aus dem eigenen Ereignis heraus aufgerufen: nach dem laufenden keines mehr). Nach einem Neustart beginnt
/// die Schleife wieder beim ersten Bild.
/// </remarks>
public sealed class FakeFrameSource : IFrameSource
{
    private readonly PixelImage[] _frames;
    private readonly TimeSpan _period;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private Task _loop = Task.CompletedTask;
    private volatile bool _disposed;

    /// <summary>Gesetzt, während dieser Thread gerade ein Ereignis dieser Quelle ausliefert (gegen Selbst-Verklemmung).</summary>
    [ThreadStatic] private static FakeFrameSource? t_callbackOwner;

    public FakeFrameSource(IReadOnlyList<PixelImage> frames, double framesPerSecond = 15, string name = "Demo-Kamera")
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0) throw new ArgumentException("Die Attrappe braucht mindestens ein Bild.", nameof(frames));
        if (!double.IsFinite(framesPerSecond) || framesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond), framesPerSecond, "Die Bildrate muss größer als 0 sein.");

        _frames = new PixelImage[frames.Count];
        for (int i = 0; i < frames.Count; i++)
            _frames[i] = PixelImages.Copy(frames[i] ?? throw new ArgumentException($"Bild {i + 1} fehlt (null).", nameof(frames)));

        // PeriodicTimer braucht mindestens 1 ms; mehr als ein Tag Abstand ergibt keinen Sinn
        double ms = Math.Clamp(1000.0 / framesPerSecond, 1.0, TimeSpan.FromDays(1).TotalMilliseconds);
        _period = TimeSpan.FromMilliseconds(ms);
        Name = string.IsNullOrWhiteSpace(name) ? "Demo-Kamera" : name.Trim();
    }

    public string Name { get; }

    /// <summary>Abstand zwischen zwei Bildern.</summary>
    internal TimeSpan Period => _period;

    public event EventHandler<PixelImage>? FrameArrived;
    public event EventHandler<string>? Failed;

    /// <summary>Startet die Wiedergabe; läuft sie schon, passiert nichts. <paramref name="ct"/> bricht nur das Starten ab.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ct.ThrowIfCancellationRequested();
            lock (_sync)
            {
                // Eine ausgefallene Schleife (beendet, aber nicht abgebrochen) zählt nicht als laufend
                if (_cts is { IsCancellationRequested: false } && !_loop.IsCompleted) return;
            }
            // Reste eines Laufs, der aus einem Ereignis heraus angehalten wurde oder ausgefallen ist: nicht auf ihn
            // warten. Er liefert nach seinem laufenden Ereignis nichts mehr – und genau dieses Ereignis wartet womöglich
            // (synchron, auch über einen anderen Thread wie den Aufräumer der Scan-Schleife) auf diesen Start.
            await FinishLoopAsync(waitForLoop: false).ConfigureAwait(false);

            var cts = new CancellationTokenSource();
            lock (_sync)
            {
                _cts = cts;
                _loop = Task.Run(() => RunAsync(cts.Token), CancellationToken.None);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Hält die Wiedergabe an; danach kommen keine Bilder mehr. Mehrfach aufrufbar.</summary>
    public async Task StopAsync()
    {
        if (t_callbackOwner == this)
        {
            // Aus einem eigenen Ereignis heraus: nur abbrechen, nicht auf die eigene Schleife warten (Verklemmung)
            CancelLoop();
            return;
        }
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await FinishLoopAsync(waitForLoop: true).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await StopAsync().ConfigureAwait(false);
    }

    /// <summary>Spielt einen Ausfall vor (für Tests und Demo): Wiedergabe endet, <see cref="Failed"/> kommt auf einem Hintergrund-Thread.</summary>
    internal Task SimulateFailureAsync(string message)
    {
        CancelLoop();
        return Task.Run(() => Raise(Failed, message, nameof(Failed)));
    }

    private void CancelLoop()
    {
        lock (_sync)
        {
            try { _cts?.Cancel(); }
            catch (ObjectDisposedException) { /* schon aufgeräumt */ }
        }
    }

    /// <summary>Bricht den laufenden Durchgang ab und wartet (meist) sein Ende ab. Nur unter <see cref="_lifecycle"/>.</summary>
    private async Task FinishLoopAsync(bool waitForLoop)
    {
        CancellationTokenSource? cts;
        Task loop;
        lock (_sync)
        {
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = Task.CompletedTask;
            try { cts?.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        if (!waitForLoop)
        {
            // Die alte Schleife prüft ihr (abgebrochenes) Token vor jedem Bild; aufgeräumt wird, wenn sie endet
            _ = loop.ContinueWith(_ => cts?.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return;
        }
        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Abbruch ist normal, andere Fehler hat die Schleife schon gemeldet
        }
        cts?.Dispose();
    }

    private async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(_period);
        int index = 0;
        try
        {
            do
            {
                if (token.IsCancellationRequested) return;
                var frame = PixelImages.Copy(_frames[index]);
                index = (index + 1) % _frames.Length;
                Raise(FrameArrived, frame, nameof(FrameArrived));
            }
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // z. B. kein Speicher mehr für die Kopie – wie eine echte Kamera als Ausfall melden
            Trace.TraceError($"Lupe: Demo-Kamera ausgefallen: {ex}");
            Raise(Failed, $"Die Kamera „{Name}“ ist ausgefallen ({ex.Message}). Starten Sie sie erneut.", nameof(Failed));
        }
    }

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
}
