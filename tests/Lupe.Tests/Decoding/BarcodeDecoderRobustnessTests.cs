// SPDX-License-Identifier: MIT
using Lupe.Core.Decoding;
using Lupe.Core.Model;
using Lupe.Tests.TestSupport;

namespace Lupe.Tests.Decoding;

/// <summary>Reale Bildfehler: gedreht, schräg, invertiert, verrauscht, unscharf, winzig, am Rand, in unruhiger Umgebung.</summary>
public sealed class BarcodeDecoderRobustnessTests
{
    private static readonly BarcodeDecoder Decoder = new();

    public static TheoryData<string> WritableFormats() => BarcodeDecoderTests.WritableFormats();

    /// <summary>
    /// Alle schreibbaren Formate außer MaxiCode: Den liest zxing-cpp – wie zuvor ZXing.Net – nur ungedreht auf ruhigem
    /// Grund (ohne Suchmuster-Erkennung, es nimmt den Umriss aller dunklen Pixel als Code). Verrauscht, gedreht oder in
    /// einem unruhigen Bild findet es ihn nicht; das steht in den Grenzen (docs/limitations.md). Geprüft wird MaxiCode
    /// im Rundlauf, invertiert und mitten auf einer ruhigen Fläche (ForeignFormatTests).
    /// </summary>
    public static TheoryData<string> DetectableFormats()
    {
        var data = new TheoryData<string>();
        foreach (var name in BarcodeDecoder.AllFormats)
            if (name != "MAXICODE" && BarcodeDecoderTests.CanWrite(Enum.Parse<BarcodeFormat>(name))) data.Add(name);
        return data;
    }

    private static void AssertFound(PixelImage img, BarcodeFormat format, string? text = null)
    {
        var codes = Decoder.Decode(img);
        var code = Assert.Single(codes);
        Assert.Equal(format.ToString(), code.Format);
        Assert.Equal(text ?? BarcodeDecoderTests.Expected(format), code.Text);
    }

    // ---------- Drehung ----------

    [Theory]
    [MemberData(nameof(DetectableFormats))]
    public void Um_90_180_und_270_Grad_gedreht(string formatName)
    {
        var format = Enum.Parse<BarcodeFormat>(formatName);
        var img = DecodingImages.RenderSized(format);
        for (int turns = 1; turns <= 3; turns++)
            AssertFound(TestCodes.Rotate90(img, turns), format);
    }

    [Theory]
    [InlineData("QR_CODE", 10, 1)]
    [InlineData("QR_CODE", 27, 1)]
    [InlineData("QR_CODE", 45, 1)]
    [InlineData("QR_CODE", -33, 1)]
    [InlineData("DATA_MATRIX", 20, 1)]
    [InlineData("DATA_MATRIX", -35, 1)]
    [InlineData("DATA_MATRIX", 45, 2)]
    [InlineData("AZTEC", 8, 2)]
    [InlineData("AZTEC", 17, 2)]
    [InlineData("AZTEC", 33, 2)]
    [InlineData("AZTEC", -24, 2)]
    [InlineData("PDF_417", 12, 2)]
    [InlineData("PDF_417", 25, 2)]
    [InlineData("PDF_417", -38, 2)]
    [InlineData("CODE_128", 12, 2)]
    [InlineData("CODE_128", 30, 2)]
    [InlineData("CODE_128", -42, 2)]
    [InlineData("EAN_13", 20, 2)]
    [InlineData("EAN_13", -35, 2)]
    [InlineData("UPC_A", 30, 2)]
    [InlineData("CODE_39", 25, 2)]
    [InlineData("ITF", 30, 2)]
    [InlineData("CODABAR", -28, 2)]
    public void Schraeglage(string formatName, double angle, int scale)
    {
        var format = Enum.Parse<BarcodeFormat>(formatName);
        var img = DecodingImages.Rotate(DecodingImages.RenderSized(format, scale: scale), angle);
        // Genau ein Treffer: keine Phantom-EAN-8/UPC-E aus schräg geschnittenen Zeilen, keine ITF-Bruchstücke
        AssertFound(img, format);
    }

    [Fact]
    public void Schraeger_Code_im_unruhigen_Bild_mit_Punkten_im_Original()
    {
        var code = DecodingImages.Rotate(TestCodes.Render("schräg und unruhig", BarcodeFormat.AZTEC, 300, 300), 25);
        var img = DecodingImages.Paste(DecodingImages.Busy(1280, 720, seed: 21), code, 600, 200);

        var found = Assert.Single(Decoder.Decode(img));

        Assert.Equal("schräg und unruhig", found.Text);
        Assert.All(found.Points, p =>
        {
            Assert.InRange(p.X, 600, 600 + code.Width);
            Assert.InRange(p.Y, 200, 200 + code.Height);
        });
    }

    // ---------- Invertiert ----------

    [Theory]
    [MemberData(nameof(WritableFormats))]
    public void Invertiert(string formatName)
    {
        var format = Enum.Parse<BarcodeFormat>(formatName);
        AssertFound(TestCodes.Invert(DecodingImages.RenderSized(format)), format);
    }

