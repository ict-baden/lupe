using System.Diagnostics;
using Lupe.Core.Camera;
using Lupe.Core.Model;

namespace Lupe.Platform.Windows.Camera;

/// <summary>
/// Echte Kamera als <see cref="IFrameSource"/>. Jeder Start öffnet eine neue <see cref="CaptureSession"/>
/// (MediaCapture + MediaFrameReader), jeder Stopp gibt sie vollständig frei – die Kamera-LED geht dann aus und andere
/// Programme bekommen die Kamera wieder.
/// </summary>
/// <remarks>
/// Sitzungen, die ausgefallen sind oder aus einem eigenen Ereignis heraus angehalten wurden, werden im Hintergrund
/// freigegeben, ohne auf ihr letztes Bild-Ereignis zu warten: Dieses Ereignis wartet womöglich (synchron, auch über
/// andere Threads wie die Scan-Schleife) auf einen Neustart. Ein Start wartet auf solche Freigaben nur kurz.
/// </remarks>
internal sealed class MediaCaptureFrameSource : IFrameSource
{
    /// <summary>So lange wartet ein Start oder Stopp höchstens, bis im Hintergrund freigegebene Sitzungen die Kamera loslassen.</summary>
    private static readonly TimeSpan ReleaseWaitTimeout = TimeSpan.FromSeconds(3);

    private readonly CameraInfo _camera;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _sync = new();
    // unter _sync
    private CaptureSession? _session;
    private Task _releasing = Task.CompletedTask;
    private volatile bool _disposed;

    /// <summary>Gesetzt, während dieser Thread gerade ein Ereignis dieser Quelle ausliefert (gegen Selbst-Verklemmung).</summary>
    [ThreadStatic] private static MediaCaptureFrameSource? t_callbackOwner;

    public MediaCaptureFrameSource(CameraInfo camera)
    {
        _camera = camera;
        Name = CameraList.CleanName(camera.Name);
    }

    public string Name { get; }

    public event EventHandler<PixelImage>? FrameArrived;
    public event EventHandler<string>? Failed;

    /// <summary>Format und Modus der laufenden Sitzung (für Tests und Diagnose); null, wenn keine läuft.</summary>
    internal CaptureFormat? ActiveFormat
    {
        get
        {
            lock (_sync) return _session is { IsActive: true } s ? s.Format : null;
        }
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string? failure = null;
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CaptureSession? old;
            lock (_sync) old = _session;
            if (old is { IsActive: true }) return;
            // Ausgefallen oder aus einem Ereignis heraus angehalten: freigeben, ohne auf ihr letztes Bild zu warten
            if (old is not null) ReleaseInBackground(old);
            await WaitForReleasesAsync().ConfigureAwait(false);

            var session = new CaptureSession(_camera, OnSessionFrame, OnSessionFailure);
            lock (_sync) _session = session;
            string? error;
            try
            {
                // Media Foundation im MTA des Thread-Pools öffnen, nicht im STA des Aufrufers (WPF)
                error = await Task.Run(() => session.OpenAsync(ct), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                ReleaseInBackground(session);
                await WaitForReleasesAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                // Sollte OpenAsync selbst abfangen – zur Sicherheit trotzdem keine Ausnahme nach außen
                error = CameraProblems.Describe(CameraProblems.FromException(ex), Name);
            }

            if (error is not null || !session.IsActive)
            {
                // Nicht gestartet, beim Start ausgefallen oder aus einem Bild-Ereignis heraus schon wieder angehalten
                ReleaseInBackground(session);
                await WaitForReleasesAsync().ConfigureAwait(false);
                failure = error;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
        // Erst nach dem Freigeben der Sperre melden: Ein Empfänger darf dann sogar synchron neu starten
        if (failure is not null) Raise(Failed, failure, nameof(Failed));
    }

    public Task StopAsync()
    {
        if (t_callbackOwner == this)
        {
            // Aus einem eigenen Ereignis heraus: sofort keine Bilder mehr, Freigabe im Hintergrund
            CaptureSession? session;
            lock (_sync) session = _session;
            if (session is not null) ReleaseInBackground(session);
            return Task.CompletedTask;
        }
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
            CaptureSession? session;
            lock (_sync)
            {
                session = _session;
                _session = null;
            }
            // Wartet ein laufendes Bild-Ereignis ab: Nach dem Stopp kommt sicher kein Bild mehr
            if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
            await WaitForReleasesAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Schaltet die Sitzung sofort ab und gibt sie im Hintergrund frei (ohne auf ihr laufendes Bild-Ereignis zu warten).</summary>
    private void ReleaseInBackground(CaptureSession session)
    {
        session.Deactivate();
        lock (_sync)
        {
            if (_session == session) _session = null;
            var release = Task.Run(() => session.ReleaseNowAsync().AsTask());
            _releasing = _releasing.IsCompleted ? release : Task.WhenAll(_releasing, release);
        }
    }

    /// <summary>Wartet (kurz) auf Freigaben im Hintergrund, damit ein neuer Start die Kamera nicht mit der alten Sitzung teilen muss.</summary>
    private async Task WaitForReleasesAsync()
    {
        Task releasing;
        lock (_sync) releasing = _releasing;
        if (releasing.IsCompleted) return;
        var done = await Task.WhenAny(releasing, Task.Delay(ReleaseWaitTimeout)).ConfigureAwait(false);
        if (done != releasing) Trace.TraceWarning($"Lupe: Die vorige Sitzung der Kamera „{Name}“ ist noch nicht freigegeben – mache trotzdem weiter.");
    }

    private void OnSessionFrame(CaptureSession session, PixelImage image) => Raise(FrameArrived, image, nameof(FrameArrived));

    private void OnSessionFailure(CaptureSession session, string message)
    {
        ReleaseInBackground(session);
        Raise(Failed, message, nameof(Failed));
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
