using System.Diagnostics;
using System.Runtime.InteropServices;
using Lupe.Core.Camera;
using Lupe.Core.Model;
using Lupe.Core.Resources;

namespace Lupe.Platform.MacOS.Camera;

/// <summary>
/// Kamera über libLupeCamera (AVFoundation, Bilder als BGRA). Jeder Start öffnet das Gerät neu, jeder Stopp gibt es ganz
/// frei (grüne Kamera-LED aus). Bilder und Ausfälle kommen auf der Warteschlange der Bibliothek.
/// </summary>
internal sealed class AVFoundationFrameSource : IFrameSource
{
    private readonly CameraInfo _camera;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private nint _handle;
    private GCHandle _self;
    private volatile bool _active;
    private volatile bool _disposed;
    private int _failedRaised;

    /// <summary>Gesetzt, während dieser Thread ein Ereignis dieser Quelle ausliefert (Stopp aus dem Ereignis: nicht warten).</summary>
    [ThreadStatic] private static AVFoundationFrameSource? t_callbackOwner;

    public AVFoundationFrameSource(CameraInfo camera)
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
            if (_handle != 0 && _active) return;
            Release();
            _failedRaised = 0;
            // Erlaubnis-Dialog und startRunning blockieren – nicht auf dem Aufrufer-Thread
            failure = await Task.Run(Open, ct).ConfigureAwait(false);
            if (failure is not null) Release();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Release();
            throw;
        }
        finally
        {
            _lifecycle.Release();
        }
        if (failure is not null) RaiseFailed(failure);
    }

    private unsafe string? Open()
    {
        if (!OperatingSystem.IsMacOS()) return Strings.Mac_NotHere_Camera;
        if (!NativeCamera.Available) return string.Format(Strings.Mac_Camera_LibraryMissing, Name);
        if (MacOSPermissions.CameraMessage(NativeCamera.RequestAccess()) is { } denied) return denied;

        var formats = NativeCamera.Formats(_camera.Id);
        if (formats is null) return NativeCamera.Describe(NativeCamera.NotFound, Name);
        int best = FormatChooser.Choose(formats);
        var (width, height) = best < 0 ? (0, 0) : (formats[best].Width, formats[best].Height);

        _self = GCHandle.Alloc(this);
        nint handle = NativeCamera.Open(_camera.Id, width, height, &OnFrame, &OnError, GCHandle.ToIntPtr(_self), out int error);
        if (handle == 0)
        {
            Trace.TraceWarning($"Lupe: Kamera {Name} nicht startbar (Code {error}).");
            return NativeCamera.Describe(error, Name);
        }
        _handle = handle;
        _active = true;
        NativeCamera.Start(handle);
        return null;
    }

    [UnmanagedCallersOnly]
    private static unsafe void OnFrame(nint context, byte* bgra, int width, int height, int bytesPerRow)
    {
        if (GCHandle.FromIntPtr(context).Target is not AVFoundationFrameSource source) return;
        source.Deliver(bgra, width, height, bytesPerRow);
    }

    [UnmanagedCallersOnly]
    private static void OnError(nint context, int code)
    {
        if (GCHandle.FromIntPtr(context).Target is not AVFoundationFrameSource source) return;
        source.Fail(code);
    }

    private unsafe void Deliver(byte* bgra, int width, int height, int bytesPerRow)
    {
        if (!_active || _disposed || width <= 0 || height <= 0 || bytesPerRow < width * 4) return;
        PixelImage image;
        try
        {
            image = Copy(new ReadOnlySpan<byte>(bgra, checked(bytesPerRow * height)), width, height, bytesPerRow);
        }
        catch (OverflowException)
        {
            return;
        }
        RaiseOnCallback(() => SafeEvents.Raise(FrameArrived, this, image, nameof(FrameArrived)));
    }

    /// <summary>BGRA mit beliebigem Zeilenabstand → eigenes, dichtes Bild mit Alpha 255.</summary>
    internal static PixelImage Copy(ReadOnlySpan<byte> source, int width, int height, int bytesPerRow)
    {
        int stride = checked(width * 4);
        var bgra = GC.AllocateUninitializedArray<byte>(checked(stride * height));
        for (int y = 0; y < height; y++)
        {
            var row = bgra.AsSpan(y * stride, stride);
            source.Slice(y * bytesPerRow, stride).CopyTo(row);
            for (int x = 3; x < stride; x += 4) row[x] = 255;
        }
        return new PixelImage(width, height, bgra, stride);
    }

    private void Fail(int code)
    {
        if (!_active || _disposed) return;
        _active = false;
        Trace.TraceWarning($"Lupe: Kamera {Name} ausgefallen (Code {code}).");
        RaiseOnCallback(() => RaiseFailed(NativeCamera.Describe(code, Name)));
    }

    private void RaiseOnCallback(Action raise)
    {
        var previous = t_callbackOwner;
        t_callbackOwner = this;
        try
        {
            raise();
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
            // Aus dem eigenen Ereignis heraus: die Bibliothek wartet beim Anhalten auf genau diesen Rückruf
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
            await Task.Run(Release).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Hält an, gibt das Gerät frei; danach kommt kein Rückruf mehr, erst dann fällt der GCHandle.</summary>
    private void Release()
    {
        _active = false;
        nint handle = _handle;
        _handle = 0;
        if (handle != 0)
        {
            try
            {
                NativeCamera.Close(handle);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Lupe: Kamera {Name} ließ sich nicht freigeben: {ex.Message}");
            }
        }
        if (_self.IsAllocated) _self.Free();
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
