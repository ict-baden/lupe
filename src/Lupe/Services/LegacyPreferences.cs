using System.Text.Json;
using Lupe.Core.Model;

namespace Lupe.Services;

/// <summary>
/// Umzug aus %LocalAppData%\Lupe\oberflaeche.json: Frühere Fassungen hielten „Im Infobereich weiterlaufen“ und den
/// Merker für den Infobereich-Hinweis in dieser eigenen Datei. Beides steht jetzt in <see cref="LupeSettings"/>.
/// </summary>
internal static class LegacyPreferences
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "Lupe", "oberflaeche.json");

    private sealed record OldFile(bool? KeepRunningInTray, bool? TrayHintShown);

    /// <summary>Gibt es die alte Datei (noch)?</summary>
    public static bool Exists(string? path = null)
    {
        try { return File.Exists(path ?? DefaultPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    /// <summary>
    /// Übernimmt die Werte der alten Datei in <paramref name="settings"/> – nur, wo die Einstellungen noch auf dem Standard
    /// stehen (ausgeschaltetes Weiterlaufen bleibt aus, ein schon gezeigter Hinweis bleibt gezeigt). Fehlt die Datei oder
    /// ist sie unlesbar, bleibt alles, wie es ist.
    /// </summary>
    public static LupeSettings Migrate(LupeSettings settings, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        OldFile? old;
        try
        {
            var file = path ?? DefaultPath;
            if (!File.Exists(file)) return settings;
            old = JsonSerializer.Deserialize<OldFile>(File.ReadAllText(file), Json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            return settings;
        }
        if (old is null) return settings;
        return settings with
        {
            KeepRunningInTray = settings.KeepRunningInTray && (old.KeepRunningInTray ?? true),
            TrayHintShown = settings.TrayHintShown || (old.TrayHintShown ?? false),
        };
    }

    /// <summary>Löscht die alte Datei, nachdem ihre Werte in den Einstellungen gespeichert sind. false, wenn das nicht ging.</summary>
    public static bool Delete(string? path = null)
    {
        try
        {
            File.Delete(path ?? DefaultPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
