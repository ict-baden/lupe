// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Globalization;
using Lupe.Core.Model;
using Lupe.Core.Settings;

namespace Lupe.Tests.Settings;

/// <summary>Link-Regeln: Treffer, Präfix-Varianten, Angriffe, Zeitlimit, Prüfung für die Einstellungen.</summary>
public sealed class LinkRulesTests
{
    private static readonly LinkRule[] Defaults = [.. new LupeSettings().LinkRules];

    private static string? Link(string text, params LinkRule[] rules) => LinkRules.Match(rules, text)?.AbsoluteUri;

    // ---------- Treffer mit den Standardregeln ----------

    [Theory]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("https://example.com/pfad?x=1&y=2#teil", "https://example.com/pfad?x=1&y=2#teil")]
    [InlineData("  https://example.com/a  \r\n", "https://example.com/a")]
    [InlineData("https://ict-baden.de:8443/x", "https://ict-baden.de:8443/x")]
    public void Standardregel_oeffnet_https_Links(string text, string expected)
    {
        Assert.Equal(expected, Link(text, Defaults));
    }

    [Theory]
    [InlineData("http://example.com")]      // Standardregel verlangt https
    [InlineData("4006381333931")]
    [InlineData("Hallo Welt")]
    [InlineData("https://example.com/mit leerzeichen")] // \S+ passt nicht
    [InlineData("")]
    [InlineData("   ")]
    public void Standardregel_passt_nicht(string text)
    {
        Assert.Null(Link(text, Defaults));
    }

    [Fact]
    public void Rueckgabe_ist_ein_absoluter_Uri()
    {
        var uri = LinkRules.Match(Defaults, "https://example.com/x");

        Assert.NotNull(uri);
        Assert.True(uri.IsAbsoluteUri);
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("example.com", uri.Host);
    }

    // ---------- Präfix ----------

    [Fact]
    public void Praefix_mit_Host_maskiert_den_Code_als_Daten()
    {
        var rule = new LinkRule(".+", "https://example.com/suche?q=");

        Assert.Equal("https://example.com/suche?q=4006381333931", Link("4006381333931", rule));
        Assert.Equal("https://example.com/suche?q=A%26B%201%2F2%23x%3Fy%3Dz", Link("A&B 1/2#x?y=z", rule));
        Assert.Equal("https://example.com/suche?q=Gr%C3%BC%C3%9Fe", Link("Grüße", rule));
        Assert.Equal("https://example.com/suche?q=a-b_c.d~e", Link("a-b_c.d~e", rule));
    }

    [Fact]
    public void Praefix_als_Pfad()
    {
        var rule = new LinkRule(@"^\d+$", "https://example.com/artikel/");

        Assert.Equal("https://example.com/artikel/12345", Link("12345", rule));
        Assert.Equal("https://example.com/artikel/12345", Link(" 12345\n", rule)); // getrimmt, bevor gesucht wird
    }

    [Fact]
    public void Code_kann_Host_und_Pfad_des_Praefixes_nicht_veraendern()
    {
        var rule = new LinkRule(".+", "https://shop.example/artikel/");

        foreach (var attack in new[] { "../../admin", "@evil.example/", "//evil.example/x", "%2e%2e/admin", "x?admin=1#frag", "\\\\evil.example\\x" })
        {
            var uri = LinkRules.Match([rule], attack);
            Assert.NotNull(uri);
            Assert.Equal("shop.example", uri.Host);
            Assert.StartsWith("https://shop.example/artikel/", uri.AbsoluteUri);
            Assert.Empty(uri.Query);
            Assert.Empty(uri.Fragment);
        }
    }

    [Fact]
    public void Praefix_ohne_Abschluss_des_Hosts_wird_nicht_angewendet()
    {
        // Sonst würde aus „https://shop.example“ + „x.evil.example“ ein fremder Host
        var rule = new LinkRule(".+", "https://shop.example");

        Assert.Null(Link("x.evil.example", rule));
        Assert.Null(Link("123", rule));
        Assert.NotNull(LinkRules.Validate(rule));
    }

    [Fact]
    public void Praefix_nur_Schema_nimmt_den_Code_als_Rest_der_Adresse()
    {
        var rule = new LinkRule(@"^www\.", "https://");

        Assert.Equal("https://www.example.com/a/b?c=d", Link("www.example.com/a/b?c=d", rule));
        Assert.Equal("http://www.example.com/", Link("www.example.com", rule with { Prefix = "http://" }));
        Assert.Equal("https://www.example.com/", Link("www.example.com", rule with { Prefix = "HTTPS://" }));
    }

    [Fact]
    public void Praefix_nur_Schema_lehnt_Anmeldedaten_und_Muell_ab()
    {
        var rule = new LinkRule(".+", "https://");

        Assert.Null(Link("bank.example@evil.example", rule));
        Assert.Null(Link("user:pass@evil.example/", rule));
        Assert.Null(Link("exa mple.com", rule));
        Assert.Null(Link("evil.example\\x", rule));
        Assert.Null(Link("evil.example/\r\nx", rule));
    }

    [Fact]
    public void Leerzeichen_am_Praefix_werden_getrimmt()
    {
        Assert.Equal("https://example.com/?q=1", Link("1", new LinkRule(".+", "  https://example.com/?q=  ")));
    }

    [Fact]
    public void Code_selbst_wird_mit_Praefix_ein_harmloser_Parameter()
    {
        var uri = LinkRules.Match([new LinkRule(".+", "https://example.com/?q=")], "javascript:alert(1)");

        Assert.NotNull(uri);
        Assert.Equal("example.com", uri.Host);
        Assert.Equal("https://example.com/?q=javascript%3Aalert%281%29", uri.AbsoluteUri);
    }

    [Fact]
    public void Einzelnes_Surrogat_im_Code_bricht_nichts()
    {
        var uri = LinkRules.Match([new LinkRule(".+", "https://example.com/?q=")], "a\uD800b");

        Assert.NotNull(uri);
        Assert.Equal("example.com", uri.Host);
    }

    // ---------- Angriffe: nur absolute http(s)-Links ----------

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(document.cookie)")]
    [InlineData("  javascript:alert(1)  ")]
    [InlineData("file:///C:/")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("file://server/freigabe/x.exe")]
    [InlineData(@"\\server\x")]
    [InlineData(@"\\server\freigabe\setup.exe")]
    [InlineData(@"C:\Windows\System32\calc.exe")]
    [InlineData("ms-settings:privacy-webcam")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    [InlineData("search-ms:query=x")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("mailto:a@example.com")]
    [InlineData("ftp://example.com/x")]
    [InlineData("calc.exe")]
    [InlineData("https:example.com")]
    [InlineData("https:/example.com")]
    [InlineData("https://")]
    [InlineData("https:///pfad")]
    [InlineData(@"https:\\example.com\x")]
    [InlineData(@"https://example.com\@evil.example")]
    [InlineData("https://bank.example@evil.example/")]
    [InlineData("https://user:passwort@example.com/")]
    [InlineData("https://@example.com/")]
    [InlineData("https://example.com:99999/")]
    [InlineData("https://exa mple.com/")]
    [InlineData("https://example.com/\r\nSet-Cookie: x")]
    [InlineData("https://example.com/a\u0000b")]
    [InlineData("https://example.com/a\u0085b")] // NEL mitten drin (am Rand wäre es Leerraum und würde getrimmt)
    [InlineData("https://example.com/a\u001Bb")]
    [InlineData("//example.com/x")]
    [InlineData("/relativ/pfad")]
    public void Unsichere_oder_ungueltige_Links_werden_nie_geliefert(string text)
    {
        var everything = new LinkRule(".*");

        Assert.Null(Link(text, everything));
    }

    [Theory]
    [InlineData("https://good.example\uFF0Fevil.example/")] // Vollbreiten-Schrägstrich ／ im Host
    [InlineData("https://good.example\uFE6Bevil.example/")] // kleines ﹫ im Host
    [InlineData("https://evil.example\uFF03.good.example/")] // Vollbreiten-Raute ＃ im Host
    [InlineData("https://evil.example\uFF1F.good.example/")] // Vollbreiten-Fragezeichen ？ im Host
    public void Nach_IDN_Regeln_verbotene_Zeichen_im_Host_ergeben_null_statt_Ausnahme(string text)
    {
        // Mit der Standardregel passt so ein Code (\S+); .NET wirft beim Umrechnen des Hosts eine UriFormatException
        Assert.Null(LinkRules.Match(Defaults, text));
        Assert.Null(LinkRules.Match([new LinkRule(".*")], text));
        Assert.Null(LinkRules.Match([new LinkRule(".*", "https://")], text["https://".Length..]));
        Assert.Null(LinkRules.Match([new LinkRule(".*", "http://")], text["https://".Length..]));
        // Danach geht es mit der nächsten Regel weiter
        Assert.StartsWith("https://example.com/?q=https%3A%2F%2F", Link(text, new LinkRule(".*"), new LinkRule(".*", "https://example.com/?q=")));
    }

    [Theory]
    [InlineData("https://good.example\uFF0Fx/")]
    [InlineData("https://a\uFE6Bb/?q=")]
    public void Praefix_mit_verbotenem_Zeichen_im_Host_meldet_sich_statt_zu_werfen(string prefix)
    {
        var rule = new LinkRule(".*", prefix);

        Assert.Equal("Das Präfix ist kein gültiger Link-Anfang.", LinkRules.Validate(rule));
        Assert.Null(LinkRules.Match([rule], "123"));
    }

    [Theory]
    [InlineData("javascript:")]
    [InlineData("file:///C:/")]
    [InlineData(@"\\server\")]
    [InlineData("ms-settings:")]
    [InlineData("data:text/html,")]
    [InlineData("ftp://example.com/")]
    [InlineData("https://user:pw@example.com/")]
    [InlineData("https://example.com\\")]
    public void Unsicheres_Praefix_ergibt_keinen_Link(string prefix)
    {
        var rule = new LinkRule(".*", prefix);

        Assert.Null(Link("alert(1)", rule));
        Assert.Null(Link("https://example.com/", rule));
        Assert.NotNull(LinkRules.Validate(rule));
    }

    [Theory]
    [InlineData("http://192.168.1.10/status")]
    [InlineData("http://localhost:8080/")]
    [InlineData("https://[2001:db8::1]/")]
    [InlineData("https://bücher.example/")]
    [InlineData("HTTPS://EXAMPLE.COM/Gross")]
    public void Ungewoehnliche_aber_gueltige_http_Links_gehen(string text)
    {
        var uri = LinkRules.Match([new LinkRule(".*")], text);

        Assert.NotNull(uri);
        Assert.True(uri.Scheme is "http" or "https");
    }

    // ---------- Reihenfolge, aktiv, ungültige Regeln ----------

    [Fact]
    public void Erste_passende_Regel_gewinnt()
    {
        LinkRule[] rules =
        [
            new(@"^\d{13}$", "https://ean.example/"),
            new(@"^\d+$", "https://zahl.example/"),
        ];

        Assert.Equal("https://ean.example/4006381333931", Link("4006381333931", rules));
        Assert.Equal("https://zahl.example/42", Link("42", rules));
    }

    [Fact]
    public void Passt_eine_Regel_ohne_sicheren_Link_geht_es_mit_der_naechsten_weiter()
    {
        LinkRule[] rules =
        [
            new(".*"),                                 // Code selbst ist kein Link
            new(@"^\d+$", "https://example.com/?nr="),
        ];

        Assert.Equal("https://example.com/?nr=123", Link("123", rules));
    }

    [Fact]
    public void Inaktive_Regeln_werden_uebersprungen()
    {
        LinkRule[] rules =
        [
            new(@"^\d+$", "https://aus.example/", Enabled: false),
            new(@"^\d+$", "https://an.example/"),
        ];

        Assert.Equal("https://an.example/7", Link("7", rules));
        Assert.Null(Link("7", rules[0]));
    }

    [Fact]
    public void Ungueltige_Regeln_werden_uebersprungen()
    {
        LinkRule[] rules =
        [
            new("(kaputt"),
            new(""),
            null!,
            new(null!),
            new(".+", "javascript:"),
            new(@"^\d+$", "https://ok.example/"),
        ];

        Assert.Equal("https://ok.example/5", Link("5", rules));
    }

    [Fact]
    public void Ohne_Regeln_oder_ohne_Text_kein_Link()
    {
        Assert.Null(LinkRules.Match([], "https://example.com/"));
        Assert.Null(LinkRules.Match(null!, "https://example.com/"));
        Assert.Null(LinkRules.Match(Defaults, null!));
    }

    [Fact]
    public void Regeln_werden_nur_einmal_aufgezaehlt_und_auch_als_Liste_aus_den_Einstellungen_angenommen()
    {
        var calls = 0;
        IEnumerable<LinkRule> Lazy()
        {
            calls++;
            yield return new LinkRule(@"^\d+$", "https://example.com/");
        }

        Assert.Equal("https://example.com/1", LinkRules.Match(Lazy(), "1")?.AbsoluteUri);
        Assert.Equal(1, calls);
        Assert.NotNull(LinkRules.Match(new LupeSettings().LinkRules, "https://example.com/"));
    }

    [Fact]
    public void Muster_sucht_irgendwo_im_Code_Verankerung_mit_Zirkumflex()
    {
        Assert.NotNull(Link("Artikel 123", new LinkRule(@"\d+", "https://example.com/?q=")));
        Assert.Null(Link("Artikel 123", new LinkRule(@"^\d+$", "https://example.com/?q=")));
    }

    // ---------- Zeitlimit und Kultur ----------

    [Theory]
    [InlineData(@"^(a+)+$")]
    [InlineData(@"^(a|aa)+$")]
    [InlineData(@"^(\w+\s?)*$")]
    [InlineData(@"(a*)*b")]
    public void Katastrophales_Backtracking_wird_durch_das_Zeitlimit_abgebrochen(string pattern)
    {
        var text = new string('a', 64) + "!";
        LinkRule[] rules =
        [
            new(pattern, "https://langsam.example/"),
            new("!$", "https://danach.example/?q="),
        ];

        var watch = Stopwatch.StartNew();
        var link = Link(text, rules);
        watch.Stop();

        // Ohne Zeitlimit liefe das praktisch ewig (2^64 Schritte); mit Limit ~100 ms, dann die nächste Regel
        Assert.StartsWith("https://danach.example/?q=", link);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"dauerte {watch.ElapsedMilliseconds} ms");
        Assert.True(watch.Elapsed >= LinkRules.MatchTimeout * 0.5, "Das Zeitlimit hat gar nicht gegriffen – Muster zu harmlos?");
    }

    [Fact]
    public void Zeitlimit_ist_100_ms()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(100), LinkRules.MatchTimeout);
    }

    [Fact]
    public void Gross_Kleinschreibung_unabhaengig_von_der_Kultur()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // Im Türkischen ist das kleine „I“ ein „ı“ – ohne CultureInvariant passte (?i)ITEM nicht auf „item“
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var rule = new LinkRule(@"(?i)^https://example\.com/ITEM$");

            Assert.Equal("https://example.com/item", Link("https://example.com/item", rule));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ---------- Validate ----------

    [Theory]
    [InlineData(@"^https://\S+$", "")]
    [InlineData(@"^\d{13}$", "https://example.com/artikel?ean=")]
    [InlineData(".*", "https://")]
    [InlineData(".*", "http://")]
    [InlineData(".*", "  https://example.com/  ")]
    [InlineData(".*", "https://example.com/#")]
    [InlineData(".*", "http://192.168.0.1:8080/")]
    [InlineData("(?<ean>\\d+)", "https://example.com/?x=")]
    public void Gueltige_Regeln_ergeben_keine_Meldung(string pattern, string prefix)
    {
        Assert.Null(LinkRules.Validate(new LinkRule(pattern, prefix)));
        Assert.Null(LinkRules.Validate(new LinkRule(pattern, prefix, Enabled: false)));
    }

    [Theory]
    [InlineData("", "Muster")]
    [InlineData("   ", "Muster")]
    [InlineData("(", "schließende Klammer")]
    [InlineData("a)", "zu viele schließende Klammern")]
    [InlineData("[abc", "eckige Klammer")]
    [InlineData("[z-a]", "verkehrt herum")]
    [InlineData("*a", "ohne etwas davor")]
    [InlineData("a{3,1}", "verkehrt herum")]
    [InlineData(@"abc\", "am Ende")]
    [InlineData(@"\q", "Escape")]
    [InlineData(@"\p{Gibtsnicht}", "Unicode")]
    [InlineData("(?<n", "Gruppe")]
    [InlineData(@"\k<fehlt>", "Gruppe")]
    public void Ungueltiges_Muster_ergibt_deutsche_Meldung(string pattern, string expected)
    {
        var message = LinkRules.Validate(new LinkRule(pattern));

        Assert.NotNull(message);
        Assert.Contains(expected, message);
    }

    [Fact]
    public void Meldung_nennt_die_Fehlerstelle()
    {
        Assert.Equal(
            "Das Muster ist kein gültiger regulärer Ausdruck: eckige Klammer „[“ nicht geschlossen (bei Zeichen 4).",
            LinkRules.Validate(new LinkRule("[abc")));
    }

    [Theory]
    [InlineData("ftp://example.com/", "http:// bzw. https://")]
    [InlineData("javascript:", "http:// bzw. https://")]
    [InlineData("file:///C:/", "http:// bzw. https://")]
    [InlineData(@"\\server\x", "http:// bzw. https://")]
    [InlineData("www.example.com/", "http:// bzw. https://")]
    [InlineData("https://example.com", "fehlt ein „/“")]
    [InlineData("https://example.com:8080", "fehlt ein „/“")]
    [InlineData("https://user:pw@example.com/", "Benutzernamen")]
    [InlineData("https://bank.example@evil.example/", "Benutzernamen")]
    [InlineData("https://example.com\\x/", "Backslash")]
    [InlineData("https://example.com/\nx", "Steuerzeichen")]
    [InlineData("https://exa mple.com/", "kein gültiger Link-Anfang")]
    [InlineData("https:///x", "kein gültiger Link-Anfang")]
    [InlineData("https://example.com:99999/", "kein gültiger Link-Anfang")]
    public void Ungueltiges_Praefix_ergibt_deutsche_Meldung(string prefix, string expected)
    {
        var message = LinkRules.Validate(new LinkRule(".*", prefix));

        Assert.NotNull(message);
        Assert.Contains(expected, message);
    }

    [Fact]
    public void Validate_mit_null_ist_ein_Programmierfehler()
    {
        Assert.Throws<ArgumentNullException>(() => LinkRules.Validate(null!));
    }

    [Fact]
    public void Was_Validate_akzeptiert_kann_Match_auch_anwenden()
    {
        // Jede gültige Regel mit Präfix liefert für einen einfachen Code einen Link mit dem Host des Präfixes
        foreach (var prefix in new[] { "https://example.com/", "https://example.com/?q=", "http://example.com:81/a#", "https://example.com/x/y?z=1&w=" })
        {
            var rule = new LinkRule(".+", prefix);
            Assert.Null(LinkRules.Validate(rule));
            var uri = LinkRules.Match([rule], "Code 123");
            Assert.NotNull(uri);
            Assert.Equal("example.com", uri.Host);
        }
    }
}
