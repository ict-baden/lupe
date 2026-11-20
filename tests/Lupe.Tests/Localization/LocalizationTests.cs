// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Lupe.Core.Resources;
using Lupe.Core.Localization;

namespace Lupe.Tests.Localization;

/// <summary>Sprachdateien und Sprachwahl.</summary>
public sealed partial class LocalizationTests
{
    // Die Sprachdateien liegen als Kopie neben der Test-DLL (Lupe.Tests.csproj): In der CI schreibt
    // ContinuousIntegrationBuild die Quellpfade auf /_/ um, ein Weg über [CallerFilePath] fände sie dort nicht.
    private static string ResourceDir() => Path.Combine(AppContext.BaseDirectory, "Resources");

    private static Dictionary<string, string> Load(string file) =>
        XDocument.Load(Path.Combine(ResourceDir(), file)).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    private static SortedSet<int> Placeholders(string text) =>
        new(Placeholder().Matches(text.Replace("{{", "").Replace("}}", "")).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));

    [Fact]
    public void Maschinell_uebersetzte_Sprachen_sind_in_der_Auswahl_gekennzeichnet()
    {
        Assert.Equal("Deutsch", UiLanguage.DisplayName("de"));
        Assert.Equal("English", UiLanguage.DisplayName("en"));
        Assert.Equal("Español (maschinell übersetzt)", UiLanguage.DisplayName("es"));
        Assert.All(UiLanguage.MachineTranslated, code => Assert.Contains(code, UiLanguage.Supported));
    }

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
    [InlineData("", "fr-FR", "fr")]
    [InlineData("", "fr-CA", "fr")]
    [InlineData("", "es-MX", "es")]
    [InlineData("", "zh-CN", "zh-Hans")]
    [InlineData("", "it-IT", "en")]
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
            Assert.Equal("Langue du système", Strings.Language_System);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            Assert.Equal("系统语言", Strings.Language_System);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("it");
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
        Assert.Equal("Español", UiLanguage.NativeName("es"));
        Assert.Equal("Français", UiLanguage.NativeName("fr"));
        Assert.Contains("中文", UiLanguage.NativeName("zh-Hans")); // je nach ICU „简体中文“ oder „中文（简体）“
    }

    [Fact]
    public void Alle_fuenf_Sprachen()
    {
        Assert.Equal(["en", "de", "es", "fr", "zh-Hans"], UiLanguage.Supported);
        foreach (var code in UiLanguage.Supported.Where(c => c != UiLanguage.Fallback))
            Assert.True(File.Exists(Path.Combine(ResourceDir(), $"Strings.{code}.resx")), $"Strings.{code}.resx fehlt");
    }

    [Fact]
    public void Satellitenassemblys_fuer_jede_Sprache()
    {
        // SatelliteResourceLanguages (Directory.Build.props) muss jede Sprache enthalten, sonst fehlt sie im Paket
        foreach (var code in UiLanguage.Supported.Where(c => c != UiLanguage.Fallback))
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, code, "Lupe.Core.resources.dll")), $"{code}/Lupe.Core.resources.dll fehlt");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("zh-Hans")]
    public void Angezeigte_Tastenkuerzel_lassen_sich_in_jeder_Sprache_wieder_einlesen(string language)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            foreach (var hotkey in new[]
            {
                new Lupe.Core.Model.Hotkey(Ctrl: true, Alt: true, Shift: true, Win: true, Key: 0x51), // Q
                new Lupe.Core.Model.Hotkey(Ctrl: true, Alt: false, Shift: true, Win: false, Key: 0x22), // Bild ab / Page Down
                new Lupe.Core.Model.Hotkey(Ctrl: false, Alt: true, Shift: false, Win: true, Key: 0x2C), // Druck
            })
            {
                var text = Lupe.Core.Input.HotkeyText.Format(hotkey, mac: false);
                Assert.True(Lupe.Core.Input.HotkeyText.TryParse(text, out var parsed), $"{language}: „{text}“ nicht lesbar");
                Assert.Equal(hotkey, parsed);
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
