using System.Diagnostics;
using Lupe.Core.Model;

namespace Lupe.Core.Camera;

/// <summary>
/// Echte Kamera als <see cref="IFrameSource"/>. Jeder Start öffnet eine neue <see cref="CaptureSession"/>
/// (MediaCapture + MediaFrameReader), jeder Stopp gibt sie vollständig frei – die Kamera-LED geht dann aus und andere
/// Programme bekommen die Kamera wieder.
/// </summary>
internal sealed class MediaCaptureFrameSource : IFrameSource
{
    private readonly CameraInfo _camera;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _sync = new();
    private CaptureSession? _session;
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
        bool inCallback = t_callbackOwner == this;
        string? failure = null;
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CaptureSession? old;
            lock (_sync) old = _session;
            if (old is { IsActive: true }) return;
            if (old is not null)
            {
                if (inCallback)
                {
                    // Aus einem eigenen Bild-Ereignis heraus würde die Freigabe auf genau dieses Ereignis warten
                    lock (_sync) _session = null;
                    _ = Task.Run(() => old.DisposeAsync().AsTask());
                }
                else
                {
                    await CloseAsync(old).ConfigureAwait(false);
                }
            }

            var session = new CaptureSession(_camera, OnSessionFrame, OnSessionFailure);
            lock (_sync) _session = session;
            string? error;
            try
            {
                // Media Foundation im MTA des Thread-Pools öffnen, nicht im STA des Aufrufers (WPF)
                error = await Task.Run(() => session.OpenAsync(ct), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await CloseAsync(session).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                // Sollte OpenAsync selbst abfangen – zur Sicherheit trotzdem keine Ausnahme nach außen
                error = CameraProblems.Describe(CameraProblems.FromException(ex), Name);
            }

            if (error is not null)
            {
                await CloseAsync(session).ConfigureAwait(false);
                failure = error;
            }
            else if (!session.IsActive)
            {
                // Schon beim Start ausgefallen (bereits gemeldet) oder aus einem Ereignis heraus angehalten
                await CloseAsync(session).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
        if (failure is not null) Raise(Failed, failure, nameof(Failed));
    }

    public Task StopAsync()
    {
        if (t_callbackOwner == this)
        {
            // Aus einem eigenen Ereignis heraus: sofort keine Bilder mehr, Freigabe im Hintergrund
            CaptureSession? session;
            lock (_sync) session = _session;
            if (session is not null)
            {
                session.Deactivate();
                _ = Task.Run(() => CloseInBackgroundAsync(session));
            }
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
            lock (_sync) session = _session;
            if (session is not null) await CloseAsync(session).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Gibt die Sitzung frei. Nur unter <see cref="_lifecycle"/>.</summary>
    private async Task CloseAsync(CaptureSession session)
    {
        lock (_sync)
        {
            if (_session == session) _session = null;
        }
        await session.DisposeAsync().ConfigureAwait(false);
    }

    private async Task CloseInBackgroundAsync(CaptureSession session)
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await CloseAsync(session).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Lupe: Freigeben der Kamera fehlgeschlagen: {ex}");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private void OnSessionFrame(CaptureSession session, PixelImage image) => Raise(FrameArrived, image, nameof(FrameArrived));

    private void OnSessionFailure(CaptureSession session, string message)
    {
        Raise(Failed, message, nameof(Failed));
        _ = Task.Run(() => CloseInBackgroundAsync(session));
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
