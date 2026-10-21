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
    /// <summary>Umgebungsvariable, die Einstellung und Systemsprache übersteuert (Skripte, Tests): ein Code aus <see cref="Supported"/>.</summary>
    public const string EnvironmentVariable = "LUPE_LANGUAGE";

    /// <summary>Rückfallsprache, steht in Strings.resx.</summary>
    public const string Fallback = "en";

    /// <summary>
    /// Sprachen mit eigener Sprachdatei, in der Reihenfolge der Auswahlliste (Richtlinie 13). es, fr und zh-Hans sind
    /// maschinell vorübersetzt und noch nicht gegengelesen (Kopf der jeweiligen Strings.*.resx).
    /// </summary>
    public static IReadOnlyList<string> Supported { get; } = ["en", "de", "es", "fr", "zh-Hans"];

    /// <summary>Maschinell übersetzt und noch nicht von Muttersprachlern gegengelesen – so in der Auswahlliste gekennzeichnet.</summary>
    public static IReadOnlyList<string> MachineTranslated { get; } = ["es", "fr", "zh-Hans"];

    /// <summary>Eintrag der Auswahlliste: Name der Sprache in ihr selbst, bei <see cref="MachineTranslated"/> mit Vermerk.</summary>
    public static string DisplayName(string code) =>
        MachineTranslated.Contains(code, StringComparer.OrdinalIgnoreCase)
            ? string.Format(CultureInfo.CurrentCulture, Resources.Strings.Language_MachineTranslated, NativeName(code))
            : NativeName(code);

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
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(Resolve(forced.Length > 0 ? forced : setting, System));
        }
        catch (CultureNotFoundException)
        {
            // Ohne Kulturdaten (DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1) gibt es keine Kultur „en“ – dann die neutrale
            // Kultur, deren Texte aus Strings.resx (Englisch) kommen, statt beim Start abzustürzen
            culture = CultureInfo.InvariantCulture;
        }
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        return culture;
    }

    /// <summary>
    /// Die Kulturen aller Sprachen aus <see cref="Supported"/>, zum Nachschlagen von Texten in jeder Sprache. Ohne
    /// Kulturdaten (DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1) gibt es nur die neutrale Kultur mit den englischen Texten.
    /// </summary>
    public static IReadOnlyList<CultureInfo> SupportedCultures => _supportedCultures.Value;

    private static readonly Lazy<IReadOnlyList<CultureInfo>> _supportedCultures = new(() =>
    {
        var cultures = new List<CultureInfo>();
        foreach (var code in Supported)
        {
            try { cultures.Add(CultureInfo.GetCultureInfo(code)); }
            catch (CultureNotFoundException) { } // Invariant-Modus: diese Sprache gibt es dann nicht
        }
        if (cultures.Count == 0) cultures.Add(CultureInfo.InvariantCulture);
        return cultures;
    });

    /// <summary>Name einer Sprache in ihr selbst („Deutsch“, „English“), für die Auswahlliste.</summary>
    public static string NativeName(string code)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            var name = culture.NativeName;
            return name.Length == 0 ? code : char.ToUpper(name[0], culture) + name[1..];
        }
        catch (CultureNotFoundException)
        {
            return code; // ohne Kulturdaten (Invariant-Modus)
        }
    }
}
