using Lupe.Tests.TestSupport;
using System.Text;
using Lupe.Core.History;
using Lupe.Core.Model;
using static Lupe.Tests.History.TestEntries;

namespace Lupe.Tests.History;

/// <summary>CSV-Export: Kodierung, Format, Quoting und Schutz vor CSV-Injektion – über echte Dateien.</summary>
public sealed class HistoryCsvTests : IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly TempFolder _temp = new();
    private string CsvPath => _temp.File("export.csv");

    public void Dispose() => _temp.Dispose();

    private static HistoryEntry Local(string text, int hour = 7, string format = "QR_CODE", ScanSource source = ScanSource.Kamera)
    {
        var local = new DateTime(2026, 3, 5, hour, 8, 9, DateTimeKind.Unspecified);
        return new HistoryEntry
        {
            Time = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)),
            Text = text,
            Format = format,
            Source = source,
        };
    }

    private string Export(params HistoryEntry[] entries)
    {
        var store = new HistoryStore(_temp.File("verlauf.json"), persist: false);
        foreach (var entry in entries) store.Add(entry);
        store.ExportCsv(CsvPath);
        var bytes = File.ReadAllBytes(CsvPath);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        return StrictUtf8.GetString(bytes, 3, bytes.Length - 3);
    }

    /// <summary>Liest CSV nach RFC 4180 mit Semikolon (wie Excel) – unabhängig von der Implementierung.</summary>
    private static List<string[]> Parse(string csv)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"' && field.Length == 0) quoted = true;
            else if (c == ';') { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
            {
                row.Add(field.ToString()); field.Clear();
                rows.Add([.. row]); row.Clear();
                i++;
            }
            else
            {
                Assert.False(c is '"' or '\r' or '\n', $"Ungeschütztes Zeichen {(int)c} an Stelle {i}");
                field.Append(c);
            }
        }
        Assert.False(quoted, "Anführungszeichen nicht geschlossen");
        Assert.True(field.Length == 0 && row.Count == 0, "Letzte Zeile ohne CRLF");
        return rows;
    }

    [Fact]
    public void Kopfzeile_BOM_Semikolon_CRLF_und_Zeit_in_Ortszeit()
    {
        var csv = Export(Local("4006381333931", format: "EAN_13", source: ScanSource.Bildschirm));

        Assert.Equal("Zeit;Format;Quelle;Inhalt\r\n05.03.2026 07:08:09;EAN_13;Bildschirm;4006381333931\r\n", csv);
    }

    [Fact]
    public void Zeit_aus_anderer_Zeitzone_wird_in_Ortszeit_umgerechnet()
    {
        var utc = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        var entry = new HistoryEntry { Time = utc.ToOffset(TimeSpan.FromHours(-7)), Text = "x", Format = "QR_CODE", Source = ScanSource.Datei };

        var rows = Parse(Export(entry));

        Assert.Equal(utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), rows[1][0]);
    }

    [Fact]
    public void Zeitformat_haengt_nicht_von_der_Kultur_ab()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            // Finnisch nutzt „.“ als Zeittrenner, Arabisch andere Ziffern/Kalender
            foreach (var culture in new[] { "fi-FI", "ar-SA", "en-US", "de-DE" })
            {
                Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(culture);
                var rows = Parse(Export(Local("x", hour: 23)));
                Assert.Equal("05.03.2026 23:08:09", rows[1][0]);
            }
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Neueste_zuerst_mit_Quelle_und_Format()
    {
        var rows = Parse(Export(
            Local("alt", hour: 6, format: "CODE_128", source: ScanSource.Datei),
            Local("neu", hour: 8, format: "QR_CODE", source: ScanSource.Zwischenablage)));

        Assert.Equal(3, rows.Count);
        Assert.Equal(["Zeit", "Format", "Quelle", "Inhalt"], rows[0]);
        Assert.Equal(["QR_CODE", "Zwischenablage", "neu"], rows[1][1..]);
        Assert.Equal(["CODE_128", "Datei", "alt"], rows[2][1..]);
    }

    [Fact]
    public void Leerer_Verlauf_ergibt_nur_die_Kopfzeile()
    {
        Assert.Equal("Zeit;Format;Quelle;Inhalt\r\n", Export());
    }

    [Theory]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("sagte \"Hallo\"", "\"sagte \"\"Hallo\"\"\"")]
    [InlineData("Zeile 1\r\nZeile 2", "\"Zeile 1\r\nZeile 2\"")]
    [InlineData("nur LF\nhier", "\"nur LF\nhier\"")]
    [InlineData("nur CR\rhier", "\"nur CR\rhier\"")]
    [InlineData(" Leerzeichen am Rand ", "\" Leerzeichen am Rand \"")]
    [InlineData("\"", "\"\"\"\"")]
    [InlineData("", "")]
    [InlineData("https://example.com/?a=1&b=2", "https://example.com/?a=1&b=2")]
    [InlineData("Grüße 😀", "Grüße 😀")]
    [InlineData("a=b+c-d@e", "a=b+c-d@e")]
    public void Werte_werden_korrekt_gequotet(string text, string expectedField)
    {
        var csv = Export(Local(text));

        Assert.EndsWith(";" + expectedField + "\r\n", csv);
        Assert.Equal(text, Parse(csv)[1][3]);
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("=HYPERLINK(\"https://evil.example\";\"klick\")")]
    [InlineData("+49 721 123")]
    [InlineData("-5")]
    [InlineData("-2+3+cmd|' /C calc'!A0")]
    [InlineData("@SUM(1+1)*cmd|' /C calc'!A0")]
    [InlineData("\t=1+1")]
    [InlineData("\r=1+1")]
    [InlineData("\n=1+1")]
    [InlineData("  =1+1")]
    [InlineData("\uFF1D1+1")] // ＝
    [InlineData("\uFF0B1")]   // ＋
    [InlineData("\uFF0D1")]   // －
    [InlineData("\uFF20SUM(1)")] // ＠
    public void CSV_Injektion_wird_mit_Apostroph_entschaerft(string text)
    {
        var field = Parse(Export(Local(text)))[1][3];

        Assert.Equal("'" + text, field);
    }

    [Fact]
    public void Trenner_im_Inhalt_kann_keine_neue_Zelle_mit_Formel_beginnen()
    {
        const string attack = "x;=cmd|' /C calc'!A0;\"=1+1\"\r\n=2+2";

        var rows = Parse(Export(Local(attack)));

        Assert.Equal(2, rows.Count);
        Assert.Equal(4, rows[1].Length);
        Assert.Equal(attack, rows[1][3]);
    }

    [Fact]
    public void Auch_Format_und_Quelle_werden_entschaerft()
    {
        var rows = Parse(Export(Local("x", format: "=FORMAT;X")));

        Assert.Equal("'=FORMAT;X", rows[1][1]);
    }

    [Fact]
    public void Zufaellige_Inhalte_ueberstehen_den_Rundlauf()
    {
        var random = new Random(4711);
        const string alphabet = "ab ;\"\r\n\t=+-@'äß😀,|\\";
        var texts = Enumerable.Range(0, 300).Select(_ =>
        {
            var length = random.Next(0, 25);
            var builder = new StringBuilder();
            while (builder.Length < length)
            {
                var index = random.Next(alphabet.Length);
                // Surrogatpaar nicht zerreißen
                if (char.IsSurrogate(alphabet[index])) builder.Append("😀");
                else builder.Append(alphabet[index]);
            }
            return builder.ToString();
        }).ToArray();
        var entries = texts.Select((t, i) => Local(t) with { Time = Local(t).Time.AddSeconds(-i) }).ToArray();

        var rows = Parse(Export(entries));

        Assert.Equal(texts.Length + 1, rows.Count);
        for (var i = 0; i < texts.Length; i++)
        {
            var text = texts[i];
            var trimmed = text.TrimStart();
            var dangerous = text.Length > 0 && ("=+-@\t\r\n".Contains(text[0]) || (trimmed.Length > 0 && "=+-@".Contains(trimmed[0])));
            Assert.Equal(dangerous ? "'" + text : text, rows[i + 1][3]);
            Assert.Equal(4, rows[i + 1].Length);
        }
    }

    [Fact]
    public void Einzelnes_Surrogat_bricht_den_Export_nicht()
    {
        var csv = Export(Local("kaputt \uD800 hier"));

        Assert.Contains("kaputt \uFFFD hier", csv);
    }

    [Fact]
    public void Export_ueberschreibt_eine_vorhandene_Datei_vollstaendig()
    {
        File.WriteAllText(CsvPath, new string('x', 10_000));

        var csv = Export(Local("kurz"));

        Assert.Equal(2, Parse(csv).Count);
        Assert.Equal(["export.csv"], _temp.FileNames());
    }

    [Fact]
    public void Export_aus_gespeichertem_Verlauf_nach_dem_Neuladen()
    {
        var path = _temp.File("verlauf.json");
        var first = new HistoryStore(path);
        first.Add(Local("a;b", hour: 6));
        first.Add(Local("=c", hour: 7));

        new HistoryStore(path).ExportCsv(CsvPath);

        var rows = Parse(File.ReadAllText(CsvPath, Encoding.UTF8));
        Assert.Equal(["'=c", "a;b"], rows.Skip(1).Select(r => r[3]));
    }

    [Fact]
    public void Export_in_einen_unmoeglichen_Ordner_meldet_sich_deutsch()
    {
        // Eine Datei, wo ein Ordner sein müsste
        File.WriteAllText(_temp.File("blockiert"), "");
        var store = new HistoryStore(persist: false);
        store.Add(Local("x"));

        var ex = Assert.Throws<IOException>(() => store.ExportCsv(_temp.File("blockiert", "export.csv")));

        Assert.StartsWith("Der Verlauf konnte nicht nach", ex.Message);
        Assert.Contains("exportiert werden", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [WindowsFact]
    public void Export_ueber_eine_in_Excel_geoeffnete_Datei_meldet_sich_deutsch()
    {
        File.WriteAllText(CsvPath, "alt");
        var store = new HistoryStore(persist: false);
        store.Add(Local("x"));

        IOException ex;
        // Excel öffnet CSV-Dateien mit Schreibsperre
        using (new FileStream(CsvPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ex = Assert.Throws<IOException>(() => store.ExportCsv(CsvPath));
        }

        Assert.Contains("exportiert werden", ex.Message);
        Assert.Contains("anderen Programm", ex.Message);
        Assert.Equal("alt", File.ReadAllText(CsvPath));
        Assert.Equal(["export.csv"], _temp.FileNames());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Leerer_Pfad_ist_ein_Programmierfehler(string path)
    {
        var store = new HistoryStore(persist: false);

        Assert.ThrowsAny<ArgumentException>(() => store.ExportCsv(path));
    }
}
