using Lupe.Core.Model;
using Lupe.Core.Settings;

namespace Lupe.Tests.Settings;

/// <summary>
/// Die Einstellungsdatei der laufenden App: Eine Datei, die beim Start kaputt, teilweise unbrauchbar oder gesperrt war, wird
/// nie stillschweigend mit Standardwerten überschrieben – sie wird vorher gesichert bzw. erst nach dem Wiederlesen ergänzt.
/// </summary>
public sealed class SettingsFileTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 14, 10, 42, 7);
    private const string BackupName = "einstellungen.json.defekt-20261014-104207";

    private readonly TempFolder _temp = new();
    private string FilePath => _temp.File("einstellungen.json");

    public void Dispose() => _temp.Dispose();

    private SettingsFile Open() => new(FilePath, () => Now);

    /// <summary>Hält die Datei offen, ohne andere lesen oder schreiben zu lassen (wie ein Sicherungsprogramm).</summary>
    private FileStream Lock() => new(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

    // ================= ohne Probleme =================

    [Fact]
    public void Fehlende_Datei_Standardwerte_und_erstes_Speichern_legt_sie_an()
    {
        var file = Open();

        Assert.Equal(SettingsFileState.Missing, file.State);
        Assert.Null(file.LoadProblem);
        SettingsAssert.Equal(new LupeSettings(), file.Current);

        var result = file.Save(file.Current with { Sound = false });

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Null(result.Message);
        Assert.False(SettingsStore.Load(FilePath).Sound);
        Assert.Equal(["einstellungen.json"], _temp.FileNames());
    }

    [Fact]
    public void Gleiche_Werte_werden_nicht_geschrieben()
    {
        SettingsStore.Save(new LupeSettings { HistoryLimit = 77 }, FilePath);
        var written = File.GetLastWriteTimeUtc(FilePath);
        File.SetLastWriteTimeUtc(FilePath, written.AddHours(-1));
        var file = Open();

        // Eine neue Liste mit gleichem Inhalt ist keine Änderung (die Record-Gleichheit sähe das anders)
        var result = file.Save(file.Current with { LinkRules = [.. file.Current.LinkRules] });

        Assert.Equal(SettingsSaveStatus.Unchanged, result.Status);
        Assert.Equal(written.AddHours(-1), File.GetLastWriteTimeUtc(FilePath));
    }

    [Fact]
    public void Gelesene_Datei_wird_ohne_Sicherung_ueberschrieben()
    {
        SettingsStore.Save(SettingsAssert.AllDifferent(), FilePath);
        var file = Open();

        Assert.Equal(SettingsFileState.Loaded, file.State);
        var result = file.Save(file.Current with { Sound = true });

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Equal(["einstellungen.json"], _temp.FileNames());
        SettingsAssert.Equal(SettingsAssert.AllDifferent() with { Sound = true }, SettingsStore.Load(FilePath));
    }

    // ================= kaputt =================

    [Theory]
    [InlineData("{ \"Sound\": false, \"HistoryLimit\": 42, ")]
    [InlineData("kein JSON")]
    [InlineData("[1, 2]")]
    public void Kaputte_Datei_wird_vor_dem_Speichern_gesichert(string content)
    {
        File.WriteAllText(FilePath, content);
        var original = File.ReadAllBytes(FilePath);
        var file = Open();
        Assert.Equal(SettingsFileState.Broken, file.State);
        Assert.Contains("beschädigt", file.LoadProblem);
        Assert.Contains("als „einstellungen.json.defekt-…“ daneben gesichert", file.LoadProblem);
        Assert.Equal(file.LoadProblem, file.Problem);

        var result = file.Save(file.Current with { AimHelp = false });
        Assert.Null(file.Problem);

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Equal(["einstellungen.json", BackupName], _temp.FileNames());
        Assert.Equal(original, File.ReadAllBytes(_temp.File(BackupName)));
        Assert.Equal(_temp.File(BackupName), file.BackupPath);
        Assert.Contains($"„{BackupName}“ gesichert", result.Message);
        Assert.False(SettingsStore.Load(FilePath, out var problem).AimHelp);
        Assert.Null(problem);
        Assert.Equal(SettingsFileState.Loaded, file.State);
    }

    [Fact]
    public void Nach_der_Sicherung_wird_nicht_noch_einmal_gesichert()
    {
        File.WriteAllText(FilePath, "{ kaputt");
        var file = Open();

        file.Save(file.Current with { Sound = false });
        var second = file.Save(file.Current with { Sound = true });

        Assert.Equal(SettingsSaveStatus.Saved, second.Status);
        Assert.Null(second.Message);
        Assert.Equal(["einstellungen.json", BackupName], _temp.FileNames());
    }

    [Fact]
    public void Vorhandene_Sicherung_wird_nicht_ueberschrieben()
    {
        File.WriteAllText(_temp.File(BackupName), "ältere Sicherung");
        File.WriteAllText(FilePath, "{ kaputt");
        var file = Open();

        file.Save(file.Current with { Sound = false });

        Assert.Equal("ältere Sicherung", File.ReadAllText(_temp.File(BackupName)));
        Assert.Equal("{ kaputt", File.ReadAllText(_temp.File(BackupName + "-2")));
    }

    [Fact]
    public void Teilweise_unbrauchbare_Datei_wird_auch_gesichert()
    {
        // Ein Wert unbrauchbar, eine Link-Regel ohne Muster: beim Laden ersetzt bzw. verworfen – im Original aber noch da
        File.WriteAllText(FilePath, """{ "Sound": "ja", "HistoryLimit": 50, "LinkRules": [ { "Pattern": "^x$" }, 17 ] }""");
        var original = File.ReadAllBytes(FilePath);
        var file = Open();
        Assert.Equal(SettingsFileState.Repaired, file.State);
        Assert.Equal(50, file.Current.HistoryLimit);

        var result = file.Save(file.Current with { AimHelp = false });

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Equal(original, File.ReadAllBytes(_temp.File(BackupName)));
        var saved = SettingsStore.Load(FilePath);
        Assert.Equal(50, saved.HistoryLimit);
        Assert.False(saved.AimHelp);
    }

    [Fact]
    public void Scheitert_die_Sicherung_bleibt_die_kaputte_Datei_unangetastet()
    {
        File.WriteAllText(FilePath, "{ kaputt");
        var file = Open();

        SettingsSaveResult result;
        using (Lock())
        {
            result = file.Save(file.Current with { Sound = false });
        }

        Assert.Equal(SettingsSaveStatus.Failed, result.Status);
        Assert.True(result.IsProblem);
        Assert.Contains("nicht sichern", result.Message);
        Assert.Equal(result.Message, file.Problem);
        Assert.Equal("{ kaputt", File.ReadAllText(FilePath));
        Assert.Equal(["einstellungen.json"], _temp.FileNames());
        // Die Änderung gilt trotzdem (bis zum Beenden) …
        Assert.False(file.Current.Sound);
        // … und beim nächsten Speichern klappt es mit Sicherung
        var retry = file.Save(file.Current with { AimHelp = false });
        Assert.Equal(SettingsSaveStatus.Saved, retry.Status);
        Assert.Equal("{ kaputt", File.ReadAllText(_temp.File(BackupName)));
        var saved = SettingsStore.Load(FilePath);
        Assert.False(saved.Sound);
        Assert.False(saved.AimHelp);
    }

    // ================= gesperrt =================

    [Fact]
    public void Gesperrte_Datei_wird_nicht_ueberschrieben_solange_sie_gesperrt_ist()
    {
        SettingsStore.Save(SettingsAssert.AllDifferent(), FilePath);
        var before = File.ReadAllBytes(FilePath);

        SettingsFile file;
        SettingsSaveResult result;
        using (Lock())
        {
            file = Open();
            Assert.Equal(SettingsFileState.Unreadable, file.State);
            Assert.Contains("nicht gelesen", file.LoadProblem);
            Assert.Contains("nicht mit Standardwerten überschrieben", file.Problem);
            SettingsAssert.Equal(new LupeSettings(), file.Current);

            result = file.Save(file.Current with { Sound = false });
        }

        Assert.Equal(SettingsSaveStatus.Deferred, result.Status);
        Assert.True(result.IsProblem);
        Assert.Contains("nicht lesbar", result.Message);
        Assert.Equal(result.Message, file.Problem);
        Assert.False(file.Current.Sound);
        Assert.Equal(before, File.ReadAllBytes(FilePath));
        Assert.Equal(SettingsFileState.Unreadable, file.State);
    }

    [Fact]
    public void Wieder_lesbar_werden_nur_die_Aenderungen_der_Sitzung_uebernommen()
    {
        var stored = SettingsAssert.AllDifferent(); // u. a. Sound aus, Zielhilfe aus, Limit 250, Strg+Umschalt+F9
        SettingsStore.Save(stored, FilePath);

        SettingsFile file;
        using (Lock())
        {
            file = Open();
            // In der Sitzung (mit Standardwerten) geändert: Zielhilfe aus, Wiederholungssperre 5 s
            Assert.Equal(SettingsSaveStatus.Deferred, file.Save(file.Current with { AimHelp = false }).Status);
            Assert.Equal(SettingsSaveStatus.Deferred, file.Save(file.Current with { RepeatSuppressMs = 5000 }).Status);
        }

        var result = file.Save(file.Current with { TypeSuffix = "Enter" });

        Assert.Equal(SettingsSaveStatus.Merged, result.Status);
        Assert.Contains("zusammengeführt", result.Message);
        Assert.Null(file.Problem);
        var expected = stored with { AimHelp = false, RepeatSuppressMs = 5000, TypeSuffix = "Enter" };
        SettingsAssert.Equal(expected, result.Settings);
        SettingsAssert.Equal(expected, file.Current);
        SettingsAssert.Equal(expected, SettingsStore.Load(FilePath));
        Assert.Equal(SettingsFileState.Loaded, file.State);
        Assert.Equal(["einstellungen.json"], _temp.FileNames());
    }

    [Fact]
    public void Reload_uebernimmt_die_wieder_lesbare_Datei_ohne_zu_schreiben()
    {
        SettingsStore.Save(SettingsAssert.AllDifferent(), FilePath);
        var written = File.GetLastWriteTimeUtc(FilePath).AddHours(-1);
        File.SetLastWriteTimeUtc(FilePath, written);

        SettingsFile file;
        using (Lock())
        {
            file = Open();
            Assert.Null(file.Reload()); // noch gesperrt: nichts Neues
        }

        var result = file.Reload();

        Assert.NotNull(result);
        Assert.Equal(SettingsSaveStatus.Unchanged, result.Status);
        Assert.Contains("wieder lesbar", result.Message);
        Assert.Null(file.Problem);
        SettingsAssert.Equal(SettingsAssert.AllDifferent(), file.Current);
        Assert.Equal(SettingsFileState.Loaded, file.State);
        Assert.Equal(written, File.GetLastWriteTimeUtc(FilePath));
        Assert.Null(file.Reload()); // nichts mehr zu tun
    }

    [Fact]
    public void Reload_mit_Aenderungen_fuehrt_zusammen_und_speichert()
    {
        SettingsStore.Save(SettingsAssert.AllDifferent(), FilePath);
        SettingsFile file;
        using (Lock())
        {
            file = Open();
            file.Save(file.Current with { Camera = "Neue Kamera" });
        }

        var result = file.Reload();

        Assert.NotNull(result);
        Assert.Equal(SettingsSaveStatus.Merged, result.Status);
        SettingsAssert.Equal(SettingsAssert.AllDifferent() with { Camera = "Neue Kamera" }, SettingsStore.Load(FilePath));
    }

    [Fact]
    public void Gesperrt_und_danach_kaputt_wird_gesichert()
    {
        File.WriteAllText(FilePath, "{ kaputt");
        SettingsFile file;
        using (Lock())
        {
            file = Open();
            file.Save(file.Current with { Sound = false });
        }

        var result = file.Save(file.Current with { AimHelp = false });

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Equal("{ kaputt", File.ReadAllText(_temp.File(BackupName)));
        var saved = SettingsStore.Load(FilePath);
        Assert.False(saved.Sound);
        Assert.False(saved.AimHelp);
    }

    [Fact]
    public void Gesperrt_und_danach_geloescht_speichert_einfach()
    {
        SettingsStore.Save(new LupeSettings { HistoryLimit = 77 }, FilePath);
        SettingsFile file;
        using (Lock())
        {
            file = Open();
        }
        File.Delete(FilePath);

        var result = file.Save(file.Current with { Sound = false });

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.False(SettingsStore.Load(FilePath).Sound);
        Assert.Equal(1000, SettingsStore.Load(FilePath).HistoryLimit);
    }

    // ================= Zustand beim Laden =================

    [Theory]
    [InlineData("", SettingsFileState.Missing)]
    [InlineData("{}", SettingsFileState.Loaded)]
    [InlineData("""{ "Sound": false }""", SettingsFileState.Loaded)]
    [InlineData("""{ "Sound": "ja" }""", SettingsFileState.Repaired)]
    [InlineData("""{ "Formats": [ "QR_CODE", 5 ] }""", SettingsFileState.Repaired)]
    [InlineData("{", SettingsFileState.Broken)]
    public void Load_nennt_den_Zustand_der_Datei(string content, SettingsFileState expected)
    {
        File.WriteAllText(FilePath, content);

        SettingsStore.Load(FilePath, out _, out var state);

        Assert.Equal(expected, state);
    }

    [Fact]
    public void SameValues_vergleicht_Listen_nach_Inhalt()
    {
        var a = new LupeSettings { Formats = ["QR_CODE"] };
        var b = new LupeSettings { Formats = ["QR_CODE"] };

        Assert.NotEqual(a, b); // Record-Gleichheit: andere Liste
        Assert.True(SettingsStore.SameValues(a, b));
        Assert.False(SettingsStore.SameValues(a, b with { Formats = ["EAN_13"] }));
        Assert.True(SettingsStore.SameValues(new LupeSettings { HistoryLimit = 50_000 }, new LupeSettings { HistoryLimit = 10_000 })); // bereinigt
    }
}
