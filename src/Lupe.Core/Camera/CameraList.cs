using System.Text;
using Lupe.Core.Resources;

namespace Lupe.Core.Camera;

/// <summary>Ein Datenstrom einer Kameragruppe, auf das Nötige reduziert (testbar ohne Kamera).</summary>
/// <param name="IsColor">Farbbild (nicht Infrarot/Tiefe).</param>
/// <param name="IsVideo">Vorschau- oder Aufnahme-Datenstrom (nicht Foto).</param>
/// <param name="DeviceId">Geräte-Kennung des Datenstroms, falls bekannt.</param>
internal sealed record CameraSourceCandidate(bool IsColor, bool IsVideo, string? DeviceId);

/// <summary>Eine Kameragruppe (<c>MediaFrameSourceGroup</c>), auf das Nötige reduziert.</summary>
internal sealed record CameraCandidate(string Id, string? DisplayName, IReadOnlyList<CameraSourceCandidate> Sources);

/// <summary>Macht aus den Kameragruppen von Windows eine Auswahlliste für Menschen.</summary>
internal static class CameraList
{
    /// <summary>Name für Kameras ohne (brauchbaren) Namen.</summary>
    public static string FallbackName => Strings.Core_Camera_FallbackName;

    /// <summary>
    /// Nur Gruppen mit Farbvideo; dieselbe Kamera in mehreren Gruppen (z. B. zusätzlich in einer Windows-Hello-Gruppe mit
    /// Infrarot) nur einmal – die schlichteste Gruppe gewinnt; Namen bereinigt und eindeutig. Reihenfolge wie von Windows.
    /// </summary>
    public static IReadOnlyList<CameraInfo> Build(IEnumerable<CameraCandidate> candidates)
    {
        var usable = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Id) && c.Sources.Any(s => s.IsColor && s.IsVideo))
            .ToList();

        // Dieselbe Farbkamera in mehreren Gruppen: die mit den wenigsten Datenströmen behalten
        var keep = new List<CameraCandidate>();
        foreach (var candidate in usable)
        {
            var devices = ColorDevices(candidate);
            int existing = devices.Count == 0 ? -1 : keep.FindIndex(k => ColorDevices(k).Overlaps(devices));
            if (existing < 0) keep.Add(candidate);
            else if (candidate.Sources.Count < keep[existing].Sources.Count) keep[existing] = candidate;
        }

        var names = MakeUnique(keep.Select(k => k.DisplayName).ToList());
        return keep.Select((k, i) => new CameraInfo(k.Id, names[i])).ToList();
    }

    private static HashSet<string> ColorDevices(CameraCandidate candidate) =>
        candidate.Sources
            .Where(s => s.IsColor && s.IsVideo && !string.IsNullOrWhiteSpace(s.DeviceId))
            .Select(s => s.DeviceId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Steuerzeichen und Leerraum-Folgen entfernen; leer → „Kamera“.</summary>
    public static string CleanName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return FallbackName;
        var sb = new StringBuilder(raw.Length);
        bool space = false;
        foreach (char ch in raw)
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                space = sb.Length > 0;
                continue;
            }
            if (space) sb.Append(' ');
            space = false;
            sb.Append(ch);
        }
        return sb.Length == 0 ? FallbackName : sb.ToString();
    }

    /// <summary>
    /// Bereinigte, eindeutige Namen in gleicher Reihenfolge. Mehrfach vorkommende Namen (Groß/Klein egal) werden
    /// durchnummeriert: „USB Camera (1)“, „USB Camera (2)“ – ohne mit einem vorhandenen Namen zusammenzufallen.
    /// </summary>
    public static IReadOnlyList<string> MakeUnique(IReadOnlyList<string?> rawNames)
    {
        var cleaned = rawNames.Select(CleanName).ToList();
        var counts = cleaned.GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(cleaned.Where(n => counts[n] == 1), StringComparer.OrdinalIgnoreCase);
        var next = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(cleaned.Count);
        foreach (var name in cleaned)
        {
            if (counts[name] == 1)
            {
                result.Add(name);
                continue;
            }
            int n = next.GetValueOrDefault(name);
            string unique;
            do unique = $"{name} ({++n})";
            while (!taken.Add(unique));
            next[name] = n;
            result.Add(unique);
        }
        return result;
    }
}