    [Fact]
    public void Heller_QR_Code_im_Dunkelmodus_Bildschirm()
    {
        var dark = DecodingImages.Solid(1920, 1080, 30);
        var img = DecodingImages.Paste(dark, TestCodes.Invert(TestCodes.Render("dunkel", width: 150, height: 150)), 1500, 800);
        AssertFound(img, BarcodeFormat.QR_CODE, "dunkel");
    }

    [Fact]
    public void Normaler_und_invertierter_Code_im_selben_Bild()
    {
        var canvas = DecodingImages.Solid(800, 400, 255);
        canvas = DecodingImages.Paste(canvas, TestCodes.Render("normal", width: 220, height: 220), 40, 80);
        canvas = DecodingImages.Paste(canvas, TestCodes.Invert(TestCodes.Render("invertiert", width: 220, height: 220)), 450, 80);

        Assert.Equal(["normal", "invertiert"], Decoder.Decode(canvas).Select(c => c.Text));
    }

    // ---------- Rauschen und Unschärfe ----------

    [Theory]
    [MemberData(nameof(DetectableFormats))]
    public void Verrauscht(string formatName)
    {
        var format = Enum.Parse<BarcodeFormat>(formatName);
        AssertFound(TestCodes.Noise(DecodingImages.RenderSized(format), 50, seed: 7), format);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(110)]
    public void QR_mit_starkem_Rauschen(int amount) =>
        AssertFound(TestCodes.Noise(TestCodes.Render("laut", width: 300, height: 300), amount, seed: 9), BarcodeFormat.QR_CODE, "laut");

    [Theory]
    [InlineData("QR_CODE", 2)]
    [InlineData("DATA_MATRIX", 1)]
    [InlineData("AZTEC", 1)]
    [InlineData("PDF_417", 1)]
    [InlineData("CODE_128", 1)]
    [InlineData("EAN_13", 1)]
    public void Unscharf(string formatName, int radius)
    {
        var format = Enum.Parse<BarcodeFormat>(formatName);
        AssertFound(DecodingImages.Blur(DecodingImages.RenderSized(format, scale: 2), radius), format);
    }

    [Fact]
    public void Kamerabild_mit_Rauschen_Unschaerfe_und_Schraeglage()
    {
        var code = DecodingImages.Rotate(TestCodes.Render("Kamera", width: 260, height: 260), 12);
        var frame = DecodingImages.Paste(DecodingImages.Busy(1280, 720, seed: 30), code, 500, 220);
        frame = TestCodes.Noise(DecodingImages.Blur(frame, 1), 25, seed: 31);
        AssertFound(frame, BarcodeFormat.QR_CODE, "Kamera");
    }

    // ---------- Klein, groß, am Rand ----------

    [Fact]
    public void Kleiner_QR_Code_in_4K_Bildschirmfoto()
    {
        // 60 × 60 Pixel, 2 Pixel je Modul, unten rechts auf einem unruhigen 4K-Bildschirm
        var qr = TestCodes.Render("Lupe", width: 60, height: 60);
        var img = DecodingImages.Paste(DecodingImages.Busy(3840, 2160, seed: 12), qr, 3400, 1900);

        var code = Assert.Single(Decoder.Decode(img));

        Assert.Equal("Lupe", code.Text);
        Assert.All(code.Points, p =>
        {
            Assert.InRange(p.X, 3400, 3460);
            Assert.InRange(p.Y, 1900, 1960);
        });
    }

    [Fact]
    public void Kleine_Codes_ueber_Kachelgrenzen_hinweg_im_4K_Bild()
    {
        var canvas = DecodingImages.Busy(3840, 2160, seed: 13);
        // Genau auf Kachelgrenzen (704, 1024, 1408 …) und in der Übersicht zu klein
        canvas = DecodingImages.Paste(canvas, TestCodes.Render("Grenze 1", width: 70, height: 70), 990, 680);
        canvas = DecodingImages.Paste(canvas, TestCodes.Render("Grenze 2", width: 70, height: 70), 2780, 1100);
        canvas = DecodingImages.Paste(canvas, TestCodes.Render("Ecke", width: 70, height: 70), 3770, 2090);
        // und ein großer, der über mehrere Kacheln reicht
        canvas = DecodingImages.Paste(canvas, TestCodes.Render("Groß", width: 900, height: 900), 1500, 300);

        // Lesereihenfolge: „Grenze 1“ und „Grenze 2“ liegen neben dem großen Code (gleiche Zeile), „Ecke“ darunter
        Assert.Equal(["Grenze 1", "Groß", "Grenze 2", "Ecke"], Decoder.Decode(canvas).Select(c => c.Text));
    }

    [Fact]
    public void Winziger_QR_Code_mit_einem_Pixel_je_Modul()
    {
        // 21 × 21 Module mit je 1 Pixel – nur über die vergrößerte Variante lesbar
        var qr = TestCodes.Render("winzig", width: 1, height: 1, margin: 1);
        Assert.InRange(qr.Width, 23, 25);
        var img = TestCodes.Embed(qr, 60, 40, 20, 8, background: 255);

        var code = Assert.Single(Decoder.Decode(img));

        Assert.Equal("winzig", code.Text);
        Assert.All(code.Points, p => Assert.InRange(p.X, 19, 45));
    }

