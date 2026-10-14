using System.Reflection;
using System.Text;
using System.Text.Json;
using Lupe.Core.Model;
using Lupe.Core.Settings;

namespace Lupe.Tests.Settings;

/// <summary>Einstellungsdatei: Rundlauf, tolerantes Lesen, kaputte Dateien, Normalisierung, atomares Speichern.</summary>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private string FilePath => _temp.File("einstellungen.json");

    public void Dispose() => _temp.Dispose();

    private LupeSettings LoadJson(string json, out string? problem)
    {
        File.WriteAllText(FilePath, json);
        return SettingsStore.Load(FilePath, out problem);
    }

    [Fact]
    public void Standardpfad_liegt_pro_Benutzer_unter_LocalAppData()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(Path.Combine(local, "Lupe", "einstellungen.json"), SettingsStore.DefaultPath, ignoreCase: true);
    }

    [Fact]
    public void Fehlende_Datei_ergibt_Standardwerte_ohne_Hinweis()
    {
        var path = _temp.File("gibt-es-nicht", "einstellungen.json");

        var settings = SettingsStore.Load(path, out var problem);

        SettingsAssert.Equal(new LupeSettings(), settings);
        Assert.Null(problem);
        Assert.False(Directory.Exists(Path.GetDirectoryName(path))); // Laden legt nichts an
    }

    [Fact]
    public void Standardwerte_sind_wie_im_Vertrag()
    {
        var settings = SettingsStore.Load(FilePath);

        Assert.Equal(ResultActions.Kopieren, settings.Actions);
        Assert.Equal(ResultActions.Kopieren, settings.ScreenActions);
        Assert.Equal("", settings.TypeSuffix);
        Assert.Equal([new LinkRule(@"^https://\S+$")], settings.LinkRules);
        Assert.Empty(settings.Formats);
        Assert.Equal(Hotkey.Default, settings.ScreenHotkey);
        Assert.True(settings.Sound);
        Assert.True(settings.AimHelp);
        Assert.Equal(3000, settings.RepeatSuppressMs);
        Assert.Equal("", settings.Camera);
        Assert.True(settings.KeepHistory);
        Assert.Equal(1000, settings.HistoryLimit);
    }

    [Fact]
    public void Rundlauf_erhaelt_alle_Felder()
    {
        var original = SettingsAssert.AllDifferent();
        // Wächter: Kommt ein Feld dazu, muss es hier auch abweichend gesetzt werden
        foreach (var property in typeof(LupeSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var a = JsonSerializer.Serialize(property.GetValue(original));
            var b = JsonSerializer.Serialize(property.GetValue(new LupeSettings()));
            Assert.True(a != b, $"{property.Name} weicht im Testwert nicht vom Standard ab");
        }

        SettingsStore.Save(original, FilePath);
        var loaded = SettingsStore.Load(FilePath, out var problem);

        Assert.Null(problem);
        SettingsAssert.Equal(original, loaded);
    }

    [Fact]
    public void Speichern_legt_den_Ordner_an_und_hinterlaesst_keine_Temp_Datei()
    {
        var path = _temp.File("Lupe", "einstellungen.json");

        SettingsStore.Save(new LupeSettings { Sound = false }, path);
        SettingsStore.Save(new LupeSettings { Sound = true }, path);

        Assert.True(SettingsStore.Load(path).Sound);
        Assert.Equal(["einstellungen.json"], Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName));
    }

    [Fact]
    public void Datei_ist_lesbar_eingerueckt_mit_Enum_Namen_und_unmaskierten_Mustern()
    {
        SettingsStore.Save(SettingsAssert.AllDifferent(), FilePath);

        var text = File.ReadAllText(FilePath);

        Assert.Contains("\"Actions\": \"Kopieren, LinkOeffnen\"", text);
        Assert.Contains("\"TypeSuffix\": \"Tab\"", text);
        Assert.Contains(@"""Pattern"": ""^\\d{13}$""", text);
        Assert.Contains("\"Prefix\": \"https://example.com/artikel?ean=\"", text);
        Assert.Contains("Grüße", text);
        Assert.Contains("\n  \"Sound\": false", text.ReplaceLineEndings("\n"));
        Assert.NotEqual(0xEF, File.ReadAllBytes(FilePath)[0]); // ohne BOM
    }

    [Fact]
    public void Fehlende_Felder_bekommen_Standardwerte()
    {
        var settings = LoadJson("""{ "Sound": false, "HistoryLimit": 42 }""", out var problem);

        SettingsAssert.Equal(new LupeSettings { Sound = false, HistoryLimit = 42 }, settings);
        Assert.Null(problem);
    }

    [Fact]
    public void Unbekannte_Felder_werden_ignoriert()
    {
        var settings = LoadJson("""
            {
              "Sound": false,
              "Theme": "dunkel",
              "Zukunft": { "Verschachtelt": [1, 2, { "x": null }] },
              "LinkRules": [ { "Pattern": "^a$", "Prefix": "", "Enabled": true, "Kommentar": "neu" } ]
            }
            """, out var problem);

        Assert.False(settings.Sound);
        Assert.Equal([new LinkRule("^a$")], settings.LinkRules);
        Assert.Null(problem);
    }

    [Fact]
    public void Gross_und_Kleinschreibung_der_Namen_ist_egal()
    {
        var settings = LoadJson("""{ "sound": false, "HISTORYLIMIT": 50, "screenHotkey": { "ctrl": false, "alt": true, "shift": false, "win": true, "key": 83 } }""", out _);

        Assert.False(settings.Sound);
        Assert.Equal(50, settings.HistoryLimit);
        Assert.Equal(new Hotkey(false, true, false, true, 0x53), settings.ScreenHotkey);
    }

    [Fact]
    public void Kommentare_abschliessende_Kommas_und_BOM_werden_toleriert()
    {
        const string json = """
            // Einstellungen von Hand bearbeitet
            {
              /* Ton aus */ "Sound": false,
              "Formats": [ "QR_CODE", ],
            }
            """;
        File.WriteAllBytes(FilePath, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(json)]);

        var settings = SettingsStore.Load(FilePath, out var problem);

        Assert.False(settings.Sound);
        Assert.Equal(["QR_CODE"], settings.Formats);
        Assert.Null(problem);
    }

    public static TheoryData<string> BrokenContents => new()
    {
        "{",
        "kein JSON",
        "{ \"Sound\": false, ",
        "[1, 2]",
        "null",
        "\"Text\"",
        "42",
        "\0",
    };

    [Theory]
    [MemberData(nameof(BrokenContents))]
    public void Kaputte_Datei_ergibt_Standardwerte_und_bleibt_bis_zum_Speichern_unveraendert(string content)
    {
        File.WriteAllText(FilePath, content);
        var before = File.ReadAllBytes(FilePath);

        var settings = SettingsStore.Load(FilePath, out var problem);
        _ = SettingsStore.Load(FilePath);

        SettingsAssert.Equal(new LupeSettings(), settings);
        Assert.NotNull(problem);
        Assert.Contains("beschädigt", problem);
        Assert.Equal(before, File.ReadAllBytes(FilePath));

        SettingsStore.Save(settings with { Sound = false }, FilePath);
        Assert.False(SettingsStore.Load(FilePath, out var after).Sound);
        Assert.Null(after);
    }

    [Fact]
    public void Ungueltiges_UTF8_gilt_als_kaputt()
    {
        File.WriteAllBytes(FilePath, [0x7B, 0x22, 0x53, 0xFF, 0x22, 0x3A, 0x31, 0x7D]);

        var settings = SettingsStore.Load(FilePath, out var problem);

        SettingsAssert.Equal(new LupeSettings(), settings);
        Assert.NotNull(problem);
    }

    [Fact]
    public void Hinweis_nennt_die_Zeile_des_Fehlers()
    {
        LoadJson("{\n  \"Sound\": false,\n  \"Camera\": kaputt\n}", out var problem);

        Assert.Contains("Zeile 3", problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \r\n ")]
    [InlineData("{}")]
    public void Leere_Datei_ergibt_Standardwerte_ohne_Hinweis(string content)
    {
        var settings = LoadJson(content, out var problem);

        SettingsAssert.Equal(new LupeSettings(), settings);
        Assert.Null(problem);
    }

    [Fact]
    public void Falscher_Typ_in_einem_Feld_betrifft_nur_dieses_Feld()
    {
        var settings = LoadJson("""
            {
              "Sound": "ja",
              "AimHelp": false,
              "HistoryLimit": 50,
              "RepeatSuppressMs": { "ms": 5 },
              "Actions": "Fliegen",
              "ScreenHotkey": "Strg+Alt+Q",
              "Camera": 17,
              "KeepHistory": false
            }
            """, out var problem);

        Assert.True(settings.Sound);              // Standard
        Assert.False(settings.AimHelp);           // übernommen
        Assert.Equal(50, settings.HistoryLimit);  // übernommen
        Assert.Equal(3000, settings.RepeatSuppressMs);
        Assert.Equal(ResultActions.Kopieren, settings.Actions);
        Assert.Equal(Hotkey.Default, settings.ScreenHotkey);
        Assert.Equal("", settings.Camera);
        Assert.False(settings.KeepHistory);
        Assert.NotNull(problem);
        foreach (var name in new[] { "Ton", "Wiederholungssperre", "Aktionen (Kamera/Datei)", "Tastenkürzel", "Kamera" })
            Assert.Contains(name, problem);
        Assert.DoesNotContain("Zielhilfe", problem);
        Assert.DoesNotContain("Aktionen (Bildschirm)", problem);
        Assert.StartsWith($"Einige Einstellungen in „{FilePath}“ waren ungültig. Standardwert verwendet für: ", problem);
    }

    [Fact]
    public void Hinweis_nennt_jedes_Feld_mit_deutschem_Namen()
    {
        // [[]] passt zu keinem Feld: Einzelwerte fallen auf den Standard, in Listen fliegt das Element raus
        var properties = typeof(LupeSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var json = "{" + string.Join(",", properties.Select(p => $"\"{p.Name}\": [[]]")) + "}";

        var settings = LoadJson(json, out var problem);

        SettingsAssert.Equal(new LupeSettings { LinkRules = [], Formats = [] }, settings);
        Assert.NotNull(problem);
        var listed = problem[(problem.IndexOf("ungültig.", StringComparison.Ordinal) + 9)..];
        foreach (var property in properties)
            Assert.DoesNotContain(property.Name, listed);
        Assert.Contains("Standardwert verwendet für: Aktionen (Kamera/Datei), Aktionen (Bildschirm), Taste nach dem Eintippen, ", listed);
        Assert.EndsWith("Unbrauchbare Einträge entfernt aus: Link-Regeln, Formate.", listed);
    }

    [Fact]
    public void Kaputte_Link_Regel_wird_einzeln_verworfen()
    {
        var settings = LoadJson("""
            {
              "LinkRules": [
                { "Pattern": "^eins$", "Prefix": "https://example.com/?q=" },
                { "Pattern": "^zwei$", "Enabled": "vielleicht" },
                42,
                { "Pattern": ["kein", "Text"] },
                { "Pattern": "^drei$", "Enabled": false }
              ]
            }
            """, out var problem);

        Assert.Equal([new LinkRule("^eins$", "https://example.com/?q="), new LinkRule("^drei$", "", false)], settings.LinkRules);
        Assert.EndsWith(" Unbrauchbare Einträge entfernt aus: Link-Regeln.", problem);
        Assert.DoesNotContain("Standardwert", problem);
    }

    [Fact]
    public void Link_Regeln_null_fehlend_und_leer()
    {
        Assert.Equal(new LupeSettings().LinkRules, LoadJson("""{ "LinkRules": null }""", out _).LinkRules);
        Assert.Equal(new LupeSettings().LinkRules, LoadJson("""{ }""", out _).LinkRules);
        // Bewusst alle Regeln gelöscht: bleibt leer
        Assert.Empty(LoadJson("""{ "LinkRules": [] }""", out _).LinkRules);
    }

    [Fact]
    public void Link_Regeln_ohne_Muster_fallen_weg_Praefix_wird_getrimmt()
    {
        var settings = LoadJson("""
            {
              "LinkRules": [
                { "Prefix": "https://example.com/" },
                { "Pattern": null },
                { "Pattern": "" },
                null,
                { "Pattern": "^ok$", "Prefix": null },
                { "Pattern": "^trim$", "Prefix": "  https://example.com/?q=  " },
                { "Pattern": "(ungültig" }
              ]
            }
            """, out _);

        Assert.Equal(
            [new LinkRule("^ok$"), new LinkRule("^trim$", "https://example.com/?q="), new LinkRule("(ungültig")],
            settings.LinkRules);
    }

    [Fact]
    public void Zahlen_als_Text_werden_gelesen()
    {
        var settings = LoadJson("""{ "HistoryLimit": "250", "RepeatSuppressMs": "1200" }""", out var problem);

        Assert.Equal(250, settings.HistoryLimit);
        Assert.Equal(1200, settings.RepeatSuppressMs);
        Assert.Null(problem);
    }

    [Theory]
    [InlineData("\"Kopieren, Eintippen\"", ResultActions.Kopieren | ResultActions.Eintippen)]
    [InlineData("\"kopieren, linkoeffnen\"", ResultActions.Kopieren | ResultActions.LinkOeffnen)]
    [InlineData("\"Keine\"", ResultActions.Keine)]
    [InlineData("6", ResultActions.Eintippen | ResultActions.LinkOeffnen)]
    [InlineData("255", ResultActions.Kopieren | ResultActions.Eintippen | ResultActions.LinkOeffnen)]
    [InlineData("0", ResultActions.Keine)]
    public void Aktionen_als_Namen_oder_Zahl_unbekannte_Bits_fallen_weg(string json, ResultActions expected)
    {
        var settings = LoadJson($$"""{ "Actions": {{json}}, "ScreenActions": {{json}} }""", out _);

        Assert.Equal(expected, settings.Actions);
        Assert.Equal(expected, settings.ScreenActions);
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(5000, 5000)]
    [InlineData(100_000, 100_000)]
    [InlineData(10_000_000, 100_000)]
    [InlineData(int.MinValue, 1)]
    public void Verlaufslimit_wird_begrenzt(int value, int expected)
    {
        Assert.Equal(expected, LoadJson($$"""{ "HistoryLimit": {{value}} }""", out _).HistoryLimit);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(2500, 2500)]
    [InlineData(int.MaxValue, 3_600_000)]
    public void Wiederholungssperre_wird_begrenzt(int value, int expected)
    {
        Assert.Equal(expected, LoadJson($$"""{ "RepeatSuppressMs": {{value}} }""", out _).RepeatSuppressMs);
    }

    [Fact]
    public void Zu_grosse_Zahl_faellt_auf_den_Standard()
    {
        var settings = LoadJson("""{ "HistoryLimit": 99999999999999999999, "Sound": false }""", out var problem);

        Assert.Equal(1000, settings.HistoryLimit);
        Assert.False(settings.Sound);
        Assert.Contains("Größe des Verlaufs", problem);
    }

    [Theory]
    [InlineData("Enter", "Enter")]
    [InlineData("enter", "Enter")]
    [InlineData(" TAB ", "Tab")]
    [InlineData("", "")]
    [InlineData("Leertaste", "")]
    [InlineData("Enter\r\n", "Enter")]
    public void Suffix_ist_nur_leer_Enter_oder_Tab(string value, string expected)
    {
        Assert.Equal(expected, LoadJson($$"""{ "TypeSuffix": {{JsonSerializer.Serialize(value)}} }""", out _).TypeSuffix);
    }

    [Fact]
    public void Suffix_null_wird_leer()
    {
        Assert.Equal("", LoadJson("""{ "TypeSuffix": null, "Camera": null }""", out _).TypeSuffix);
        Assert.Equal("", SettingsStore.Load(FilePath).Camera);
    }

    [Fact]
    public void Formate_werden_in_ZXing_Schreibweise_bereinigt()
    {
        var settings = LoadJson("""
            { "Formats": [ "qr_code", " EAN_13 ", "QR_CODE", "GIBTSNICHT", "All_1D", "2048", null, "", "Data_Matrix", "QR_CODE, AZTEC" ] }
            """, out _);

        Assert.Equal(["QR_CODE", "EAN_13", "DATA_MATRIX"], settings.Formats);
    }

    [Theory]
    [InlineData("""{ "Ctrl": true, "Alt": true, "Shift": false, "Win": false, "Key": 0 }""")]
    [InlineData("""{ "Ctrl": true, "Alt": true, "Shift": false, "Win": false, "Key": 17 }""")]   // Strg allein
    [InlineData("""{ "Ctrl": true, "Alt": true, "Shift": false, "Win": false, "Key": 160 }""")]  // linke Umschalttaste
    [InlineData("""{ "Ctrl": true, "Alt": true, "Shift": false, "Win": false, "Key": 91 }""")]   // Windows-Taste
    [InlineData("""{ "Ctrl": true, "Alt": true, "Shift": false, "Win": false, "Key": 1 }""")]    // linke Maustaste
    [InlineData("""{ "Ctrl": true, "Alt": true, "Shift": false, "Win": false, "Key": 300 }""")]
    [InlineData("""{ "Ctrl": true, "Alt": true, "Shift": false, "Win": false, "Key": -1 }""")]
    [InlineData("null")]
    public void Unbrauchbares_Tastenkuerzel_wird_Standard(string hotkey)
    {
        Assert.Equal(Hotkey.Default, LoadJson($$"""{ "ScreenHotkey": {{hotkey}} }""", out _).ScreenHotkey);
    }

    [Theory]
    [InlineData(0x41, false)]  // A allein
    [InlineData(0x41, true)]   // Umschalt+A (= großes A)
    [InlineData(0x35, false)]  // 5
    [InlineData(0x20, false)]  // Leertaste
    [InlineData(0x0D, false)]  // Enter
    [InlineData(0x09, true)]   // Umschalt+Tab
    [InlineData(0x08, false)]  // Rücktaste
    [InlineData(0x1B, false)]  // Esc
    [InlineData(0x25, false)]  // Pfeil links
    [InlineData(0x2E, false)]  // Entf
    [InlineData(0x60, false)]  // Ziffernblock 0
    [InlineData(0xBE, false)]  // Punkt
    [InlineData(0xE2, true)]   // < > auf deutschen Tastaturen
    public void Schreibtaste_ohne_Strg_Alt_Win_wird_Standard(int key, bool shift)
    {
        // Systemweit angemeldet, würde das Kürzel die Taste in allen Programmen verschlucken
        var hotkey = new Hotkey(Ctrl: false, Alt: false, Shift: shift, Win: false, Key: key);

        Assert.Equal(Hotkey.Default, SettingsStore.Normalize(new LupeSettings { ScreenHotkey = hotkey }).ScreenHotkey);
        Assert.Equal(Hotkey.Default, LoadJson($$"""{ "ScreenHotkey": {{JsonSerializer.Serialize(hotkey)}} }""", out _).ScreenHotkey);
    }

    [Theory]
    [InlineData(0x78, true, false, true, false)]  // Strg+Umschalt+F9
    [InlineData(0x2C, false, false, false, false)] // Druck allein
    [InlineData(0x13, false, false, false, false)] // Pause allein
    [InlineData(0x70, false, false, false, false)] // F1 allein
    [InlineData(0x87, false, false, true, false)]  // Umschalt+F24
    [InlineData(0x53, false, false, true, true)]   // Win+Umschalt+S
    [InlineData(0x41, false, true, false, false)]  // Alt+A
    [InlineData(0x20, true, false, false, false)]  // Strg+Leertaste
    public void Brauchbares_Tastenkuerzel_bleibt(int key, bool ctrl, bool alt, bool shift, bool win)
    {
        var hotkey = new Hotkey(ctrl, alt, shift, win, key);

        SettingsStore.Save(new LupeSettings { ScreenHotkey = hotkey }, FilePath);

        Assert.Equal(hotkey, SettingsStore.Load(FilePath).ScreenHotkey);
    }

    [Fact]
    public void Tastenkuerzel_mit_fehlenden_Feldern_nimmt_false()
    {
        Assert.Equal(new Hotkey(false, false, false, false, 0x78), LoadJson("""{ "ScreenHotkey": { "Key": 120 } }""", out _).ScreenHotkey);
    }

    [Fact]
    public void Normalize_bereinigt_auch_Werte_aus_dem_Code()
    {
        var messy = new LupeSettings
        {
            Actions = (ResultActions)0xFF,
            TypeSuffix = null!,
            LinkRules = [new LinkRule("^a$", null!), null!, new LinkRule(null!)],
            Formats = null!,
            ScreenHotkey = null!,
            RepeatSuppressMs = -10,
            Camera = null!,
            HistoryLimit = 0,
        };

        var clean = SettingsStore.Normalize(messy);

        Assert.Equal(ResultActions.Kopieren | ResultActions.Eintippen | ResultActions.LinkOeffnen, clean.Actions);
        Assert.Equal("", clean.TypeSuffix);
        Assert.Equal([new LinkRule("^a$")], clean.LinkRules);
        Assert.Empty(clean.Formats);
        Assert.Equal(Hotkey.Default, clean.ScreenHotkey);
        Assert.Equal(0, clean.RepeatSuppressMs);
        Assert.Equal("", clean.Camera);
        Assert.Equal(1, clean.HistoryLimit);
    }

    [Fact]
    public void Normalize_laesst_gueltige_Einstellungen_unveraendert()
    {
        var settings = SettingsAssert.AllDifferent();

        SettingsAssert.Equal(settings, SettingsStore.Normalize(settings));
    }

    [Fact]
    public void Speichern_bereinigt_die_Werte()
    {
        SettingsStore.Save(new LupeSettings { HistoryLimit = -3, TypeSuffix = "enter", Formats = ["qr_code", "QR_CODE"] }, FilePath);

        using var doc = JsonDocument.Parse(File.ReadAllBytes(FilePath));
        Assert.Equal(1, doc.RootElement.GetProperty("HistoryLimit").GetInt32());
        Assert.Equal("Enter", doc.RootElement.GetProperty("TypeSuffix").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("Formats").GetArrayLength());
    }

    [Fact]
    public void Save_mit_null_ist_ein_Programmierfehler()
    {
        Assert.Throws<ArgumentNullException>(() => SettingsStore.Save(null!, FilePath));
        Assert.Throws<ArgumentNullException>(() => SettingsStore.TrySave(null!, out _, FilePath));
        Assert.Throws<ArgumentNullException>(() => SettingsStore.Normalize(null!));
    }

    [Fact]
    public void Speichern_in_einen_unmoeglichen_Ordner_meldet_sich_deutsch()
    {
        File.WriteAllText(_temp.File("blockiert"), "");
        var path = _temp.File("blockiert", "einstellungen.json");

        var ex = Assert.Throws<IOException>(() => SettingsStore.Save(new LupeSettings(), path));
        var ok = SettingsStore.TrySave(new LupeSettings(), out var error, path);

        Assert.StartsWith("Die Einstellungen konnten nicht in", ex.Message);
        Assert.NotNull(ex.InnerException);
        Assert.False(ok);
        Assert.Equal(ex.Message, error);
    }

    [Fact]
    public void Gesperrte_Datei_bleibt_beim_gescheiterten_Speichern_heil()
    {
        SettingsStore.Save(new LupeSettings { HistoryLimit = 77 }, FilePath);
        var before = File.ReadAllBytes(FilePath);

        bool ok;
        string? error;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ok = SettingsStore.TrySave(new LupeSettings { HistoryLimit = 88 }, out error, FilePath);
        }

        Assert.False(ok);
        Assert.Contains("gespeichert", error);
        Assert.Equal(before, File.ReadAllBytes(FilePath));
        Assert.Equal(["einstellungen.json"], _temp.FileNames());
        Assert.True(SettingsStore.TrySave(new LupeSettings { HistoryLimit = 88 }, out error, FilePath));
        Assert.Null(error);
        Assert.Equal(88, SettingsStore.Load(FilePath).HistoryLimit);
    }

    [Fact]
    public void Gesperrte_Datei_beim_Laden_ergibt_Standardwerte_mit_Hinweis()
    {
        SettingsStore.Save(new LupeSettings { HistoryLimit = 77 }, FilePath);

        LupeSettings settings;
        string? problem;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            settings = SettingsStore.Load(FilePath, out problem);
        }

        Assert.Equal(1000, settings.HistoryLimit);
        Assert.NotNull(problem);
        Assert.Contains("nicht gelesen", problem);
    }

    [Fact]
    public void Kurze_Sperre_beim_Speichern_wird_abgewartet()
    {
        SettingsStore.Save(new LupeSettings(), FilePath);
        var locked = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        // Wie ein Virenscanner: gibt die Datei nach kurzer Zeit wieder frei
        var release = new Thread(() =>
        {
            Thread.Sleep(40);
            locked.Dispose();
        });
        release.Start();

        SettingsStore.Save(new LupeSettings { HistoryLimit = 5 }, FilePath);
        release.Join();

        Assert.Equal(5, SettingsStore.Load(FilePath).HistoryLimit);
    }

    [Fact]
    public void Gleichzeitiges_Speichern_hinterlaesst_eine_vollstaendige_Fassung()
    {
        var variants = Enumerable.Range(1, 16).Select(i => SettingsAssert.AllDifferent() with { HistoryLimit = i * 10 }).ToArray();

        Parallel.ForEach(variants, new ParallelOptions { MaxDegreeOfParallelism = 8 }, v =>
        {
            for (var round = 0; round < 5; round++) SettingsStore.Save(v, FilePath);
        });

        var loaded = SettingsStore.Load(FilePath, out var problem);
        Assert.Null(problem);
        Assert.Contains(loaded.HistoryLimit, variants.Select(v => v.HistoryLimit));
        SettingsAssert.Equal(variants.Single(v => v.HistoryLimit == loaded.HistoryLimit), loaded);
        Assert.Equal(["einstellungen.json"], _temp.FileNames());
    }

    [Fact]
    public void Datei_aus_aelterer_Fassung_mit_weniger_Feldern_wird_ergaenzt()
    {
        // So könnte eine frühe Fassung ausgesehen haben – das Neuere kommt aus den Standardwerten
        var settings = LoadJson("""
            {
              "Actions": "Kopieren, LinkOeffnen",
              "LinkRules": [ { "Pattern": "^https://\\S+$", "Prefix": "", "Enabled": true } ],
              "Sound": false
            }
            """, out var problem);

        Assert.Null(problem);
        Assert.Equal(ResultActions.Kopieren | ResultActions.LinkOeffnen, settings.Actions);
        Assert.Equal(new LupeSettings().ScreenHotkey, settings.ScreenHotkey);
        Assert.Equal(1000, settings.HistoryLimit);
        Assert.False(settings.Sound);
    }
}
