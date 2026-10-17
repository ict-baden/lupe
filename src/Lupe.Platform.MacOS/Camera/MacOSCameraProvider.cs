using System.Diagnostics;
using FlashCap;
using FlashCap.Devices;
using Lupe.Core.Camera;
using Lupe.Core.Platform;

namespace Lupe.Platform.MacOS.Camera;

/// <summary>Webcams unter macOS über FlashCap (AVFoundation).</summary>
public sealed class MacOSCameraProvider : ICameraProvider
{
    internal static TimeSpan ListTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public async Task<IReadOnlyList<CameraInfo>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS()) return [];
        // Ohne Erlaubnis gar nicht erst fragen; die Kamera-Seite zeigt den Hinweis aus MacOSPermissions
        if (MacOSPermissions.Camera() is CameraAuthorization.Denied or CameraAuthorization.Restricted) return [];
        List<CaptureDeviceDescriptor> descriptors;
        try
        {
            // AVFoundation kann beim Auflisten hängen (auf GitHub-Runnern ohne Kamera und ohne Dialog dauerhaft) –
            // dann wie „keine Kamera“, statt die Kamera-Seite ewig suchen zu lassen
            descriptors = await Task.Run(Enumerate, ct).WaitAsync(ListTimeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Trace.TraceWarning($"Lupe: Kameras nach {ListTimeout.TotalSeconds:0} s noch nicht aufgelistet – keine Kamera angenommen.");
            return [];
        }
        ct.ThrowIfCancellationRequested();
        var names = CameraList.MakeUnique(descriptors.Select(d => d.Name).ToList());
        return descriptors.Select((d, i) => new CameraInfo(IdOf(d), names[i])).ToList();
    }

    public IFrameSource Open(CameraInfo camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        return new FlashCapFrameSource(camera);
    }

    internal static string IdOf(CaptureDeviceDescriptor d) => d.Identity?.ToString() ?? d.Name;

    /// <summary>Alle Kameras mit mindestens einem brauchbaren Format; Fehler → leere Liste.</summary>
    internal static List<CaptureDeviceDescriptor> Enumerate()
    {
        if (!OperatingSystem.IsMacOS()) return [];
        try
        {
            return new AVFoundationDevices().EnumerateDescriptors()
                .Where(d => d.Characteristics.Any(c => CameraFormats.IsUsable(c.PixelFormat)))
                .ToList();
        }
        catch (Exception ex)
        {
            // AVFoundation fehlt, keine Berechtigung, FlashCap-interne Fehler: wie „keine Kamera“
            Trace.TraceWarning($"Lupe: Kameras nicht auflistbar: {ex}");
            return [];
        }
    }
}

/// <summary>Formatwahl für FlashCap-Formate.</summary>
internal static class CameraFormats
{
    /// <summary>Was Lupe umwandeln kann: alles, was FlashCap als BMP liefert, und JPEG (über ImageIO).</summary>
    public static bool IsUsable(PixelFormats format) => format is PixelFormats.NV12 or PixelFormats.YUYV or PixelFormats.UYVY
        or PixelFormats.RGB24 or PixelFormats.RGB32 or PixelFormats.ARGB32 or PixelFormats.JPEG;

    /// <summary>Name wie in <see cref="FormatChooser.SubtypeCost"/>.</summary>
    public static string Subtype(PixelFormats format) => format switch
    {
        PixelFormats.NV12 => "NV12",
        PixelFormats.YUYV => "YUY2",
        PixelFormats.UYVY => "UYVY",
        PixelFormats.RGB24 => "RGB24",
        PixelFormats.RGB32 => "RGB32",
        PixelFormats.ARGB32 => "ARGB32",
        PixelFormats.JPEG => "MJPG",
        _ => format.ToString().ToUpperInvariant(),
    };

    /// <summary>Das beste brauchbare Format (~1280×720, flüssig); null, wenn keines passt.</summary>
    public static VideoCharacteristics? Choose(IReadOnlyList<VideoCharacteristics> all)
    {
        var usable = all.Where(c => IsUsable(c.PixelFormat)).ToList();
        var candidates = usable.Select(c => new VideoFormatCandidate(c.Width, c.Height, Fps(c), Subtype(c.PixelFormat))).ToList();
        int best = FormatChooser.Choose(candidates);
        return best < 0 ? null : usable[best];
    }

    private static double Fps(VideoCharacteristics c) =>
        c.FramesPerSecond.Denominator == 0 ? 0 : (double)c.FramesPerSecond.Numerator / c.FramesPerSecond.Denominator;
}
