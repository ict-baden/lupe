using Lupe.Core.Model;

namespace Lupe.Core.Settings;

/// <summary>Einstellungen als JSON unter %LocalAppData%\Lupe\einstellungen.json.</summary>
public static class SettingsStore
{
    public static string DefaultPath => throw new NotImplementedException();
    /// <summary>Fehlt die Datei oder ist sie kaputt: Standardwerte (eine kaputte Datei wird nicht überschrieben, bis gespeichert wird).</summary>
    public static LupeSettings Load(string? path = null) => throw new NotImplementedException();
    public static void Save(LupeSettings settings, string? path = null) => throw new NotImplementedException();
}

/// <summary>Link-Regeln anwenden – sicher: nur http(s), Regex mit Zeitlimit, ungültige Regeln werden übersprungen.</summary>
public static class LinkRules
{
    /// <summary>Der zu öffnende Link für <paramref name="text"/>, null wenn keine Regel passt oder das Ergebnis kein sicherer Link ist.</summary>
    public static Uri? Match(IEnumerable<LinkRule> rules, string text) => throw new NotImplementedException();

    /// <summary>Prüft eine Regel für die Einstellungen; Fehlermeldung auf Deutsch oder null.</summary>
    public static string? Validate(LinkRule rule) => throw new NotImplementedException();
}
