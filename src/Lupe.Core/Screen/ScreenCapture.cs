using Lupe.Core.Model;

namespace Lupe.Core.Screen;

/// <summary>Rechteck in physischen Bildschirmpixeln (Koordinaten des virtuellen Desktops über alle Monitore).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

public sealed record MonitorArea(PixelRect Bounds, PixelRect WorkArea, double Scale, bool IsPrimary, string Name);

/// <summary>Bildschirmfotos für den Scan eines Ausschnitts – DPI-genau über alle Monitore.</summary>
public static class ScreenCapture
{
    public static IReadOnlyList<MonitorArea> Monitors() => throw new NotImplementedException();

    /// <summary>Der ganze virtuelle Desktop (alle Monitore).</summary>
    public static PixelRect VirtualScreen => throw new NotImplementedException();

    public static PixelImage Capture(PixelRect area) => throw new NotImplementedException();
}
