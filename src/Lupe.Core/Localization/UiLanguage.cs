// SPDX-License-Identifier: MIT
using System.Globalization;

namespace Lupe.Core.Localization;

/// <summary>
/// Sprache der Oberfläche (Richtlinie 13): folgt dem Betriebssystem, lässt sich in den Einstellungen festlegen
/// (<see cref="Model.LupeSettings.Language"/>, auch per verteilter einstellungen.json). Fehlt ein Text, zeigt .NET den
/// englischen aus Strings.resx.
/// </summary>
public static class UiLanguage
{
    /// <summary>Umgebungsvariable, die Einstellung und Systemsprache übersteuert (Skripte, Tests): "en" oder "de".</summary>
    public const string EnvironmentVariable = "LUPE_LANGUAGE";

    /// <summary>Rückfallsprache, steht in Strings.resx.</summary>
    public const string Fallback = "en";

    /// <summary>Sprachen mit eigener Sprachdatei, in der Reihenfolge der Auswahlliste.</summary>
    public static IReadOnlyList<string> Supported { get; } = ["en", "de"];

    /// <summary>Sprache des Systems beim Start, bevor Lupe sie überschreibt.</summary>
    public static CultureInfo System { get; } = CultureInfo.CurrentUICulture;

    /// <summary>Wert für die Einstellung: "" (Systemsprache) oder ein Code aus <see cref="Supported"/>.</summary>
    public static string Normalize(string? setting)
    {
        var code = setting?.Trim() ?? "";
        return Supported.FirstOrDefault(s => s.Equals(code, StringComparison.OrdinalIgnoreCase)) ?? "";
    }

    /// <summary>Die Sprache, die gilt: die eingestellte, sonst die des Systems, wenn es sie gibt, sonst Englisch.</summary>
    public static string Resolve(string? setting, CultureInfo system)
    {
        var chosen = Normalize(setting);
        if (chosen.Length > 0) return chosen;
        for (var c = system; !c.Equals(CultureInfo.InvariantCulture); c = c.Parent)
        {
            var match = Supported.FirstOrDefault(s => s.Equals(c.Name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return Fallback;
    }

    /// <summary>
    /// Stellt die Sprache für alle Threads ein; danach erzeugte Texte kommen in dieser Sprache. Vorrang hat
    /// <see cref="EnvironmentVariable"/>, dann die Einstellung, dann das System.
    /// </summary>
    public static CultureInfo Apply(string? setting)
    {
        var forced = Normalize(Environment.GetEnvironmentVariable(EnvironmentVariable));
        var culture = CultureInfo.GetCultureInfo(Resolve(forced.Length > 0 ? forced : setting, System));
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        return culture;
    }

    /// <summary>Name einer Sprache in ihr selbst („Deutsch“, „English“), für die Auswahlliste.</summary>
    public static string NativeName(string code)
    {
        var name = CultureInfo.GetCultureInfo(code).NativeName;
        return name.Length == 0 ? code : char.ToUpper(name[0], CultureInfo.GetCultureInfo(code)) + name[1..];
    }
}
