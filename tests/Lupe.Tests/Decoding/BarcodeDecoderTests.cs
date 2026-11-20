// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Text;
using Lupe.Core.Decoding;
using Lupe.Core.Model;
using Lupe.Tests.TestSupport;

namespace Lupe.Tests.Decoding;

public sealed class BarcodeDecoderTests
{
    private static readonly BarcodeDecoder Decoder = new();

    /// <summary>Formate aus <see cref="BarcodeDecoder.AllFormats"/>, für die zxing-cpp einen Schreiber hat.</summary>
    public static TheoryData<string> WritableFormats()
    {
        var data = new TheoryData<string>();
        foreach (var name in BarcodeDecoder.AllFormats)
            if (CanWrite(Enum.Parse<BarcodeFormat>(name))) data.Add(name);
        return data;
    }

    internal static bool CanWrite(BarcodeFormat format)
    {
        try
        {
            TestCodes.Modules(DecodingImages.SampleText(format), format);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    internal static string Expected(BarcodeFormat format) => format switch
    {
        // Codabar: Start-/Stoppzeichen (A … B) gehören nicht zum Inhalt
        BarcodeFormat.CODABAR => "40156",
        // DataBar: die 14-stellige GTIN mit Prüfziffer, ohne den Datenbezeichner (01)
        BarcodeFormat.RSS_14 or BarcodeFormat.RSS_LIMITED => "09501101530010",
        _ => DecodingImages.SampleText(format),
    };

    private static void AssertInside(DecodedCode code, int left, int top, int width, int height, int tolerance = 3)
    {
        Assert.NotEmpty(code.Points);
        foreach (var (x, y) in code.Points)
        {
            Assert.InRange(x, left - tolerance, left + width + tolerance);
            Assert.InRange(y, top - tolerance, top + height + tolerance);
        }
    }

    // ---------- Formate ----------

    [Fact]
    public void Jedes_Format_laesst_sich_schreiben()
    {
        // Mit ZXing.Net fehlten Schreiber für MaxiCode, DataBar und IMB – sie ließen sich nur mit fertigen Bildern prüfen.
        // zxing-cpp (mit libzint) schreibt alle Formate, die Lupe liest: Jedes läuft also durch die Rundlauf- und
        // Robustheitstests.
        Assert.All(BarcodeDecoder.AllFormats, f => Assert.True(CanWrite(Enum.Parse<BarcodeFormat>(f)), f));
    }

    [Theory]
    [MemberData(nameof(WritableFormats))]
    public void Rundlauf_jedes_schreibbaren_Formats(string formatName)
    {
        var format = Enum.Parse<BarcodeFormat>(formatName);
        var img = DecodingImages.RenderSized(format);

        var code = Assert.Single(Decoder.Decode(img));

        Assert.Equal(formatName, code.Format);
        Assert.Equal(Expected(format), code.Text);
        AssertInside(code, 0, 0, img.Width, img.Height, tolerance: 0);
        Assert.Equal(DecodingImages.IsOneD(format) ? 2 : 4, code.Points.Count);
    }

    [Theory]
    [MemberData(nameof(WritableFormats))]
    public void Formatfilter_laesst_nur_das_Gewuenschte_durch(string formatName)
    {
        var format = Enum.Parse<BarcodeFormat>(formatName);
        var img = DecodingImages.RenderSized(format);

        Assert.Single(new BarcodeDecoder([formatName]).Decode(img));
        // Gegenprobe mit einem anderen Format (beim QR-Code mit Data Matrix)
        var other = format == BarcodeFormat.QR_CODE ? "DATA_MATRIX" : "QR_CODE";
        Assert.Empty(new BarcodeDecoder([other]).Decode(img));
    }

    [Fact]
    public void Gemischtes_Bild_mit_Formatfilter()
    {
        var canvas = DecodingImages.Solid(1100, 400, 255);
        canvas = DecodingImages.Paste(canvas, TestCodes.Render("QR daneben", width: 200, height: 200), 20, 100);
        canvas = DecodingImages.Paste(canvas, DecodingImages.RenderSized(BarcodeFormat.EAN_13, scale: 2), 260, 60);

        Assert.Equal(["EAN_13"], new BarcodeDecoder(["EAN_13"]).Decode(canvas).Select(c => c.Format));
        Assert.Equal(["QR_CODE"], new BarcodeDecoder(["qr_code"]).Decode(canvas).Select(c => c.Format));
        Assert.Equal(2, Decoder.Decode(canvas).Count);
    }

    [Fact]
    public void UPC_A_ohne_UPC_A_Filter_wird_als_EAN_13_gemeldet()
    {
        // UPC-A ist ein EAN-13 mit führender 0 – wer nur EAN-13 erlaubt, bekommt ihn in dieser Form
        var img = DecodingImages.RenderSized(BarcodeFormat.UPC_A);
        var code = Assert.Single(new BarcodeDecoder(["EAN_13"]).Decode(img));
        Assert.Equal(("EAN_13", "0036000291452"), (code.Format, code.Text));
    }

    // ---------- Mehrere Codes ----------

    [Fact]
    public void Mehrere_Formate_in_einem_Bild_in_Lesereihenfolge()
    {
        var canvas = DecodingImages.Solid(1400, 900, 255);
        var parts = new (int X, int Y, PixelImage Img, string Format, string Text)[]
        {
            (40, 40, TestCodes.Render("oben links", width: 220, height: 220), "QR_CODE", "oben links"),
            (500, 60, TestCodes.Render("oben Mitte", BarcodeFormat.DATA_MATRIX, 200, 200), "DATA_MATRIX", "oben Mitte"),
            (950, 40, TestCodes.Render("oben rechts", BarcodeFormat.AZTEC, 220, 220), "AZTEC", "oben rechts"),
            (40, 500, TestCodes.Render("unten links", BarcodeFormat.PDF_417, 400, 160), "PDF_417", "unten links"),
            (520, 480, DecodingImages.RenderSized(BarcodeFormat.CODE_128, "UNTEN-MITTE", scale: 1), "CODE_128", "UNTEN-MITTE"),
            (950, 500, DecodingImages.RenderSized(BarcodeFormat.EAN_13), "EAN_13", "4006381333931"),
        };
        foreach (var p in parts) canvas = DecodingImages.Paste(canvas, p.Img, p.X, p.Y);

        var codes = Decoder.Decode(canvas);

        Assert.Equal(parts.Select(p => p.Format + ":" + p.Text), codes.Select(c => c.Format + ":" + c.Text));
        foreach (var p in parts)
            AssertInside(codes.Single(c => c.Text == p.Text), p.X, p.Y, p.Img.Width, p.Img.Height);
    }

    [Fact]
    public void Viele_QR_Codes_dicht_nebeneinander()
    {
        var canvas = DecodingImages.Solid(760, 760, 255);
        var texts = new List<string>();
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
            {
                var text = $"Feld {row}/{col}";
                texts.Add(text);
                canvas = DecodingImages.Paste(canvas, TestCodes.Render(text, width: 240, height: 240, margin: 2), 10 + col * 250, 10 + row * 250);
            }

        var codes = Decoder.Decode(canvas);

        Assert.Equal(texts, codes.Select(c => c.Text));
    }

    [Fact]
    public void Gleicher_Code_zweimal_wird_zusammengefuehrt()
    {
        var qr = TestCodes.Render("doppelt", width: 200, height: 200);
        var canvas = DecodingImages.Paste(DecodingImages.Paste(DecodingImages.Solid(700, 300, 255), qr, 20, 50), qr, 450, 50);

        var code = Assert.Single(Decoder.Decode(canvas));
        Assert.Equal("doppelt", code.Text);
    }

    [Fact]
    public void Gleicher_Text_in_verschiedenen_Formaten_bleibt_getrennt()
    {
        var canvas = DecodingImages.Solid(800, 300, 255);
        canvas = DecodingImages.Paste(canvas, TestCodes.Render("96385074", width: 200, height: 200), 20, 50);
        canvas = DecodingImages.Paste(canvas, DecodingImages.RenderSized(BarcodeFormat.EAN_8), 330, 80);

        Assert.Equal(["QR_CODE:96385074", "EAN_8:96385074"], Decoder.Decode(canvas).Select(c => c.Format + ":" + c.Text));
    }

    // ---------- Text und Bytes ----------

    [Theory]
    [InlineData("UTF-8", false)]
    [InlineData("UTF-8", true)] // ohne ECI-Kennung: zxing-cpp erkennt UTF-8 am Inhalt
    public void Umlaute_und_Emoji_als_UTF8_im_QR(string charset, bool disableEci)
    {
        const string text = "Grüße aus Baden – 10 € – Ωμέγα – 日本 – 😀";
        var img = DecodingImages.RenderQr(text, charset, disableEci);

        var code = Assert.Single(Decoder.Decode(img));

        Assert.Equal(text, code.Text);
        Assert.Equal(Encoding.UTF8.GetBytes(text), code.RawBytes);
    }

    [Theory]
    [InlineData("QR_CODE")]
    [InlineData("DATA_MATRIX")]
    [InlineData("AZTEC")]
    [InlineData("PDF_417")]
    public void Umlaute_in_der_Voreinstellung_der_Schreiber(string formatName)
    {
        // Die meisten Schreiber legen Umlaute als ISO-8859-1 ohne ECI-Kennung ab (wie früher ZXing.Net)
        const string text = "Größe: Übermaß, Äpfel & Öl";
        var img = TestCodes.Render(text, Enum.Parse<BarcodeFormat>(formatName), 360, formatName == "PDF_417" ? 160 : 360);

        Assert.Equal(text, Assert.Single(Decoder.Decode(img)).Text);
    }

    [Theory]
    [InlineData("Groß")]
    [InlineData("Maß")]
    [InlineData("Ä")]
    [InlineData("ß")]
    [InlineData("Käse")]
    [InlineData("Jürgen Müller")]
    [InlineData("Übermaß")]
    [InlineData("Straße 5, 76530 Baden-Baden")]
    public void Kurze_Latin1_Texte_ohne_Zeichensatzangabe(string text)
    {
        // ZXing.Net hielt diese Bytes für Shift_JIS und las sie unter .NET als UTF-8 („Gro�“) – Lupe musste das reparieren.
        // zxing-cpp erkennt ISO-8859-1 selbst; die Reparatur ist entfallen, der Test bleibt, damit es so bleibt.
        var code = Assert.Single(Decoder.Decode(TestCodes.Render(text, width: 200, height: 200)));
        Assert.Equal(text, code.Text);
        Assert.Equal(Encoding.Latin1.GetBytes(text), code.RawBytes);
    }

    [Theory]
    [InlineData("eci=0")]          // ohne Kennung: zxing-cpp erkennt Shift_JIS am Inhalt
    [InlineData("eci=Shift_JIS")]
    public void Japanisch_in_Shift_JIS(string eci)
    {
        // Mit ZXing.Net kam Shift_JIS unter .NET verstümmelt an (keine Shift_JIS-Codepage) – eine bekannte Grenze von Lupe
        byte[] nihongo = [0x93, 0xFA, 0x96, 0x7B, 0x8C, 0xEA, 0x82, 0xCC, 0x83, 0x65, 0x83, 0x4C, 0x83, 0x58, 0x83, 0x67]; // 日本語のテキスト
        var img = TestCodes.RenderBytes(nihongo, BarcodeFormat.QR_CODE, eci, 240, 240);

        Assert.Equal("日本語のテキスト", Assert.Single(Decoder.Decode(img)).Text);
    }

    [Fact]
    public void Binaerinhalt_mit_Nullbytes_bleibt_vollstaendig()
    {
        // zxing-cpp gibt den Text als C-Zeichenkette heraus – ohne Gegenmaßnahme endete er am ersten Nullbyte („A“)
        byte[] data = [0x41, 0x00, 0x42, 0xFF, 0x80, 0x00, 0x7F];
        var code = Assert.Single(Decoder.Decode(TestCodes.RenderBytes(data, BarcodeFormat.QR_CODE, "eci=0", 240, 240)));

        Assert.Equal(Encoding.Latin1.GetString(data), code.Text);
        Assert.Equal(data, code.RawBytes);
    }

    [Fact]
    public void Rohbytes_sind_der_Inhalt_wie_gespeichert()
    {
        // ZXing.Net lieferte Rohbytes nur für Byte-Segmente (QR im Byte-Modus); zxing-cpp liefert den Inhalt jedes Codes
        // als Bytes – im Ziffernmodus und bei Strichcodes eben die Ziffern bzw. Zeichen
        Assert.Equal("0123456789"u8.ToArray(), Assert.Single(Decoder.Decode(TestCodes.Render("0123456789"))).RawBytes);
        Assert.Equal("Lupe 128/ok"u8.ToArray(), Assert.Single(Decoder.Decode(DecodingImages.RenderSized(BarcodeFormat.CODE_128))).RawBytes);
        Assert.Equal(Encoding.Latin1.GetBytes("Lupe ü"), Assert.Single(Decoder.Decode(TestCodes.Render("Lupe ü"))).RawBytes);
    }

    // ---------- Leere und besondere Bilder ----------

    public static TheoryData<string, PixelImage> LeereBilder() => new()
    {
        { "1×1", DecodingImages.Solid(1, 1) },
        { "3×2 schwarz", DecodingImages.Solid(3, 2, 0) },
        { "weiß Full HD", DecodingImages.Solid(1920, 1080, 255) },
        { "schwarz 640×480", DecodingImages.Solid(640, 480, 0) },
        { "ganz durchsichtig", DecodingImages.Solid(300, 200, 0, alpha: 0) },
        { "Rauschen", TestCodes.Noise(DecodingImages.Solid(800, 600, 128), 127, seed: 2) },
        { "unruhig 720p", DecodingImages.Busy(1280, 720, seed: 3) },
        { "unruhig Full HD", DecodingImages.Busy(1920, 1080, seed: 4) },
        { "unruhig 4K", DecodingImages.Busy(3840, 2160, seed: 5) },
        { "schmaler Streifen", DecodingImages.Solid(2000, 3) },
    };

    [Theory]
    [MemberData(nameof(LeereBilder))]
    public void Ohne_Code_kommt_nichts_und_keine_Ausnahme(string name, PixelImage img)
    {
        Assert.NotNull(name);
        Assert.Empty(Decoder.Decode(img));
    }

    [Fact]
    public void Extreme_Seitenverhaeltnisse()
    {
        // Langer Streifen mit Strichcode: Drehvarianten dürfen nicht riesig werden
        var strip = TestCodes.Embed(DecodingImages.RenderSized(BarcodeFormat.CODE_128), 20000, 140, 9000, 10, background: 255);
        var sw = Stopwatch.StartNew();
        Assert.Equal("Lupe 128/ok", Assert.Single(Decoder.Decode(strip)).Text);
        Assert.Empty(Decoder.Decode(DecodingImages.Solid(20000, 60, 255)));
        Assert.True(sw.ElapsedMilliseconds < 5000, $"{sw.ElapsedMilliseconds} ms");
        // Pixelfäden und riesige, aber flache Bilder
        Assert.Empty(Decoder.Decode(DecodingImages.Solid(1, 5000)));
        Assert.Empty(Decoder.Decode(DecodingImages.Solid(9000, 1)));
        Assert.Empty(Decoder.Decode(DecodingImages.Solid(3_000_000, 2)));
    }

    [Fact]
    public void Transparenter_Hintergrund_wird_wie_Weiss_behandelt()
    {
        var img = DecodingImages.TransparentBackground(TestCodes.Render("transparent", width: 200, height: 200));
        Assert.Equal("transparent", Assert.Single(Decoder.Decode(img)).Text);
    }

    [Fact]
    public void Alpha_null_aus_GDI_wird_ignoriert()
    {
        var img = TestCodes.Render("GDI", width: 200, height: 200);
        for (int i = 3; i < img.Bgra.Length; i += 4) img.Bgra[i] = 0;
        Assert.Equal("GDI", Assert.Single(Decoder.Decode(img)).Text);
    }

    [Fact]
    public void Zeilenabstand_groesser_als_noetig()
    {
        var img = TestCodes.Embed(TestCodes.Render("Stride", width: 180, height: 180), 400, 300, 150, 60);
        var plain = Assert.Single(Decoder.Decode(img));
        var strided = Assert.Single(Decoder.Decode(DecodingImages.WithStride(img, 36)));
        Assert.Equal((plain.Format, plain.Text), (strided.Format, strided.Text));
        Assert.Equal(plain.Points, strided.Points);
    }

    [Fact]
    public void Null_ist_ein_Programmierfehler() => Assert.Throws<ArgumentNullException>(() => Decoder.Decode(null!));

    // ---------- Punkte ----------

    [Fact]
    public void QR_Punkte_sind_die_vier_Ecken_im_Uhrzeigersinn()
    {
        // Version 1 (21 Module), 6 Pixel je Modul, Ruhezone 4 Module: Code von 24 bis 150 im Bild, Bild bei (300, 200)
        var qr = TestCodes.Render("Hallo Lupe", width: 174, height: 174, margin: 4);
        Assert.Equal(174, qr.Width);
        var img = TestCodes.Embed(qr, 800, 600, 300, 200);

        var code = Assert.Single(Decoder.Decode(img));

        (float X, float Y)[] expected = [(324, 224), (450, 224), (450, 350), (324, 350)];
        Assert.Equal(4, code.Points.Count);
        for (int i = 0; i < 4; i++)
        {
            Assert.InRange(code.Points[i].X, expected[i].X - 2, expected[i].X + 2);
            Assert.InRange(code.Points[i].Y, expected[i].Y - 2, expected[i].Y + 2);
        }
    }

    [Fact]
    public void Strichcode_Punkte_liegen_auf_einer_Lesezeile_ueber_die_Striche()
    {
        var bars = DecodingImages.RenderSized(BarcodeFormat.CODE_128, scale: 2);
        var img = TestCodes.Embed(bars, 1200, 700, 250, 300);

        var code = Assert.Single(Decoder.Decode(img));

        Assert.Equal(2, code.Points.Count);
        AssertInside(code, 250, 300, bars.Width, bars.Height);
        Assert.True(Math.Abs(code.Points[0].Y - code.Points[1].Y) < 1);
        Assert.True(Math.Abs(code.Points[1].X - code.Points[0].X) > bars.Width * 0.6);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Punkte_gedrehter_Codes_im_Originalbild(int quarterTurns)
    {
        var qr = TestCodes.Rotate90(TestCodes.Render("gedreht", width: 200, height: 200), quarterTurns);
        var img = TestCodes.Embed(qr, 900, 500, 600, 150);

        var code = Assert.Single(Decoder.Decode(img));

        AssertInside(code, 600, 150, 200, 200);
    }

    // ---------- Abbruch und Threads ----------

    [Fact]
    public void Abbruch_vorher()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => Decoder.Decode(TestCodes.Render("x"), cts.Token));
    }

    [Fact]
    public void Abbruch_waehrend_der_Erkennung_greift_schnell()
    {
        var img = DecodingImages.Busy(3840, 2160, seed: 8);
        // Wie viele Lesevorgänge die Erkennung ohne Abbruch macht (das Bild hat keinen Code, also alle Stufen)
        int total = 0;
        var counting = new BarcodeDecoder { Reader = (g, s) => { Interlocked.Increment(ref total); return NativeReader.Read(g, s); } };
        Assert.Empty(counting.Decode(img));

        // Abbruch beim ersten echten Lesevorgang, also sicher mitten in der Erkennung – ohne Wettlauf mit einem Zeitgeber
        // oder Thread, der unter Last zu spät drankommt (auf schnellen Macs ist die Erkennung nach 40 ms durch)
        using var cts = new CancellationTokenSource();
        int calls = 0;
        var decoder = new BarcodeDecoder
        {
            Reader = (g, s) =>
            {
                if (Interlocked.Increment(ref calls) == 1) cts.Cancel();
                return NativeReader.Read(g, s);
            },
        };
        var sw = Stopwatch.StartNew();

        Assert.ThrowsAny<OperationCanceledException>(() => decoder.Decode(img, cts.Token));

        Assert.True(sw.ElapsedMilliseconds < 1000, $"Abbruch erst nach {sw.ElapsedMilliseconds} ms");
        // Nur die schon laufenden Lesevorgänge enden noch, die übrigen beginnen nicht mehr
        Assert.True(Volatile.Read(ref calls) < total, $"{calls} von {total} Lesevorgängen trotz Abbruch");
        // Die Instanz bleibt benutzbar
        Assert.Equal("weiter", Assert.Single(decoder.Decode(TestCodes.Render("weiter"))).Text);
    }

    [Fact]
    public void Eine_Instanz_aus_vielen_Threads()
    {
        var images = Enumerable.Range(0, 6).Select(i => (Text: $"Thread {i}", Img: TestCodes.Embed(
            TestCodes.Render($"Thread {i}", width: 180, height: 180), 640, 480, 40 + i * 60, 30 + i * 40))).ToArray();

        var results = new string[24];
        Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 },
            i => results[i] = Assert.Single(Decoder.Decode(images[i % images.Length].Img)).Text);

