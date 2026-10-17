using System.Diagnostics;
using FlashCap;
using Lupe.Core.Camera;
using Lupe.Core.Model;
using Lupe.Platform.MacOS.Imaging;

namespace Lupe.Platform.MacOS.Camera;

/// <summary>
/// Kamera über FlashCap (AVFoundation). Jeder Start öffnet das Gerät neu, jeder Stopp gibt es ganz frei (grüne
/// Kamera-LED aus). YUV-Formate rechnet FlashCap in RGB um und liefert ein BMP; JPEG dekodiert ImageIO.
/// </summary>
internal sealed class FlashCapFrameSource : IFrameSource
{
    private readonly CameraInfo _camera;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CaptureDevice? _device;
    private volatile bool _active;
    private volatile bool _disposed;
    private int _failedRaised;

    /// <summary>Gesetzt, während dieser Thread ein Ereignis dieser Quelle ausliefert (Stopp aus dem Ereignis: nicht warten).</summary>
    [ThreadStatic] private static FlashCapFrameSource? t_callbackOwner;

    public FlashCapFrameSource(CameraInfo camera)
    {
        _camera = camera;
        Name = CameraList.CleanName(camera.Name);
    }

    public string Name { get; }

    public event EventHandler<PixelImage>? FrameArrived;
    public event EventHandler<string>? Failed;

    public async Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string? failure = null;
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_device is not null && _active) return;
            await ReleaseAsync().ConfigureAwait(false);
            _failedRaised = 0;
            failure = await OpenAsync(ct).ConfigureAwait(false);
            if (failure is not null) await ReleaseAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
        if (failure is not null) RaiseFailed(failure);
    }

    private async Task<string?> OpenAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS()) return MacOS.NotHere("Die Kamera");
        if (MacOSPermissions.CameraMessage(MacOSPermissions.Camera()) is { } denied) return denied;

        var descriptor = await Task.Run(() => MacOSCameraProvider.Enumerate()
            .FirstOrDefault(d => string.Equals(MacOSCameraProvider.IdOf(d), _camera.Id, StringComparison.Ordinal)), ct).ConfigureAwait(false);
        if (descriptor is null) return $"Die Kamera „{Name}“ ist nicht angeschlossen.";
        var format = CameraFormats.Choose(descriptor.Characteristics);
        if (format is null) return $"Die Kamera „{Name}“ bietet kein Bildformat an, das Lupe lesen kann.";

        try
        {
            _active = true;
            var device = await descriptor.OpenAsync(format, TranscodeFormats.Auto, OnPixelBuffer, ct).ConfigureAwait(false);
            _device = device;
            await device.StartAsync(ct).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await ReleaseAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Lupe: Kamera {Name} nicht startbar: {ex}");
            // Berechtigung erst jetzt verweigert (Systemdialog beim ersten Öffnen)?
            return MacOSPermissions.CameraMessage(MacOSPermissions.Camera())
                ?? $"Die Kamera „{Name}“ ließ sich nicht starten – vielleicht verwendet sie gerade ein anderes Programm. ({ex.Message.Trim()})";
        }
    }

    private void OnPixelBuffer(PixelBufferScope scope)
    {
        if (!_active || _disposed) return;
        PixelImage? image;
        try
        {
            var data = scope.Buffer.ReferImage();
            var span = data.AsSpan();
            if (DibConverter.IsBitmap(span)) image = DibConverter.Convert(span);
            else if (DibConverter.IsJpeg(span)) image = ImageIOReader.Decode(span, Name, int.MaxValue, largestFrame: false);
            else image = null;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException)
        {
            Trace.TraceWarning($"Lupe: Kamerabild nicht lesbar: {ex.Message}");
            image = null;
        }
        finally
        {
            scope.ReleaseNow();
        }
        if (image is null || !_active) return;

        var previous = t_callbackOwner;
        t_callbackOwner = this;
        try
        {
            SafeEvents.Raise(FrameArrived, this, image, nameof(FrameArrived));
        }
        finally
        {
            t_callbackOwner = previous;
        }
    }

    public Task StopAsync()
    {
        _active = false;
        if (t_callbackOwner == this)
        {
            // Aus dem eigenen Bild-Ereignis heraus: FlashCap wartet beim Freigeben auf genau dieses Ereignis
            _ = Task.Run(StopCoreAsync);
            return Task.CompletedTask;
        }
        return StopCoreAsync();
    }

    private async Task StopCoreAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await ReleaseAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task ReleaseAsync()
    {
        _active = false;
        var device = _device;
        _device = null;
        if (device is null) return;
        try
        {
            await device.StopAsync(default).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Lupe: Kamera {Name} ließ sich nicht anhalten: {ex.Message}");
        }
        try
        {
            await device.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Lupe: Kamera {Name} ließ sich nicht freigeben: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await StopAsync().ConfigureAwait(false);
    }

    private void RaiseFailed(string message)
    {
        if (Interlocked.Exchange(ref _failedRaised, 1) != 0) return;
        SafeEvents.Raise(Failed, this, message, nameof(Failed));
    }
}
