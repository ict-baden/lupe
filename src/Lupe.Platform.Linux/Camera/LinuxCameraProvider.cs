// SPDX-License-Identifier: MIT
using System.Diagnostics;
using FlashCap;
using FlashCap.Devices;
using Lupe.Core.Camera;
using Lupe.Core.Platform;

namespace Lupe.Platform.Linux.Camera;

/// <summary>Webcams unter Linux über FlashCap (V4L2, /dev/video*).</summary>
public sealed class LinuxCameraProvider : ICameraProvider
{
    public Task<IReadOnlyList<CameraInfo>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.Run<IReadOnlyList<CameraInfo>>(() =>
        {
            var usable = Descriptors().Where(d => ChooseFormat(d) is not null).ToList();
            var names = CameraList.MakeUnique(usable.Select(d => (string?)d.Name).ToList());
            return usable.Select((d, i) => new CameraInfo(IdOf(d), names[i])).ToList();
        }, ct);
    }

    public IFrameSource Open(CameraInfo camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        return new FlashCapFrameSource(camera);
    }

    /// <summary>Alle V4L2-Geräte; ohne V4L2 (kein Linux, kein /dev/video*) leer.</summary>
    internal static IReadOnlyList<CaptureDeviceDescriptor> Descriptors()
    {
        if (!OperatingSystem.IsLinux()) return [];
        try
        {
            return new V4L2Devices().EnumerateDescriptors().ToList();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Lupe: Kameras ließen sich nicht auflisten: {ex.Message}");
            return [];
        }
    }

    internal static string IdOf(CaptureDeviceDescriptor d) => d.Identity?.ToString() ?? d.Name;

    /// <summary>Das beste Format (~1280×720, umwandelbar) oder null.</summary>
    internal static VideoCharacteristics? ChooseFormat(CaptureDeviceDescriptor d)
    {
        var formats = d.Characteristics.Where(c => RawFormatOf(c.PixelFormat) != RawFormat.Unknown).ToList();
        var candidates = formats.Select(c => new VideoFormatCandidate(c.Width, c.Height,
            c.FramesPerSecond.Denominator == 0 ? 0 : (double)c.FramesPerSecond.Numerator / c.FramesPerSecond.Denominator,
            SubtypeOf(c.PixelFormat))).ToList();
        int best = FormatChooser.Choose(candidates);
        return best < 0 ? null : formats[best];
    }

    internal static RawFormat RawFormatOf(PixelFormats format) => format switch
    {
        PixelFormats.YUYV => RawFormat.Yuyv,
        PixelFormats.UYVY => RawFormat.Uyvy,
        PixelFormats.NV12 => RawFormat.Nv12,
        PixelFormats.JPEG => RawFormat.Mjpeg,
        PixelFormats.RGB24 => RawFormat.Rgb24,
        PixelFormats.RGB32 or PixelFormats.ARGB32 => RawFormat.Bgra32,
        _ => RawFormat.Unknown,
    };

    /// <summary>Name des Formats in der Schreibweise von <see cref="FormatChooser.SubtypeCost"/>.</summary>
    internal static string SubtypeOf(PixelFormats format) => format switch
    {
        PixelFormats.YUYV => "YUY2",
        PixelFormats.UYVY => "UYVY",
        PixelFormats.NV12 => "NV12",
        PixelFormats.JPEG => "MJPG",
        PixelFormats.RGB24 => "RGB24",
        PixelFormats.RGB32 or PixelFormats.ARGB32 => "RGB32",
        _ => format.ToString(),
    };
}
