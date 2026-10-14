using Lupe.Core.History;
using Lupe.Core.Model;
using static Lupe.Tests.History.TestEntries;

namespace Lupe.Tests.History;

/// <summary>Grundverhalten: Reihenfolge, Limit, Rundlauf über die Datei, Entfernen, Löschen, Ereignis.</summary>
public sealed class HistoryStoreTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private string FilePath => _temp.File("verlauf.json");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Standardpfad_liegt_pro_Benutzer_unter_LocalAppData()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(Path.Combine(local, "Lupe", "verlauf.json"), HistoryStore.DefaultPath, ignoreCase: true);
        // Gescannte Inhalte können vertraulich sein: nicht unter ProgramData (für alle lesbar) und nicht im Roaming-Profil
        Assert.False(HistoryStore.DefaultPath.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), StringComparison.OrdinalIgnoreCase));
        Assert.False(HistoryStore.DefaultPath.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Ohne_Pfad_wird_der_Standardpfad_benutzt()
    {
        // persist=false: die echte Datei des Benutzers wird weder gelesen noch geschrieben
        var store = new HistoryStore(persist: false);

        Assert.Equal(HistoryStore.DefaultPath, store.FilePath, ignoreCase: true);
        Assert.False(store.Persist);
        Assert.Equal(1000, store.Limit);
    }

    [Fact]
    public void Neuer_Verlauf_ist_leer_ohne_Hinweis()
    {
        var store = new HistoryStore(FilePath);

        Assert.Empty(store.Entries);
        Assert.Null(store.Problem);
        Assert.False(File.Exists(FilePath)); // erst beim ersten Eintrag
    }

    [Fact]
    public void Neuester_Eintrag_steht_vorne()
    {
        var store = new HistoryStore(FilePath);

        store.Add(Make("eins", 0));
        store.Add(Make("zwei", 1));
        store.Add(Make("drei", 2));

        Assert.Equal(["drei", "zwei", "eins"], store.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Eintraege_werden_nach_Zeit_einsortiert()
    {
        var store = new HistoryStore(FilePath);

        store.Add(Make("mitte", 5));
        store.Add(Make("alt", 1));
        store.Add(Make("neu", 9));
        store.Add(Make("fast neu", 7));

        Assert.Equal(["neu", "fast neu", "mitte", "alt"], store.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Bei_gleicher_Zeit_steht_der_zuletzt_hinzugefuegte_vorne()
    {
        var store = new HistoryStore(FilePath);

        store.Add(Make("erster", 3));
        store.Add(Make("zweiter", 3));

        Assert.Equal(["zweiter", "erster"], store.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Zeitzonen_werden_als_Zeitpunkt_verglichen()
    {
        var store = new HistoryStore(FilePath);
        // 08:00 UTC ist später als 09:30 +02:00 (= 07:30 UTC)
        var utc = new HistoryEntry { Time = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero), Text = "utc", Format = "QR_CODE", Source = ScanSource.Datei };

        store.Add(Make("plus zwei", 0));
        store.Add(utc);

        Assert.Equal(["utc", "plus zwei"], store.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Limit_verwirft_die_aeltesten()
    {
        var store = new HistoryStore(FilePath, limit: 3);

        for (var i = 0; i < 6; i++) store.Add(Make($"code {i}", i));

        Assert.Equal(["code 5", "code 4", "code 3"], store.Entries.Select(e => e.Text));
        Assert.Equal(3, TestEntries.ReadFile(FilePath).GetProperty("Entries").GetArrayLength());
    }

    [Fact]
    public void Aelterer_Eintrag_als_alle_faellt_bei_vollem_Limit_sofort_weg()
    {
        var store = new HistoryStore(FilePath, limit: 2);
        store.Add(Make("neu", 10));
        store.Add(Make("neuer", 11));

        store.Add(Make("uralt", -100));

        Assert.Equal(["neuer", "neu"], store.Entries.Select(e => e.Text));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MinValue)]
    public void Limit_unter_eins_wird_eins(int limit)
    {
        var store = new HistoryStore(FilePath, limit: limit);

        store.Add(Make("a", 0));
        store.Add(Make("b", 1));

        Assert.Equal(1, store.Limit);
        Assert.Equal(["b"], store.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Limit_greift_auch_beim_Laden()
    {
        var full = new HistoryStore(FilePath, limit: 10);
        for (var i = 0; i < 10; i++) full.Add(Make($"code {i}", i));

        var small = new HistoryStore(FilePath, limit: 4);

        Assert.Equal(["code 9", "code 8", "code 7", "code 6"], small.Entries.Select(e => e.Text));
        // Beim Laden wird nichts geschrieben – erst die nächste Änderung kürzt auch die Datei
        Assert.Equal(10, TestEntries.ReadFile(FilePath).GetProperty("Entries").GetArrayLength());
        small.Add(Make("neu", 20));
        Assert.Equal(4, TestEntries.ReadFile(FilePath).GetProperty("Entries").GetArrayLength());
    }

    [Fact]
    public void Rundlauf_ueber_die_Datei_erhaelt_alle_Felder()
    {
        string[] texts =
        [
            "https://example.com/?a=1&b=2",
            "Grüße aus Baden – „Anführung“ ß ÄÖÜ",
            "Emoji 😀 und 𝄞 (außerhalb der BMP)",
            "Zeile 1\r\nZeile 2\nZeile 3\tTab",
            "Steuerzeichen \u0001\u001F und \"Anführungszeichen\" \\ Backslash",
            "<script>alert(1)</script> & '",
            "",
            new string('x', 7000),
            $"{(char)0x2028}{(char)0x2029} Zeilentrenner",
        ];
        var store = new HistoryStore(FilePath);
        var expected = new List<HistoryEntry>();
        var offsets = new[] { TimeSpan.Zero, TimeSpan.FromHours(2), TimeSpan.FromHours(-5), TimeSpan.FromMinutes(330) };
        for (var i = 0; i < texts.Length; i++)
        {
            var entry = new HistoryEntry
            {
                // Ticks genau und verschiedene Versätze – beides muss erhalten bleiben
                Time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, offsets[i % offsets.Length]).AddDays(i).AddTicks(1234567 + i),
                Text = texts[i],
                Format = i % 2 == 0 ? "QR_CODE" : "CODE_128",
                Source = (ScanSource)(i % 5),
                ActionsDone = (ResultActions)(i % 8),
            };
            store.Add(entry);
            expected.Insert(0, entry);
        }

        var reloaded = new HistoryStore(FilePath);

        Assert.Null(reloaded.Problem);
        AssertSame(expected, reloaded.Entries);
        AssertSame(store.Entries, reloaded.Entries);
    }

    [Fact]
    public void Datei_ist_versioniertes_lesbares_JSON()
    {
        var store = new HistoryStore(FilePath);
        store.Add(Make("Grüße", 0, ScanSource.Bildschirm, "DATA_MATRIX", ResultActions.Kopieren | ResultActions.LinkOeffnen));

        var root = TestEntries.ReadFile(FilePath);
        var text = File.ReadAllText(FilePath);

        Assert.Equal(1, root.GetProperty("Version").GetInt32());
        var entry = root.GetProperty("Entries")[0];
        Assert.Equal("Grüße", entry.GetProperty("Text").GetString());
        Assert.Equal("Bildschirm", entry.GetProperty("Source").GetString());
        Assert.Equal("Kopieren, LinkOeffnen", entry.GetProperty("ActionsDone").GetString());
        Assert.Equal("2026-10-14T09:30:00+02:00", entry.GetProperty("Time").GetString());
        Assert.Contains("Grüße", text); // Umlaute nicht als ü
        Assert.NotEqual(0xEF, File.ReadAllBytes(FilePath)[0]); // UTF-8 ohne BOM
    }

    [Fact]
    public void Remove_entfernt_dieselbe_Instanz_und_speichert()
    {
        var store = new HistoryStore(FilePath);
        var a = Make("a", 0);
        var b = Make("b", 1);
        store.Add(a);
        store.Add(b);

        store.Remove(a);

        Assert.Equal(["b"], store.Entries.Select(e => e.Text));
        Assert.Equal(["b"], new HistoryStore(FilePath).Entries.Select(e => e.Text));
    }

    [Fact]
    public void Remove_findet_wertgleichen_Eintrag_nach_dem_Neuladen()
    {
        var first = new HistoryStore(FilePath);
        var entry = Make("weg damit", 0, ScanSource.Zwischenablage, "EAN_13", ResultActions.Eintippen);
        first.Add(entry);
        first.Add(Make("bleibt", 1));

        var second = new HistoryStore(FilePath);
        Assert.NotSame(entry, second.Entries[1]);
        second.Remove(entry);

        Assert.Equal(["bleibt"], second.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Remove_entfernt_bei_Doppelten_nur_einen()
    {
        var store = new HistoryStore(FilePath);
        var twin = Make("doppelt", 0);
        store.Add(twin);
        store.Add(twin with { });

        store.Remove(twin);

        Assert.Single(store.Entries);
    }

    [Fact]
    public void Remove_eines_unbekannten_Eintrags_aendert_nichts()
    {
        var store = new HistoryStore(FilePath);
        store.Add(Make("a", 0));
        File.Delete(FilePath);
        var changed = 0;
        store.Changed += (_, _) => changed++;

        store.Remove(Make("gibt es nicht", 0));
        store.Remove(Make("a", 1)); // anderer Zeitpunkt = anderer Eintrag

        Assert.Single(store.Entries);
        Assert.Equal(0, changed);
        Assert.False(File.Exists(FilePath)); // nicht neu geschrieben
    }

    [Fact]
    public void Clear_leert_und_speichert_einen_leeren_Verlauf()
    {
        var store = new HistoryStore(FilePath);
        store.Add(Make("a", 0));
        store.Add(Make("b", 1));

        store.Clear();

        Assert.Empty(store.Entries);
        Assert.Equal(0, TestEntries.ReadFile(FilePath).GetProperty("Entries").GetArrayLength());
        Assert.Empty(new HistoryStore(FilePath).Entries);
    }

    [Fact]
    public void Changed_kommt_nach_jeder_Aenderung_mit_dem_neuen_Stand()
    {
        var store = new HistoryStore(FilePath);
        var seen = new List<int>();
        store.Changed += (sender, _) =>
        {
            Assert.Same(store, sender);
            seen.Add(store.Entries.Count);
        };

        var a = Make("a", 0);
        store.Add(a);
        store.Add(Make("b", 1));
        store.Remove(a);
        store.Clear();

        Assert.Equal([1, 2, 1, 0], seen);
    }

    [Fact]
    public void Entries_ist_eine_unveraenderliche_Momentaufnahme()
    {
        var store = new HistoryStore(FilePath);
        store.Add(Make("a", 0));

        var snapshot = store.Entries;
        store.Add(Make("b", 1));
        store.Clear();

        Assert.Equal(["a"], snapshot.Select(e => e.Text));
        Assert.Throws<NotSupportedException>(() => ((IList<HistoryEntry>)snapshot).Add(Make("c", 2)));
        Assert.Throws<NotSupportedException>(() => ((IList<HistoryEntry>)snapshot)[0] = Make("c", 2));
    }

    [Fact]
    public void Add_mit_null_ist_ein_Programmierfehler()
    {
        var store = new HistoryStore(FilePath);

        Assert.Throws<ArgumentNullException>(() => store.Add(null!));
        Assert.Throws<ArgumentNullException>(() => store.Remove(null!));
    }

    [Fact]
    public void Eintrag_mit_null_Text_wird_als_leerer_Text_gespeichert()
    {
        var store = new HistoryStore(FilePath);
        store.Add(new HistoryEntry { Time = T0, Text = null!, Format = null!, Source = ScanSource.Datei });

        var reloaded = new HistoryStore(FilePath);

        Assert.Null(reloaded.Problem);
        var entry = Assert.Single(reloaded.Entries);
        Assert.Equal("", entry.Text);
        Assert.Equal("", entry.Format);
    }

    [Fact]
    public void Ohne_Persist_wird_nichts_geschrieben()
    {
        var dir = _temp.File("unterordner");
        var path = Path.Combine(dir, "verlauf.json");
        var store = new HistoryStore(path, persist: false);

        store.Add(Make("geheim", 0));
        store.Add(Make("noch geheimer", 1));
        store.Remove(store.Entries[0]);
        store.Clear();
        store.Add(Make("zuletzt", 2));

        Assert.Equal(["zuletzt"], store.Entries.Select(e => e.Text));
        Assert.False(Directory.Exists(dir)); // nicht einmal der Ordner
        Assert.Null(store.Problem);
    }

    [Fact]
    public void Ohne_Persist_wird_eine_vorhandene_Datei_weder_gelesen_noch_angefasst()
    {
        var saved = new HistoryStore(FilePath);
        saved.Add(Make("von früher", 0));
        var before = File.ReadAllBytes(FilePath);

        var store = new HistoryStore(FilePath, persist: false);
        Assert.Empty(store.Entries);
        store.Add(Make("neu", 1));
        store.Clear();

        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void Relativer_Pfad_wird_absolut()
    {
        var store = new HistoryStore("verlauf-relativ.json", persist: false);

        Assert.True(Path.IsPathFullyQualified(store.FilePath));
        Assert.EndsWith("verlauf-relativ.json", store.FilePath);
    }
}
