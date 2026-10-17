using System.Diagnostics.CodeAnalysis;
using Lupe.Core.Model;
using Lupe.Core.Platform;
using Lupe.Core.Screen;

namespace Lupe.Platform.Linux.Screen;

/// <summary>Bildschirmfotos unter Linux – noch nicht umgesetzt.</summary>
public sealed class LinuxScreenCapture : IScreenCapture
{
    public IReadOnlyList<MonitorArea> Monitors() => [];
    public PixelRect VirtualScreen => default;

    public bool TryCapture(PixelRect area, [NotNullWhen(true)] out PixelImage? image, out string? error)
    {
        image = null;
        error = "Bildschirmfotos sind unter Linux noch nicht verfügbar.";
        return false;
    }
}
