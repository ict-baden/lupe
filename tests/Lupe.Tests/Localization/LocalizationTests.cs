// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Lupe.Core.Resources;
using Lupe.Core.Localization;

namespace Lupe.Tests.Localization;

/// <summary>Sprachdateien und Sprachwahl (Richtlinie 13).</summary>
public sealed partial class LocalizationTests
{
    private static string ResourceDir([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "src", "Lupe.Core", "Resources"));

    private static Dictionary<string, string> Load(string file) =>
        XDocument.Load(Path.Combine(ResourceDir(), file)).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    private static SortedSet<int> Placeholders(string text) =>
        new(Placeholder().Matches(text.Replace("{{", "").Replace("}}", "")).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));

    [Fact]
    public void Jede_Sprache_hat_dieselben_Schluessel_wie_Englisch()
    {
        var en = Load("Strings.resx");
        Assert.NotEmpty(en);
        foreach (var code in UiLanguage.Supported.Where(c => c != UiLanguage.Fallback))
        {
            var other = Load($"Strings.{code}.resx");
            Assert.Equal(en.Keys.Order(StringComparer.Ordinal), other.Keys.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void Platzhalter_stimmen_in_allen_Sprachen_ueberein()
    {
        var en = Load("Strings.resx");
        foreach (var code in UiLanguage.Supported.Where(c => c != UiLanguage.Fallback))
        {
            foreach (var (key, text) in Load($"Strings.{code}.resx"))
            {
                if (!en.TryGetValue(key, out var english)) continue;
                Assert.True(Placeholders(english).SetEquals(Placeholders(text)), $"{code}: {key} – „{english}“ vs. „{text}“");
            }
        }
    }

    [Fact]
    public void Keine_leeren_Texte()
    {
        foreach (var file in UiLanguage.Supported.Select(c => c == UiLanguage.Fallback ? "Strings.resx" : $"Strings.{c}.resx"))
            foreach (var (key, text) in Load(file))
                Assert.False(string.IsNullOrWhiteSpace(text), $"{file}: {key} ist leer");
    }

    [Theory]
    [InlineData("", "de-DE", "de")]
    [InlineData("", "de-CH", "de")]
    [InlineData("", "en-US", "en")]
    [InlineData("", "fr-FR", "en")]
    [InlineData("", "", "en")]
    [InlineData("en", "de-DE", "en")]
    [InlineData(" DE ", "en-US", "de")]
    [InlineData("xx", "de-AT", "de")]
    public void Sprache_folgt_Einstellung_sonst_System_sonst_Englisch(string setting, string system, string expected) =>
        Assert.Equal(expected, UiLanguage.Resolve(setting, CultureInfo.GetCultureInfo(system)));

    [Fact]
    public void Einstellung_wird_bereinigt()
    {
        Assert.Equal("", UiLanguage.Normalize(null));
        Assert.Equal("", UiLanguage.Normalize("klingonisch"));
        Assert.Equal("de", UiLanguage.Normalize("DE"));
    }

    [Fact]
    public void Texte_kommen_in_der_eingestellten_Sprache()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            Assert.Equal("System language", Strings.Language_System);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            Assert.Equal("Systemsprache", Strings.Language_System);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr");
            Assert.Equal("System language", Strings.Language_System); // fehlende Sprache → Englisch
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Sprachnamen_in_der_eigenen_Sprache()
    {
        Assert.Equal("Deutsch", UiLanguage.NativeName("de"));
        Assert.Equal("English", UiLanguage.NativeName("en"));
    }
}
