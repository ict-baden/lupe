// SPDX-License-Identifier: MIT
using System.Globalization;
using Lupe.Cli;
using Lupe.Core.Resources;

namespace Lupe.Tests.Cli;

/// <summary>Teile der Befehlszeile ohne eigenen Prozess – laufen auf jedem System (die Prozess-Tests in App/ nur unter Windows).</summary>
public sealed class CommandLineLogicTests
{
    [Fact]
    public void Jeder_Code_bleibt_eine_Zeile_auch_mit_C1_und_Zeilentrennern()
    {
        Assert.Equal(@"a\x85b\x9Bc\u2028d\u2029e", CodeLine.Escape("a\u0085b\u009Bc\u2028d\u2029e"));
        Assert.Equal(@"a\r\nb\tc\x1Dd\x7F\x00e\", CodeLine.Escape("a\r\nb\tc\u001Dd\u007F\0e\\"));
        var escaped = CodeLine.Escape("eins\u0085zwei\u2028drei\nvier");
        Assert.Single(escaped.Split(['\n', '\r', '\u0085', '\u2028', '\u2029']));
    }

    [Fact]
    public void Zeichen_zur_Schreibrichtung_werden_sichtbar()
    {
        // U+202E (RIGHT-TO-LEFT OVERRIDE) könnte im Terminal „exe.txt“ als „txt.exe“ erscheinen lassen
        Assert.Equal(@"rechnung\u202Etxt.exe", CodeLine.Escape("rechnung\u202Etxt.exe"));
        Assert.Equal(@"\u200E\u200F\u061C\u2066\u2069", CodeLine.Escape("\u200E\u200F\u061C\u2066\u2069"));
    }

    [Fact]
    public void Gewoehnlicher_Inhalt_auch_mit_Umlauten_und_CJK_bleibt()
    {
        foreach (var text in new[] { "https://example.org/a?b=1", @"WIFI:T:WPA;S:Netz\;1;P:geheim;;", "Größe 42 – €", "条码 Ⓡ ✓" })
            Assert.Same(text, CodeLine.Escape(text));
    }

    [Theory]
    [InlineData("--hintergrund")]
    [InlineData("--background")]
    [InlineData("--BACKGROUND")]
    public void Englische_Schalter_fuer_den_Hintergrund(string arg) =>
        Assert.Equal(new StartOptions(AppMode.Background), StartOptions.Parse([arg]));

    [Theory]
    [InlineData("--bildschirm")]
    [InlineData("--screen")]
    public void Englische_Schalter_fuer_den_Bildschirm_Scan(string arg) =>
        Assert.Equal(new StartOptions(AppMode.Background, ScanOnStart: true), StartOptions.Parse([arg]));

    [Theory]
    [InlineData("light", "hell")]
    [InlineData("dark", "dunkel")]
    [InlineData("both", "beide")]
    [InlineData("hell", "hell")]
    public void Englische_Namen_fuer_das_Farbschema(string given, string expected)
    {
        var options = StartOptions.Parse(["--screenshots", Path.GetTempPath(), "--theme", given]);
        Assert.Equal(AppMode.Screenshots, options.Mode);
        Assert.Equal(expected, options.Theme);
    }

    [Fact]
    public void Version_und_Info_fuer_Fehlerberichte()
    {
        Assert.StartsWith("Lupe ", CommandLine.VersionLine());
        Assert.Contains(AppInfo.Version, CommandLine.VersionLine());
        var info = CommandLine.InfoText();
        Assert.Contains(AppInfo.ImprintUrl, info);
        Assert.Contains(AppInfo.WebsiteUrl, info);
        Assert.Contains(AppInfo.SupportUrl, info);
        Assert.Contains(AppInfo.PrivacyUrl, info);
        Assert.Contains("ICT-Baden", info);
        Assert.Contains("THIRD-PARTY-NOTICES.txt", info);
    }

    [Theory]
    [InlineData("/Applications/Lupe.app/Contents/MacOS/Lupe", "/Applications/Lupe.app/Contents/MacOS/Lupe")]
    [InlineData("/Users/x/Downloads/Lupe.app/Contents/MacOS/Lupe", "/Users/x/Downloads/Lupe.app/Contents/MacOS/Lupe")]
    [InlineData("/private/var/folders/ab/T/AppTranslocation/1234/d/Lupe.app/Contents/MacOS/Lupe", "/Applications/Lupe.app/Contents/MacOS/Lupe")]
    [InlineData("/usr/local/share/dotnet/dotnet", "/Applications/Lupe.app/Contents/MacOS/Lupe")]
    [InlineData(null, "/Applications/Lupe.app/Contents/MacOS/Lupe")]
    public void Befehl_unter_macOS_ist_das_Programm_im_Buendel(string? processPath, string expected) =>
        Assert.Equal(expected, CommandLine.MacCommand(processPath));

    [Fact]
    public void Hilfe_nennt_den_Befehl_dieses_Systems_und_hat_keine_offenen_Platzhalter()
    {
        var help = CommandLine.HelpText("9.9.9", "Win+Umschalt+Q", "QR_CODE, EAN_13");
        Assert.Contains(CommandLine.CommandName + " lesen", help);
        Assert.Contains("--version", help);
        Assert.Contains("--info", help);
        Assert.DoesNotMatch(@"\{\d\}", help);
        Assert.Equal(OperatingSystem.IsWindows(), help.Contains("start /wait", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("en", "read", "--help")]
    [InlineData("de", "lesen", "--hilfe")]
    [InlineData("es", "read", "--help")]
    [InlineData("fr", "read", "--help")]
    [InlineData("zh-Hans", "read", "--help")]
    public void Hilfe_Hinweis_und_Info_Seite_nennen_die_Schluesselwoerter_der_Sprache(string language, string read, string help)
    {
        // Beide Namen gehen in jeder Sprache; gezeigt wird der deutsche nur in der deutschen Oberfläche
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            Assert.Equal(read, Strings.App_Cli_ReadKeyword);
            Assert.Equal(help, Strings.App_Cli_HelpKeyword);
            var text = CommandLine.HelpText("9.9.9", "Win+Shift+Q", "QR_CODE");
            Assert.Contains($"{CommandLine.CommandName} {read} ", text);
            Assert.Contains($"{CommandLine.CommandName} {help}", text);
            Assert.EndsWith($" {help}", string.Format(Strings.App_Cli_HelpHint, CommandLine.CommandName));
            Assert.Contains($" {read} ", string.Format(Strings.App_Cli_NoImage, CommandLine.CommandName, "x.png"));
            var other = read == "read" ? " lesen " : " read ";
            Assert.DoesNotContain($"{CommandLine.CommandName}{other}", text);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
