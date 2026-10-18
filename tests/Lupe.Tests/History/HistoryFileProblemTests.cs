using Lupe.Tests.TestSupport;
using System.Text;
using System.Text.Json;
using Lupe.Core.History;
using Lupe.Core.Model;
using static Lupe.Tests.History.TestEntries;

namespace Lupe.Tests.History;

/// <summary>Echte Dateien: kaputt, teilweise kaputt, gesperrt, atomar geschrieben, gleichzeitig gelesen.</summary>
public sealed class HistoryFileProblemTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private string FilePath => _temp.File("verlauf.json");

    public void Dispose() => _temp.Dispose();

    public static TheoryData<string> BrokenContents => new()
    {
        "{",
        "kein JSON",
        "{\"Entries\": [ {\"Text\": \"a\"",
        "null",
        "42",
        "\"nur ein Text\"",
        "true",
        "{\"Entries\": 5}",
        "{\"Entries\": {\"Text\": \"a\"}}",
        "\0\0\0\0",
    };

    [Theory]
    [MemberData(nameof(BrokenContents))]
    public void Kaputte_Datei_ergibt_leeren_Verlauf_mit_Hinweis_und_bleibt_unveraendert(string content)
    {
        File.WriteAllText(FilePath, content);
        var before = File.ReadAllBytes(FilePath);

        var store = new HistoryStore(FilePath);
        // Weder Lesen noch erfolgloses Entfernen dürfen die kaputte Datei überschreiben
        store.Remove(Make("gibt es nicht", 0));

        Assert.Empty(store.Entries);
        Assert.NotNull(store.Problem);
        Assert.Contains("beschädigt", store.Problem);
        Assert.Contains(FilePath, store.Problem);
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void Ungueltiges_UTF8_gilt_als_kaputt()
    {
        File.WriteAllBytes(FilePath, [0x7B, 0x22, 0xFF, 0xFE, 0x22, 0x3A, 0x31, 0x7D]); // {"��":1}

        var store = new HistoryStore(FilePath);

        Assert.Empty(store.Entries);
        Assert.NotNull(store.Problem);
    }

    [Fact]
    public void Kaputte_Datei_wird_erst_beim_naechsten_Eintrag_ersetzt()
    {
        File.WriteAllText(FilePath, "{ kaputt");
        var store = new HistoryStore(FilePath);
        Assert.NotNull(store.Problem);

        store.Add(Make("neu", 0));

        Assert.Null(store.Problem); // erfolgreich gespeichert – der Hinweis ist erledigt
        var reloaded = new HistoryStore(FilePath);
        Assert.Null(reloaded.Problem);
        Assert.Equal(["neu"], reloaded.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Clear_ersetzt_auch_eine_kaputte_Datei()
    {
        File.WriteAllText(FilePath, "{ kaputt");
        var store = new HistoryStore(FilePath);

        store.Clear();

        Assert.Equal(0, TestEntries.ReadFile(FilePath).GetProperty("Entries").GetArrayLength());
        Assert.Null(store.Problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n\t ")]
    [InlineData("{}")]
    [InlineData("{\"Version\": 1}")]
    [InlineData("{\"Version\": 1, \"Entries\": null}")]
    [InlineData("[]")]
    public void Leere_Datei_ist_ein_leerer_Verlauf_ohne_Hinweis(string content)
    {
        File.WriteAllText(FilePath, content);

        var store = new HistoryStore(FilePath);

        Assert.Empty(store.Entries);
        Assert.Null(store.Problem);
    }

    [Fact]
    public void Einzelne_unbrauchbare_Eintraege_werden_uebersprungen()
    {
        const string json = """
            {
              "Version": 1,
              "Entries": [
                { "Time": "2026-10-14T10:00:00+02:00", "Text": "gut 1", "Format": "QR_CODE", "Source": "Kamera", "ActionsDone": "Kopieren" },
                { "Time": "2026-10-14T09:59:00+02:00", "Format": "QR_CODE", "Source": "Kamera" },
                { "Time": "2026-10-14T09:58:00+02:00", "Text": "falsche Quelle", "Format": "QR_CODE", "Source": "Fax" },
                { "Time": "2026-10-14T09:57:00+02:00", "Text": "Quelle als Zahl außerhalb", "Format": "QR_CODE", "Source": 99 },
                { "Time": "gestern", "Text": "Zeit kaputt", "Format": "QR_CODE", "Source": "Datei" },
                { "Time": "2026-10-14T09:56:00+02:00", "Text": null, "Format": "QR_CODE", "Source": "Datei" },
                { "Time": "2026-10-14T09:55:00+02:00", "Text": 17, "Format": "QR_CODE", "Source": "Datei" },
                42,
                null,
                "Text",
                [],
                { "Time": "2026-10-14T09:50:00+02:00", "Text": "gut 2", "Format": "EAN_13", "Source": 2 }
              ]
            }
            """;
        File.WriteAllText(FilePath, json);

        var store = new HistoryStore(FilePath);

        Assert.Equal(["gut 1", "gut 2"], store.Entries.Select(e => e.Text));
        Assert.Equal(ScanSource.Datei, store.Entries[1].Source);
        Assert.Equal(ResultActions.Keine, store.Entries[1].ActionsDone);
        Assert.Equal("10 Einträge im gespeicherten Verlauf waren unlesbar und wurden übersprungen.", store.Problem);
    }

    [Fact]
    public void Ein_einzelner_unlesbarer_Eintrag_wird_in_der_Einzahl_gemeldet()
    {
        File.WriteAllText(FilePath, """{"Entries": [ 1, {"Time": "2026-10-14T10:00:00Z", "Text": "a", "Format": "QR_CODE", "Source": "Kamera"} ]}""");

        var store = new HistoryStore(FilePath);

        Assert.Single(store.Entries);
        Assert.Equal("Ein Eintrag im gespeicherten Verlauf war unlesbar und wurde übersprungen.", store.Problem);
    }

    [Fact]
    public void Tolerantes_Lesen_BOM_Kommentare_Grossschreibung_unbekannte_Felder()
    {
        const string json = """
            // von Hand bearbeitet
            {
              "version": 7,
              "zukunft": { "irgendwas": [1, 2, 3] },
              "entries": [
                { "time": "2026-10-14T10:00:00+02:00", "TEXT": "a", "format": "QR_CODE", "source": "kamera", "actionsDone": "kopieren, linkoeffnen", "Neu": true, },
              ],
            }
            """;
        File.WriteAllBytes(FilePath, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(json)]);

        var store = new HistoryStore(FilePath);

        Assert.Null(store.Problem);
        var entry = Assert.Single(store.Entries);
        Assert.Equal("a", entry.Text);
        Assert.Equal(ScanSource.Kamera, entry.Source);
        Assert.Equal(ResultActions.Kopieren | ResultActions.LinkOeffnen, entry.ActionsDone);
    }

    [Fact]
    public void Nackte_Liste_ohne_Huelle_wird_gelesen_und_nach_Zeit_sortiert()
    {
        File.WriteAllText(FilePath, """
            [
              { "Time": "2026-10-14T08:00:00Z", "Text": "alt", "Format": "QR_CODE", "Source": "Kamera" },
              { "Time": "2026-10-14T09:00:00Z", "Text": "neu", "Format": "QR_CODE", "Source": "Kamera" }
            ]
            """);

        var store = new HistoryStore(FilePath);

        Assert.Equal(["neu", "alt"], store.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Unbekannte_Aktions_Bits_werden_entfernt()
    {
        File.WriteAllText(FilePath, """{"Entries": [ {"Time": "2026-10-14T10:00:00Z", "Text": "a", "Format": "QR_CODE", "Source": "Kamera", "ActionsDone": 255} ]}""");

        var store = new HistoryStore(FilePath);

        Assert.Equal(ResultActions.Kopieren | ResultActions.Eintippen | ResultActions.LinkOeffnen, Assert.Single(store.Entries).ActionsDone);
    }

    [Fact]
    public void Keine_Temp_Dateien_bleiben_liegen()
    {
        var store = new HistoryStore(FilePath);

        for (var i = 0; i < 25; i++) store.Add(Make($"code {i}", i));
        store.Remove(store.Entries[3]);
        store.Clear();
        store.Add(Make("zuletzt", 99));

        Assert.Equal(["verlauf.json"], _temp.FileNames());
    }

    [Fact]
    public void Ordner_wird_beim_ersten_Eintrag_angelegt()
    {
        var path = _temp.File("neu", "tiefer", "verlauf.json");
        var store = new HistoryStore(path);

        store.Add(Make("a", 0));

        Assert.True(File.Exists(path));
        Assert.Null(store.Problem);
    }

    [WindowsFact]
    public void Scheitert_das_Speichern_bleibt_der_Eintrag_im_Speicher_und_die_alte_Datei_heil()
    {
        var store = new HistoryStore(FilePath);
        store.Add(Make("alt", 0));
        var before = File.ReadAllBytes(FilePath);

        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // Datei exklusiv gesperrt (wie von einem anderen Programm) – Ersetzen geht nicht
            store.Add(Make("neu", 1));

            Assert.Equal(["neu", "alt"], store.Entries.Select(e => e.Text));
            Assert.NotNull(store.Problem);
            Assert.Contains("nicht", store.Problem);
            Assert.Contains("gespeichert", store.Problem);
        }

        Assert.Equal(before, File.ReadAllBytes(FilePath));
        Assert.Equal(["verlauf.json"], _temp.FileNames()); // auch keine Temp-Datei übrig

        // Beim nächsten Mal klappt es – und alles ist drin
        store.Add(Make("danach", 2));
        Assert.Null(store.Problem);
        Assert.Equal(["danach", "neu", "alt"], new HistoryStore(FilePath).Entries.Select(e => e.Text));
    }

    [Fact]
    public void Beim_Start_gesperrte_Datei_wird_nicht_ueberschrieben_sondern_spaeter_zusammengefuehrt()
    {
        var earlier = new HistoryStore(FilePath);
        earlier.Add(Make("von früher 1", 0));
        earlier.Add(Make("von früher 2", 1));
        var before = File.ReadAllBytes(FilePath);

        HistoryStore store;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            store = new HistoryStore(FilePath);
            Assert.Empty(store.Entries);
            Assert.NotNull(store.Problem);
            Assert.Contains("nicht gelesen", store.Problem);

            // Noch gesperrt: kein Überschreiben, Eintrag nur im Speicher
            store.Add(Make("während der Sperre", 5));
            Assert.Equal(["während der Sperre"], store.Entries.Select(e => e.Text));
            Assert.NotNull(store.Problem);
        }
        Assert.Equal(before, File.ReadAllBytes(FilePath));

        // Wieder lesbar: alte Einträge kommen dazu, nichts geht verloren
        store.Add(Make("danach", 6));

        Assert.Null(store.Problem);
        Assert.Equal(["danach", "während der Sperre", "von früher 2", "von früher 1"], store.Entries.Select(e => e.Text));
        Assert.Equal(store.Entries.Select(e => e.Text), new HistoryStore(FilePath).Entries.Select(e => e.Text));
    }

    [Fact]
    public void Gesperrte_Datei_beim_Start_und_Clear_loescht_auch_die_Datei_Inhalte()
    {
        var earlier = new HistoryStore(FilePath);
        earlier.Add(Make("vertraulich", 0));

        HistoryStore store;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            store = new HistoryStore(FilePath);
        }
        // „Alles löschen“ meint auch, was in der (beim Start nicht lesbaren) Datei stand
        store.Clear();
        store.Add(Make("neu", 1));

        Assert.Equal(["neu"], new HistoryStore(FilePath).Entries.Select(e => e.Text));
    }

    [Fact]
    public async Task Leser_sieht_nie_eine_halbe_Datei()
    {
        var store = new HistoryStore(FilePath, limit: 5000);
        store.Add(Make("start", 0));
        var longText = new string('ä', 4000) + "\r\n;\"" + new string('x', 4000);
        using var stop = new CancellationTokenSource();
        using var firstRead = new ManualResetEventSlim();
        var reads = 0;
        var lastCount = 0;

        // Eigener Thread: Der Leser läuft bis zum Ende des Tests und belegte sonst einen Pool-Thread
        var reader = OwnThreads.Start(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                byte[] bytes;
                try
                {
                    using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    bytes = new byte[stream.Length];
                    stream.ReadExactly(bytes);
                }
                catch (IOException)
                {
                    continue; // gerade ersetzt
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                // Jede gelesene Fassung ist vollständiges JSON, und es werden nie weniger Einträge
                using var doc = JsonDocument.Parse(bytes);
                var count = doc.RootElement.GetProperty("Entries").GetArrayLength();
                Assert.True(count >= lastCount, $"{count} Einträge nach {lastCount}");
                lastCount = count;
                reads++;
                firstRead.Set();
                Thread.Sleep(1);
            }
        });

        // Erst schreiben, wenn der Leser läuft
        Assert.True(firstRead.Wait(TimeSpan.FromSeconds(30)), "Der Leser kam nicht zum Zug.");
        for (var i = 1; i <= 150; i++) store.Add(Make(longText + i, i));
        stop.Cancel();
        await reader;

        Assert.True(reads > 0);
        store.Add(Make("ende", 1000));
        Assert.Null(store.Problem);
        Assert.Equal(152, new HistoryStore(FilePath, limit: 5000).Entries.Count);
        Assert.Equal(["verlauf.json"], _temp.FileNames());
    }
}
