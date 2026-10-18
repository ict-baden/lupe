using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Lupe.Core.Model;
using Lupe.Core.Platform;
using Lupe.Core.Resources;

namespace Lupe.Core.Screen;

/// <summary>Rechteck in physischen Bildschirmpixeln (Koordinaten des virtuellen Desktops über alle Monitore).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>Ein Monitor: ganze Fläche, Arbeitsbereich (ohne Taskleiste), Skalierung (1,0 = 100 %, 1,5 = 150 %), Hauptmonitor, Anzeigename.</summary>
public sealed record MonitorArea(PixelRect Bounds, PixelRect WorkArea, double Scale, bool IsPrimary, string Name);

/// <summary>Bildschirmfotos für den Scan eines Ausschnitts über die aktive Plattform (<see cref="LupePlatform.Current"/>).</summary>
/// <remarks>
/// Alle Koordinaten sind physische Pixel des virtuellen Desktops (der Hauptmonitor beginnt bei 0,0; Monitore links
/// oder oberhalb davon haben negative Koordinaten). Das gilt unabhängig davon, wie das aufrufende Programm
/// DPI-eingestellt ist: Die Aufrufe laufen intern immer mit Per-Monitor-DPI.
/// </remarks>
public static class ScreenCapture
{
    /// <summary>Alle Monitore, von links nach rechts (bei gleicher Position von oben nach unten).</summary>
    public static IReadOnlyList<MonitorArea> Monitors() => LupePlatform.Current.Screen.Monitors();

    /// <summary>Der ganze virtuelle Desktop (alle Monitore).</summary>
    public static PixelRect VirtualScreen => LupePlatform.Current.Screen.VirtualScreen;

    /// <summary>
    /// Bildschirmfoto des Rechtecks (auch über Monitorgrenzen hinweg) als BGRA mit Alpha 255, genau so groß wie
    /// <paramref name="area"/>. Bereiche außerhalb aller Monitore bleiben schwarz. Lässt das System gerade kein
    /// Bildschirmfoto zu (gesperrter Desktop, Anmeldebildschirm, keine Berechtigung), kommt ein schwarzes Bild –
    /// wer den Grund wissen will, nimmt <see cref="TryCapture"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Breite oder Höhe ≤ 0 oder das Bild wäre größer als 2 GB.</exception>
    public static PixelImage Capture(PixelRect area)
    {
        if (TryCapture(area, out var image, out string? error)) return image;
        Debug.WriteLine($"Lupe: {error}");
        return Blank(area.Width, area.Height);
    }

    /// <summary>Wie <see cref="Capture"/>, meldet aber, wenn das System kein Bildschirmfoto zulässt (Grund auf Deutsch in <paramref name="error"/>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Breite oder Höhe ≤ 0 oder das Bild wäre größer als 2 GB.</exception>
    public static bool TryCapture(PixelRect area, [NotNullWhen(true)] out PixelImage? image, out string? error)
    {
        Validate(area);
        return LupePlatform.Current.Screen.TryCapture(area, out image, out error);
    }

    /// <summary>Anzeigename eines Monitors: „Bildschirm 2“ oder „Bildschirm 2 (DELL U2720Q)“.</summary>
    public static string MonitorName(int number, string? friendlyName)
        => string.IsNullOrWhiteSpace(friendlyName)
            ? string.Format(Strings.Core_Screen_Monitor, number)
            : string.Format(Strings.Core_Screen_MonitorNamed, number, friendlyName.Trim());

    /// <summary>Wirft bei leerer oder zu großer Fläche (siehe <see cref="TryCapture"/>).</summary>
    public static void Validate(PixelRect area)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(area.Width, nameof(area));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(area.Height, nameof(area));
        if ((long)area.Width * area.Height * 4 > Array.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(area), $"{area.Width} × {area.Height} Pixel sind zu groß für ein Bildschirmfoto.");
    }

    internal static PixelImage Blank(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        MakeOpaque(pixels);
        return new PixelImage(width, height, pixels);
    }

    /// <summary>Setzt in jedem BGRA-Pixel den Alpha-Wert auf 255 (vektorisiert über 32-Bit-Wörter).</summary>
    public static void MakeOpaque(byte[] bgra)
    {
        var words = MemoryMarshal.Cast<byte, uint>(bgra.AsSpan());
        const uint alpha = 0xFF000000u; // Byte 3 (A) ist bei Little Endian das höchste Byte
        for (int i = 0; i < words.Length; i++) words[i] |= alpha;
    }
}
