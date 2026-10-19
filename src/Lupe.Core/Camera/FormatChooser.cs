// SPDX-License-Identifier: MIT
namespace Lupe.Core.Camera;

/// <summary>Ein Videoformat, das eine Kamera anbietet (Ausschnitt aus <c>MediaFrameFormat</c>).</summary>
/// <param name="Subtype">Medien-Untertyp wie NV12, YUY2, MJPG, RGB24.</param>
internal readonly record struct VideoFormatCandidate(int Width, int Height, double FramesPerSecond, string Subtype);

/// <summary>
/// Wählt das Kameraformat: etwa 1280×720 mit flüssiger Bildrate. Größer bringt der Erkennung kaum etwas, kostet aber
/// Zeit (ein 4K-Bild braucht ein Vielfaches); deutlich kleiner verliert kleine Codes.
/// </summary>
internal static class FormatChooser
{
    public const int TargetWidth = 1280;
    public const int TargetHeight = 720;

    /// <summary>Index des besten Formats; -1, wenn keines brauchbar ist. Bei Gleichstand gewinnt das erste.</summary>
    public static int Choose(IReadOnlyList<VideoFormatCandidate> formats)
    {
        int best = -1;
        double bestScore = double.PositiveInfinity;
        for (int i = 0; i < formats.Count; i++)
        {
            double score = Cost(formats[i]);
            if (score < bestScore)
            {
                best = i;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>Kosten eines Formats, kleiner ist besser; unbrauchbar = +∞.</summary>
    public static double Cost(VideoFormatCandidate f)
    {
        if (f.Width <= 0 || f.Height <= 0) return double.PositiveInfinity;

        // Größe: Abstand zur Zielfläche auf logarithmischer Skala, größer etwas teurer als kleiner
        double ratio = (double)f.Width * f.Height / ((double)TargetWidth * TargetHeight);
        double size = Math.Abs(Math.Log2(ratio));
        if (ratio > 1) size *= 1.25;

        // Bildrate: unter 15 B/s ruckelt die Vorschau, mehr als 30 B/s kostet nur Rechenzeit
        double fps = f.FramesPerSecond;
        double rate = !double.IsFinite(fps) || fps <= 0 ? 0.5
            : fps < 10 ? 3.0
            : fps < 14.5 ? 1.5
            : fps < 24 ? 0.4
            : fps > 31 ? 0.15
            : 0.0;

        return size + rate + SubtypeCost(f.Subtype);
    }

    /// <summary>Unkomprimiert ist am billigsten umzuwandeln, MJPG muss erst dekodiert werden, H.264 &amp; Co. meiden.</summary>
    public static double SubtypeCost(string? subtype) => subtype?.Trim().ToUpperInvariant() switch
    {
        "NV12" or "YUY2" => 0.0,
        "BGRA8" or "RGB32" or "ARGB32" or "RGB24" or "YV12" or "I420" or "IYUV" or "UYVY" => 0.1,
        "MJPG" => 0.3,
        "L8" or "L16" or "Y8" or "GRAY8" => 2.0,
        "H264" or "H265" or "HEVC" or "VP80" or "VP90" or "D16" => 1.5,
        _ => 1.0,
    };

    /// <summary>
    /// Ausgabegröße für den Frame-Reader: höchstens 1280×720 (Hochformat 720×1280), Seitenverhältnis bleibt, gerade
    /// Kantenlängen. Kleinere Formate bleiben unverändert. Unbekannte Größe (0) → (0, 0) = nicht skalieren.
    /// </summary>
    public static (int Width, int Height) OutputSize(int width, int height)
    {
        if (width <= 0 || height <= 0) return (0, 0);
        int longSide = Math.Max(width, height), shortSide = Math.Min(width, height);
        double factor = Math.Min(1.0, Math.Min((double)TargetWidth / longSide, (double)TargetHeight / shortSide));
        if (factor >= 1.0) return (width, height);
        int w = Math.Max(2, (int)Math.Round(width * factor / 2) * 2);
        int h = Math.Max(2, (int)Math.Round(height * factor / 2) * 2);
        return (w, h);
    }
}
