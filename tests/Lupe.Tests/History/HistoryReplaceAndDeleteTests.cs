using Lupe.Tests.TestSupport;
using System.Diagnostics;
using Lupe.Core.History;
using Xunit.Abstractions;
using Lupe.Core.Model;
using Lupe.Tests.Decoding;
using static Lupe.Tests.History.TestEntries;

namespace Lupe.Tests.History;

/// <summary>
/// Nachtragen von Aktionen (<see cref="HistoryStore.Replace"/>), Löschen der Datei beim Ausschalten des Verlaufs und die
/// Obergrenze von 10 000 Einträgen.
/// </summary>
public sealed class HistoryReplaceAndDeleteTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();
    private string FilePath => _temp.File("verlauf.json");

    public void Dispose() => _temp.Dispose();

    // ================= Replace =================

    [Fact]
    public void Replace_ersetzt_an_Ort_und_Stelle_und_speichert()
    {
        var store = new HistoryStore(FilePath);
        // Drei Codes aus einem Bild: gleiche Zeit, Reihenfolge wie gefunden
        var a = Make("A", actions: ResultActions.Keine);
        var b = Make("B", actions: ResultActions.Keine);
        var c = Make("C", actions: ResultActions.Keine);
        store.Add(c);
        store.Add(b);
        store.Add(a);
        int changed = 0;
        store.Changed += (_, _) => changed++;

        var copied = b with { ActionsDone = ResultActions.Kopieren };
        Assert.True(store.Replace(b, copied));

        Assert.Equal(["A", "B", "C"], store.Entries.Select(e => e.Text));
        Assert.Same(copied, store.Entries[1]);
        Assert.Equal(1, changed);
        var reloaded = new HistoryStore(FilePath).Entries;
        Assert.Equal(ResultActions.Kopieren, reloaded[1].ActionsDone);
        Assert.Equal(ResultActions.Keine, reloaded[0].ActionsDone);
    }

    [Fact]
    public void Replace_findet_wertgleichen_Eintrag_nach_dem_Neuladen()
    {
        var first = new HistoryStore(FilePath);
        var entry = Make("X", actions: ResultActions.Kopieren);
        first.Add(entry);
        var second = new HistoryStore(FilePath);

        Assert.True(second.Replace(entry, entry with { ActionsDone = ResultActions.Kopieren | ResultActions.LinkOeffnen }));

        Assert.Equal(ResultActions.Kopieren | ResultActions.LinkOeffnen, Assert.Single(second.Entries).ActionsDone);
    }

    [Fact]
    public void Replace_eines_nicht_mehr_vorhandenen_Eintrags_aendert_nichts()
    {
        var store = new HistoryStore(FilePath, limit: 1);
        var old = Make("alt", minutes: 0);
        store.Add(old);
        store.Add(Make("neu", minutes: 1)); // „alt“ fällt über das Limit
        var before = File.ReadAllBytes(FilePath);
        int changed = 0;
        store.Changed += (_, _) => changed++;

        Assert.False(store.Replace(old, old with { ActionsDone = ResultActions.LinkOeffnen }));

        Assert.Equal(["neu"], store.Entries.Select(e => e.Text));
        Assert.Equal(0, changed);
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void Replace_mit_anderer_Zeit_sortiert_neu_ein()
    {
        var store = new HistoryStore(FilePath);
        var a = Make("A", minutes: 0);
        store.Add(a);
        store.Add(Make("B", minutes: 5));

        Assert.True(store.Replace(a, a with { Time = T0.AddMinutes(10) }));

        Assert.Equal(["A", "B"], store.Entries.Select(e => e.Text));
        AssertNewestFirst(store.Entries);
    }

    [Fact]
    public void Replace_ohne_Persist_schreibt_nichts()
    {
        var store = new HistoryStore(FilePath, persist: false);
        var entry = Make("nur im Speicher");
        store.Add(entry);

        Assert.True(store.Replace(entry, entry with { ActionsDone = ResultActions.LinkOeffnen }));

        Assert.False(File.Exists(FilePath));
        Assert.Empty(_temp.FileNames());
    }

    [Fact]
    public void Replace_mit_null_ist_ein_Programmierfehler()
    {
        var store = new HistoryStore(FilePath, persist: false);
        Assert.Throws<ArgumentNullException>(() => store.Replace(null!, Make("x")));
        Assert.Throws<ArgumentNullException>(() => store.Replace(Make("x"), null!));
    }

    // ================= Datei löschen =================

    [Fact]
    public void TryDeleteFile_loescht_die_Datei_und_Temp_Reste()
    {
        var store = new HistoryStore(FilePath);
        store.Add(Make("vertraulich"));
        // Rest eines abgebrochenen Speicherns (wie AtomicFile ihn benennt) und eine fremde Datei
        File.WriteAllText(_temp.File($"verlauf.json.{Guid.NewGuid():N}.tmp"), "vertraulich");
        File.WriteAllText(_temp.File("einstellungen.json"), "{}");

        Assert.True(HistoryStore.TryDeleteFile(FilePath, out bool deleted, out var error));

        Assert.True(deleted);
        Assert.Null(error);
        Assert.Equal(["einstellungen.json"], _temp.FileNames());
    }

    [Fact]
    public void TryDeleteFile_ohne_Datei_ist_kein_Fehler()
    {
        Assert.True(HistoryStore.TryDeleteFile(_temp.File("gibt-es-nicht", "verlauf.json"), out bool deleted, out var error));

        Assert.False(deleted);
        Assert.Null(error);
    }

    [WindowsFact]
    public void TryDeleteFile_meldet_gesperrte_Datei_auf_Deutsch()
    {
        new HistoryStore(FilePath).Add(Make("x"));

        bool ok;
        string? error;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ok = HistoryStore.TryDeleteFile(FilePath, out _, out error);
        }

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.StartsWith($"Die Verlaufsdatei „{FilePath}“ ließ sich nicht löschen", error);
        Assert.True(File.Exists(FilePath));
    }

    // ================= Obergrenze =================

    [Theory]
    [InlineData(10_000, 10_000)]
    [InlineData(10_001, 10_000)]
    [InlineData(100_000, 10_000)]
    [InlineData(int.MaxValue, 10_000)]
    public void Limit_hoechstens_zehntausend(int limit, int expected)
    {
        Assert.Equal(expected, new HistoryStore(FilePath, limit: limit, persist: false).Limit);
        Assert.Equal(HistoryStore.MaxLimit, Core.Settings.SettingsStore.MaxHistoryLimit);
    }

    // Feste Zeitgrenze – nur auf Entwicklungsrechnern (auf GitHub-Runnern dauerte das Speichern bis zu 2,5 s)
    [LeistungFact]
    public void Voller_Verlauf_mit_zehntausend_Eintraegen_speichert_zuegig()
    {
        // Je Treffer wird die ganze Datei neu geschrieben – bei der Obergrenze muss das auf dem Oberflächen-Thread
        // unbemerkt bleiben (gemessen: rund 2 MB, rund 40 ms)
        var entries = Enumerable.Range(0, HistoryStore.MaxLimit)
            .Select(i => Make($"https://example.org/lieferung/{i:D6}?kunde=4711&lager=Baden", minutes: i, format: "QR_CODE"))
            .ToList();
        var store = new HistoryStore(FilePath, limit: HistoryStore.MaxLimit, persist: false);
        foreach (var e in entries) store.Add(e);
        Core.Settings.AtomicFile.Write(FilePath, HistoryFile.Serialize(store.Entries));
        var full = new HistoryStore(FilePath, limit: HistoryStore.MaxLimit);
        Assert.Equal(HistoryStore.MaxLimit, full.Entries.Count);

        var times = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            var watch = Stopwatch.StartNew();
            full.Add(Make($"neu {i}", minutes: 100_000 + i));
            times.Add(watch.Elapsed.TotalMilliseconds);
        }

        output.WriteLine($"Speichern mit {HistoryStore.MaxLimit} Einträgen ({new FileInfo(FilePath).Length / 1024} KB): "
            + string.Join(", ", times.Select(t => $"{t:0} ms")));
        Assert.Equal(HistoryStore.MaxLimit, full.Entries.Count);
        Assert.Null(full.Problem);
        // Großzügig wegen paralleler Testläufe; der Median ist auf diesem Rechner weit darunter
        Assert.True(times.Order().ElementAt(2) < 400, $"Speichern dauerte {string.Join(", ", times.Select(t => $"{t:0} ms"))}");
        Assert.True(new FileInfo(FilePath).Length < 4 * 1024 * 1024);
    }
}
