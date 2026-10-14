using Lupe.Core.History;
using Lupe.Core.Model;
using static Lupe.Tests.History.TestEntries;

namespace Lupe.Tests.History;

/// <summary>Kamera-Thread, Bildschirm-Scan und Oberfläche greifen gleichzeitig zu.</summary>
public sealed class HistoryConcurrencyTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private string FilePath => _temp.File("verlauf.json");

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Paralleles_Hinzufuegen_verliert_nichts(bool persist)
    {
        const int threads = 8, perThread = 40;
        var store = new HistoryStore(FilePath, limit: 1000, persist: persist);
        var changed = 0;
        store.Changed += (_, _) => Interlocked.Increment(ref changed);
        using var start = new Barrier(threads);

        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
        {
            start.SignalAndWait();
            for (var i = 0; i < perThread; i++)
                store.Add(Make($"t{t}-{i}", t * 1000 + i, (ScanSource)(t % 5)));
        });

        Assert.Equal(threads * perThread, store.Entries.Count);
        Assert.Equal(threads * perThread, changed);
        Assert.Equal(threads * perThread, store.Entries.Select(e => e.Text).Distinct().Count());
        AssertNewestFirst(store.Entries);
        Assert.Null(store.Problem);
        if (persist)
        {
            AssertSame(store.Entries, new HistoryStore(FilePath).Entries);
            Assert.Equal(["verlauf.json"], _temp.FileNames());
        }
        else
        {
            Assert.False(File.Exists(FilePath));
        }
    }

    [Fact]
    public void Paralleles_Hinzufuegen_mit_Limit_behaelt_genau_die_neuesten()
    {
        const int threads = 6, perThread = 60, limit = 25;
        var store = new HistoryStore(FilePath, limit: limit);

        Parallel.For(0, threads, t =>
        {
            // Zeiten verschränkt, damit jeder Thread neue und alte Einträge liefert
            for (var i = 0; i < perThread; i++) store.Add(Make($"{i * threads + t}", i * threads + t));
        });

        var expected = Enumerable.Range(0, threads * perThread).Reverse().Take(limit).Select(n => n.ToString());
        Assert.Equal(expected, store.Entries.Select(e => e.Text));
        Assert.Equal(expected, new HistoryStore(FilePath, limit: limit).Entries.Select(e => e.Text));
    }

    [Fact]
    public async Task Lesen_Entfernen_und_Exportieren_waehrend_geschrieben_wird()
    {
        var store = new HistoryStore(FilePath, limit: 200);
        using var stop = new CancellationTokenSource();
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

        void Guard(Action action)
        {
            try
            {
                while (!stop.IsCancellationRequested) action();
            }
            catch (Exception ex)
            {
                errors.Enqueue(ex);
            }
        }

        var readers = new[]
        {
            // Oberfläche: Liste aufzählen, Reihenfolge muss in jeder Momentaufnahme stimmen
            Task.Run(() => Guard(() =>
            {
                var snapshot = store.Entries;
                AssertNewestFirst(snapshot);
                Assert.True(snapshot.Count <= 200);
                foreach (var entry in snapshot) Assert.NotNull(entry.Text);
            })),
            // Benutzer löscht Einträge
            Task.Run(() => Guard(() =>
            {
                var snapshot = store.Entries;
                if (snapshot.Count > 10) store.Remove(snapshot[snapshot.Count / 2]);
                Thread.Sleep(1);
            })),
            // Export läuft nebenher
            Task.Run(() => Guard(() =>
            {
                var csv = _temp.File("export.csv");
                store.ExportCsv(csv);
                Assert.StartsWith("Zeit;Format;Quelle;Inhalt", File.ReadAllText(csv));
                Thread.Sleep(5);
            })),
        };

        Parallel.For(0, 4, t =>
        {
            for (var i = 0; i < 75; i++) store.Add(Make($"t{t}-{i}", t * 100 + i));
        });
        stop.Cancel();
        await Task.WhenAll(readers);

        Assert.Empty(errors);
        Assert.Null(store.Problem);
        AssertNewestFirst(store.Entries);
        AssertSame(store.Entries, new HistoryStore(FilePath, limit: 200).Entries);
    }

    [Fact]
    public void Changed_wird_ausserhalb_der_Sperre_ausgeloest()
    {
        // Ein Handler, der auf einem anderen Thread auf den Verlauf zugreift, darf nicht verklemmen
        var store = new HistoryStore(FilePath);
        var count = -1;
        store.Changed += (_, _) =>
        {
            var other = new Thread(() =>
            {
                store.Remove(Make("gibt es nicht", 0));
                count = store.Entries.Count;
            });
            other.Start();
            Assert.True(other.Join(TimeSpan.FromSeconds(10)), "Handler verklemmt");
        };

        store.Add(Make("a", 0));

        Assert.Equal(1, count);
    }
}