        for (int i = 0; i < results.Length; i++) Assert.Equal(images[i % images.Length].Text, results[i]);
    }
}

public sealed class LiveBarcodeDecoderTests
{
    private static readonly LiveBarcodeDecoder Live = new();

    [Fact]
    public void Findet_im_Kamerabild_was_dort_typisch_ist()
    {
        var frame = DecodingImages.Busy(1280, 720, seed: 50);
        frame = DecodingImages.Paste(frame, TestCodes.Render("QR live", width: 200, height: 200), 60, 60);
        frame = DecodingImages.Paste(frame, TestCodes.Render("DM live", BarcodeFormat.DATA_MATRIX, 160, 160), 1000, 80);
        frame = DecodingImages.Paste(frame, TestCodes.Render("Aztec live", BarcodeFormat.AZTEC, 200, 200), 520, 420);
        frame = DecodingImages.Paste(frame, TestCodes.Invert(TestCodes.Render("hell live", width: 180, height: 180)), 980, 480);
        frame = DecodingImages.Paste(frame, TestCodes.Rotate90(DecodingImages.RenderSized(BarcodeFormat.EAN_13), 1), 330, 300);

        Assert.Equal(["AZTEC:Aztec live", "DATA_MATRIX:DM live", "EAN_13:4006381333931", "QR_CODE:QR live", "QR_CODE:hell live"],
            Live.Decode(frame).Select(c => c.Format + ":" + c.Text).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Formatfilter_wie_beim_gruendlichen_Decoder()
    {
        var img = DecodingImages.RenderSized(BarcodeFormat.CODE_128);
        Assert.Empty(new LiveBarcodeDecoder(["QR_CODE"]).Decode(img));
        Assert.Single(new LiveBarcodeDecoder(["code_128"]).Decode(img));
    }

    [Fact]
    public void Sonderfaelle_bleiben_dem_gruendlichen_Decoder()
    {
        // Ein heller Strichcode auf dunklem Grund braucht die invertierte Fassung, ein schräger Strichcode die gedrehten,
        // ein Strichcode, der nur eine einzige Pixelzeile hoch ist, die Vergrößerung (zxing-cpp will ihn auf zwei Zeilen
        // gleich lesen) – das alles spart sich der Kamerastrom
        var invertedBars = TestCodes.Invert(DecodingImages.RenderSized(BarcodeFormat.CODE_128));
        var slanted = DecodingImages.Rotate(DecodingImages.RenderSized(BarcodeFormat.CODE_128, scale: 2), 30);
        var flat = LineConfirmationTests.OneRowBarcode();

        foreach (var img in new[] { invertedBars, slanted, flat })
        {
            Assert.Empty(Live.Decode(img));
            Assert.Single(new BarcodeDecoder().Decode(img));
        }
    }

    [Fact]
    public void Kleine_und_helle_2D_Codes_auch_live()
    {
        // Einen QR-Code mit einem Pixel je Modul liest zxing-cpp schon im ganzen Bild; helle QR-Codes kehrt es selbst um,
        // helles PDF417 liest die invertierte Fassung
        var tinyQr = TestCodes.Embed(TestCodes.Render("winzig", width: 1, height: 1, margin: 1), 60, 40, 20, 8, background: 255);
        Assert.Equal("winzig", Assert.Single(Live.Decode(tinyQr)).Text);
        Assert.Equal("hell", Assert.Single(Live.Decode(TestCodes.Invert(TestCodes.Render("hell", width: 200, height: 200)))).Text);
        Assert.Equal(DecodingImages.SampleText(BarcodeFormat.PDF_417),
            Assert.Single(Live.Decode(TestCodes.Invert(DecodingImages.RenderSized(BarcodeFormat.PDF_417)))).Text);
    }

    [Fact]
    public void Abbruch()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => Live.Decode(TestCodes.Render("x"), cts.Token));
    }

    [Fact]
    public void Kein_Phantom_im_verrauschten_Kamerabild()
    {
        // Hier setzte ZXings gestapelte DataBar-Suche früher ein RSS_EXPANDED mit leerem Inhalt zusammen – im
        // Dauerbetrieb mit 10 Bildern je Sekunde wäre das etwa alle paar Sekunden ein falscher Treffer gewesen
        var frame = TestCodes.Noise(DecodingImages.Busy(1280, 720, seed: 3046), 66, seed: 46);
        Assert.Empty(Live.Decode(frame));
    }
}
