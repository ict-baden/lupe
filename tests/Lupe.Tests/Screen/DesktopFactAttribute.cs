using System.Diagnostics;
using Lupe.Core.Model;
using Lupe.Core.Screen;
using Lupe.Tests.TestSupport;

namespace Lupe.Tests.Screen;

/// <summary>
/// Test, der ein echtes Bildschirmfoto braucht. Vorher wird einmal geprüft, ob BitBlt in dieser Sitzung den Inhalt eines
/// eigenen Testfensters liefert – in Sitzung 0 (Dienste, unsichtbarer Desktop) ist das nicht so; dann wird übersprungen.
/// </summary>
public sealed class DesktopFactAttribute : FactAttribute
{
    public DesktopFactAttribute()
    {
        if (DesktopProbe.SkipReason is { } reason) Skip = reason;
    }
}

/// <summary>
/// Test für das Verhalten, wenn Windows das Bildschirmfoto verweigert (Sitzung 0). Ist der Bildschirm nur verdeckt, läuft
/// BitBlt durch – dann wird übersprungen.
/// </summary>
public sealed class NoDesktopFactAttribute : FactAttribute
{
    public NoDesktopFactAttribute()
    {
        if (!DesktopProbe.CaptureRefused)
            Skip = DesktopProbe.SkipReason is null
                ? "Nur ohne nutzbaren Bildschirm – hier liefert BitBlt echten Inhalt."
                : "Nur wenn Windows das Bildschirmfoto verweigert – hier läuft BitBlt durch, zeigt aber nicht das Testfenster.";
    }
}

/// <summary>Tests, die den Desktop anfassen (Fenster obenauf, Tastatureingabe, Tastenkürzel), laufen nacheinander.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DesktopCollection
{
    public const string Name = "Desktop";
}

internal static class DesktopProbe
{
    private static readonly Lazy<(string? Reason, bool Refused)> Result = new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>null, wenn Bildschirmfotos hier echten Inhalt liefern, sonst der Grund zum Überspringen.</summary>
    public static string? SkipReason => Result.Value.Reason;

    /// <summary>Hat Windows das Bildschirmfoto abgelehnt (BitBlt scheiterte)?</summary>
    public static bool CaptureRefused => Result.Value.Refused;

    /// <summary>Vier gut unterscheidbare Farbfelder.</summary>
    public static PixelImage Pattern(int size = 64)
    {
        var px = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                (byte r, byte g, byte b) = (x < size / 2, y < size / 2) switch
                {
                    (true, true) => ((byte)230, (byte)20, (byte)40),
                    (false, true) => ((byte)20, (byte)200, (byte)60),
                    (true, false) => ((byte)30, (byte)60, (byte)220),
                    _ => ((byte)250, (byte)220, (byte)10),
                };
                int i = (y * size + x) * 4;
                px[i] = b;
                px[i + 1] = g;
                px[i + 2] = r;
                px[i + 3] = 255;
            }
        }
        return new PixelImage(size, size, px);
    }

    /// <summary>Anteil der Pixel, die (bis auf <paramref name="tolerance"/> je Kanal) übereinstimmen; Bild <paramref name="actual"/> ab Versatz.</summary>
    public static double Agreement(PixelImage actual, PixelImage expected, int offsetX = 0, int offsetY = 0, int tolerance = 6)
    {
        long same = 0, total = (long)expected.Width * expected.Height;
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                int ax = x + offsetX, ay = y + offsetY;
                if (ax < 0 || ay < 0 || ax >= actual.Width || ay >= actual.Height) continue;
                int a = ay * actual.Stride + ax * 4, e = y * expected.Stride + x * 4;
                if (Math.Abs(actual.Bgra[a] - expected.Bgra[e]) <= tolerance
                    && Math.Abs(actual.Bgra[a + 1] - expected.Bgra[e + 1]) <= tolerance
                    && Math.Abs(actual.Bgra[a + 2] - expected.Bgra[e + 2]) <= tolerance)
                    same++;
            }
        }
        return (double)same / total;
    }

    /// <summary>Liest einen QR-Code mit zxing-cpp direkt (unabhängig vom Baustein „decoding“).</summary>
    public static string? DecodeQr(PixelImage image) => ReferenceReader.ReadQr(image).FirstOrDefault()?.Text;

    /// <summary>Wo Testfenster hin sollen: in den Arbeitsbereich des Hauptmonitors, etwas eingerückt.</summary>
    public static (int X, int Y) Spot(int inset = 40)
    {
        var monitors = ScreenCapture.Monitors();
        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        var work = primary?.WorkArea ?? new PixelRect(0, 0, 800, 600);
        return (work.X + inset, work.Y + inset);
    }

    private static (string? Reason, bool Refused) Probe()
    {
        string session = $"Sitzung {Process.GetCurrentProcess().SessionId}";
        try
        {
            if (ScreenCapture.Monitors().Count == 0) return ($"Kein Monitor gefunden ({session}).", false);
            var pattern = Pattern();
            var (x, y) = Spot();
            using var window = TestImageWindow.Show(pattern, x, y);
            if (!ScreenCapture.TryCapture(window.Bounds, out var shot, out string? error))
                return ($"BitBlt vom Bildschirm liefert hier keinen Inhalt ({session}, kein sichtbarer Desktop): {error}", true);
            double agreement = Agreement(shot, pattern);
            if (agreement < 0.98)
                return ($"BitBlt liefert hier nicht den Inhalt des Testfensters ({session}, nur {agreement:P0} gleich) – Desktop verdeckt oder nicht sichtbar.", false);
            return (null, false);
        }
        catch (Exception ex)
        {
            return ($"Bildschirmtest nicht möglich ({session}): {ex.Message}", false);
        }
    }
}
