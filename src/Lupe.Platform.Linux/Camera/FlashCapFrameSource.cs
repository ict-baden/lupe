using System.Diagnostics;
using FlashCap;
using Lupe.Core.Camera;
using Lupe.Core.Model;
using Lupe.Core.Resources;

namespace Lupe.Platform.Linux.Camera;

/// <summary>
/// Eine V4L2-Kamera über FlashCap als <see cref="IFrameSource"/>. Jeder Start öffnet das Gerät neu, jeder Stopp gibt es
/// frei (Kamera-LED aus, andere Programme bekommen sie wieder). Probleme (belegt, keine Berechtigung, abgezogen) kommen
/// über <see cref="Failed"/>.
/// </summary>
internal sealed class FlashCapFrameSource : IFrameSource
{
    /// <summary>So lange darf nach dem Start kein Bild kommen, bevor die Kamera als ausgefallen gilt.</summary>
    internal static TimeSpan StallTimeout { get; set; } = TimeSpan.FromSeconds(8);

    private readonly CameraInfo _camera;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly Lock _sync = new();
    private CaptureDevice? _device;   // unter _sync
    private RawFormat _format;
    private int _width, _height;
    private Timer? _watchdog;
    private long _lastFrameTicks;
    private int _generation;
    private volatile bool _disposed;

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
            lock (_sync)
                if (_device is not null) return;

            var descriptor = LinuxCameraProvider.Descriptors().FirstOrDefault(d => LinuxCameraProvider.IdOf(d) == _camera.Id)
                ?? LinuxCameraProvider.Descriptors().FirstOrDefault(d => CameraList.CleanName(d.Name) == Name);
            var format = descriptor is null ? null : LinuxCameraProvider.ChooseFormat(descriptor);
            if (descriptor is null || format is null)
            {
                failure = string.Format(Strings.Linux_Camera_NotConnected, Name);
                return;
            }

            int generation;
            lock (_sync)
            {
                _format = LinuxCameraProvider.RawFormatOf(format.PixelFormat);
                _width = format.Width;
                _height = format.Height;
                generation = ++_generation;
            }
            CaptureDevice device;
            try
            {
                device = await descriptor.OpenAsync(format, TranscodeFormats.DoNotTranscode, isScattering: false,
                    maxQueuingFrames: 1, scope => OnFrame(scope, generation), ct).ConfigureAwait(false);
                await device.StartAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failure = Describe(ex, Name);
                return;
            }
            lock (_sync)
            {
                _device = device;
                Interlocked.Exchange(ref _lastFrameTicks, Environment.TickCount64);
                _watchdog = new Timer(_ => CheckAlive(generation), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }
        }
        finally
        {
            _lifecycle.Release();
            if (failure is not null) Core.Camera.SafeEvents.Raise(Failed, this, failure, nameof(Failed));
        }
    }

    public Task StopAsync()
    {
        if (t_callbackOwner == this)
        {
            // Aus einem eigenen Bild-Ereignis heraus: nicht auf FlashCap warten (das wartet auf genau dieses Ereignis)
            var device = Detach();
            if (device is not null) _ = Task.Run(() => ReleaseAsync(device));
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
            var device = Detach();
            if (device is not null) await ReleaseAsync(device).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private CaptureDevice? Detach()
    {
        lock (_sync)
        {
            var device = _device;
            _device = null;
            _generation++;
            _watchdog?.Dispose();
            _watchdog = null;
            return device;
        }
    }

    private static async Task ReleaseAsync(CaptureDevice device)
    {
        try
        {
            await device.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Lupe: Kamera ließ sich nicht anhalten: {ex.Message}");
        }
        try
        {
            await device.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Lupe: Kamera ließ sich nicht freigeben: {ex.Message}");
        }
    }

    private void OnFrame(PixelBufferScope scope, int generation)
    {
        RawFormat format;
        int width, height;
        lock (_sync)
        {
            if (generation != _generation) return;
            format = _format;
            width = _width;
            height = _height;
        }
        PixelImage? image;
        try
        {
            var data = scope.Buffer.ReferImage();
            image = FrameConversion.ToBgra(format, width, height, data.AsSpan());
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Lupe: Kamerabild unlesbar: {ex.Message}");
            return;
        }
        finally
        {
            scope.ReleaseNow();
        }
        if (image is null) return;
        Interlocked.Exchange(ref _lastFrameTicks, Environment.TickCount64);
        var previous = t_callbackOwner;
        t_callbackOwner = this;
        try
        {
            Core.Camera.SafeEvents.Raise(FrameArrived, this, image, nameof(FrameArrived));
        }
        finally
        {
            t_callbackOwner = previous;
        }
    }

    /// <summary>Gerätedatei weg oder lange kein Bild: Kamera abgezogen oder hängt – melden und freigeben.</summary>
    private void CheckAlive(int generation)
    {
        string? problem = null;
        lock (_sync)
        {
            if (generation != _generation || _device is null) return;
            if (_camera.Id.StartsWith("/dev/", StringComparison.Ordinal) && !File.Exists(_camera.Id))
                problem = string.Format(Strings.Linux_Camera_Disconnected, Name);
            else if (Environment.TickCount64 - Interlocked.Read(ref _lastFrameTicks) > StallTimeout.TotalMilliseconds)
                problem = string.Format(Strings.Linux_Camera_Stalled, Name);
        }
        if (problem is null) return;
        var device = Detach();
        if (device is not null) _ = Task.Run(() => ReleaseAsync(device));
        Core.Camera.SafeEvents.Raise(Failed, this, problem, nameof(Failed));
    }

    /// <summary>Übersetzt die Ausnahmen von FlashCap/V4L2 (errno im Text) in eine deutsche Meldung.</summary>
    internal static string Describe(Exception ex, string name)
    {
        string text = ex.Message;
        int? errno = null;
        int at = text.IndexOf("Code=", StringComparison.Ordinal);
        if (at >= 0)
        {
            var digits = new string(text[(at + 5)..].TakeWhile(char.IsAsciiDigit).ToArray());
            if (int.TryParse(digits, out int n)) errno = n;
        }
        if (ex is UnauthorizedAccessException || errno is 13 or 1)
            return string.Format(Strings.Linux_Camera_AccessDenied, name);
        if (errno == 16 || text.Contains("busy", StringComparison.OrdinalIgnoreCase))
            return string.Format(Strings.Linux_Camera_InUse, name);
        if (errno is 2 or 19 or 6 || ex is FileNotFoundException or DirectoryNotFoundException)
            return string.Format(Strings.Linux_Camera_NotConnected, name);
        return string.Format(Strings.Linux_Camera_StartFailed, name, text.Trim());
    }
}