    [Fact]
    public void Vom_Browser_verkleinerter_QR_Code()
    {
        // 4 Pixel je Modul auf 1,6 Pixel je Modul heruntergerechnet – graue Kanten, keine ganzen Pixel
        var qr = TestCodes.Render("https://ict-baden.de/x", width: 33 * 4, height: 33 * 4);
        var small = DecodingImages.Resize(qr, 53, 53);
        AssertFound(TestCodes.Embed(small, 300, 200, 120, 70, background: 255), BarcodeFormat.QR_CODE, "https://ict-baden.de/x");
    }

    [Theory]
    [InlineData("QR_CODE")]
    [InlineData("DATA_MATRIX")]
    [InlineData("AZTEC")]
    public void Knapp_ausgeschnitten_ohne_Ruhezone(string formatName)
    {
        // Wie ein eng gezogener Bildschirmausschnitt: der Code reicht bis an den Bildrand
        var format = Enum.Parse<BarcodeFormat>(formatName);
        var img = TestCodes.Render("knapp", format, 200, 200, margin: 0);
        AssertFound(img, format, "knapp");
        AssertFound(TestCodes.Invert(img), format, "knapp");
    }

    [Fact]
    public void Knapp_ausgeschnittener_Strichcode()
    {
        var img = TestCodes.Render("KNAPP-128", BarcodeFormat.CODE_128, 300, 80, margin: 0);
        AssertFound(img, BarcodeFormat.CODE_128, "KNAPP-128");
    }

    [Fact]
    public void Code_fuellt_ein_grosses_Foto()
    {
        // 12-Megapixel-Foto, Code über die halbe Bildhöhe – wird in der Übersicht gefunden, nicht in den Kacheln
        var qr = TestCodes.Render("Foto", width: 2000, height: 2000);
        AssertFound(TestCodes.Embed(qr, 4000, 3000, 1000, 500, background: 180), BarcodeFormat.QR_CODE, "Foto");
    }

    // ---------- Unruhige Umgebung ----------

    [Theory]
    [MemberData(nameof(DetectableFormats))]
    public void Im_unruhigen_Bildschirmfoto(string formatName)
    {
        var format = Enum.Parse<BarcodeFormat>(formatName);
        var img = DecodingImages.Paste(DecodingImages.Busy(1280, 720, seed: 17), DecodingImages.RenderSized(format), 640, 330);
        AssertFound(img, format);
    }

    [Theory]
    [MemberData(nameof(DetectableFormats))]
    public void Invertiert_im_unruhigen_Bildschirmfoto(string formatName)
    {
        var format = Enum.Parse<BarcodeFormat>(formatName);
        var img = DecodingImages.Paste(DecodingImages.Busy(1280, 720, seed: 18), TestCodes.Invert(DecodingImages.RenderSized(format)), 90, 360);
        AssertFound(img, format);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Keine_Phantomcodes_in_unruhigen_Bildern(int seed)
    {
        Assert.Empty(Decoder.Decode(DecodingImages.Busy(1920, 1080, seed: 100 + seed)));
        Assert.Empty(Decoder.Decode(TestCodes.Noise(DecodingImages.Busy(1280, 720, seed: 200 + seed), 60, seed)));
    }

    /// <summary>
    /// Bilder, in denen ZXings gestapelte DataBar-Expanded-Suche Zeilen aus weit entfernten Bildteilen zu einem
    /// „gültigen“ Code zusammengesetzt hat – einmal sogar mit leerem Inhalt.
    /// </summary>
    public static TheoryData<string, PixelImage, string?> FruehereDataBarPhantome() => new()
    {
        { "4K unruhig, Seed 1008", DecodingImages.Busy(3840, 2160, seed: 1008), null },
        { "4K unruhig, nur DataBar Expanded", DecodingImages.Busy(3840, 2160, seed: 6009), "RSS_EXPANDED" },
        { "720p verrauscht, Seed 2009", TestCodes.Noise(DecodingImages.Busy(1280, 720, seed: 2009), 67, seed: 9), null },
        { "720p verrauscht, leerer Text", TestCodes.Noise(DecodingImages.Busy(1280, 720, seed: 2019), 97, seed: 19), null },
        { "720p verrauscht, nur DataBar Expanded", TestCodes.Noise(DecodingImages.Busy(1280, 720, seed: 5016), 66, seed: 16), "RSS_EXPANDED" },
    };

    [Theory]
    [MemberData(nameof(FruehereDataBarPhantome))]
    public void Keine_zusammengestueckelten_DataBar_Codes(string name, PixelImage img, string? format)
    {
        Assert.NotNull(name);
        var decoder = format == null ? Decoder : new BarcodeDecoder([format]);
        Assert.Empty(decoder.Decode(img));
    }
}
