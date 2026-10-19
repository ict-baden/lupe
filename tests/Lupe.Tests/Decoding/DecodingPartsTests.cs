// SPDX-License-Identifier: MIT
using Lupe.Core.Decoding;
using Lupe.Core.Model;
using Lupe.Tests.TestSupport;
using Zx = ZXingCpp.BarcodeFormat;

namespace Lupe.Tests.Decoding;

public sealed class GrayImageTests
{
    private static PixelImage Bgra(int width, int height, params (byte B, byte G, byte R, byte A)[] pixels)
    {
        var px = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i++)
            (px[i * 4], px[i * 4 + 1], px[i * 4 + 2], px[i * 4 + 3]) = pixels[i];
        return new PixelImage(width, height, px);
    }

    [Fact]
    public void Helligkeit_wie_BT601()
    {
        var gray = GrayImage.FromBgra(Bgra(5, 1, (255, 255, 255, 255), (0, 0, 0, 255), (0, 0, 255, 255), (0, 255, 0, 255), (255, 0, 0, 255)));
        Assert.Equal(new byte[] { 255, 0, 76, 149, 28 }, gray.Pixels);
    }

    [Fact]
    public void Transparenz_wird_auf_Weiss_verrechnet()
    {
        // Schwarz deckend, Schwarz ganz durchsichtig, Schwarz halb durchsichtig, Weiß ganz durchsichtig
        var gray = GrayImage.FromBgra(Bgra(4, 1, (0, 0, 0, 255), (0, 0, 0, 0), (0, 0, 0, 128), (255, 255, 255, 0)));
        Assert.Equal(new byte[] { 0, 255, 127, 255 }, gray.Pixels);
    }

    [Fact]
    public void Alpha_ueberall_null_gilt_als_deckend()
    {
        // GDI-Aufnahmen liefern oft Alpha 0 – sonst wäre alles weiß
        var gray = GrayImage.FromBgra(Bgra(2, 1, (0, 0, 0, 0), (255, 255, 255, 0)));
        Assert.Equal(new byte[] { 0, 255 }, gray.Pixels);
    }

    [Fact]
    public void Zeilenabstand_wird_beachtet()
    {
        var img = DecodingImages.WithStride(Bgra(2, 2, (0, 0, 0, 255), (255, 255, 255, 255), (255, 255, 255, 255), (0, 0, 0, 255)), 8);
        Assert.Equal(16, img.Stride);
        Assert.Equal(new byte[] { 0, 255, 255, 0 }, GrayImage.FromBgra(img).Pixels);
    }

    [Fact]
    public void Grosse_Bilder_werden_parallel_gleich_umgerechnet()
    {
        var img = TestCodes.Noise(DecodingImages.Solid(1500, 800, 128), 100, seed: 4);
        var gray = GrayImage.FromBgra(img);
        for (int i = 0; i < gray.Pixels.Length; i += 9973)
        {
            int s = i * 4;
            Assert.Equal((img.Bgra[s] * 7424 + img.Bgra[s + 1] * 38550 + img.Bgra[s + 2] * 19562) >> 16, gray.Pixels[i]);
        }
    }

    private static GrayImage Ramp(int w, int h) => new(w, h, Enumerable.Range(0, w * h).Select(i => (byte)(i % 251)).ToArray());

    [Fact]
    public void Ausschnitt_und_Invertieren()
    {
        var img = Ramp(5, 4);
        var crop = img.Crop(1, 2, 3, 2);
        Assert.Equal(new byte[] { 11, 12, 13, 16, 17, 18 }, crop.Pixels);

        Assert.Equal(new byte[] { 244, 243, 242, 239, 238, 237 }, crop.Invert().Pixels);
        Assert.Throws<ArgumentOutOfRangeException>(() => img.Crop(3, 0, 3, 1));
    }

    [Fact]
    public void Verkleinern_mittelt_Bloecke()
    {
        var img = new GrayImage(4, 2, [0, 10, 100, 101, 20, 30, 102, 103]);
        var half = img.Downscale(2);
        Assert.Equal((2, 1), (half.Width, half.Height));
        Assert.Equal(new byte[] { 15, 102 }, half.Pixels);
        Assert.Same(img, img.Downscale(1));
    }

    [Fact]
    public void Vergroessern_bilinear()
    {
        var img = new GrayImage(2, 1, [0, 200]);
        var up = img.Upscale(2);
        Assert.Equal((4, 2), (up.Width, up.Height));
        // Ränder bleiben, dazwischen wird gemischt (¼ und ¾)
        Assert.Equal(new byte[] { 0, 50, 150, 200, 0, 50, 150, 200 }, up.Pixels);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(-30)]
    [InlineData(17)]
    public void Drehen_mit_passender_Rueckabbildung(double angle)
    {
        // Einzelner schwarzer Punkt: nach dem Drehen muss die Rückabbildung seiner neuen Lage wieder bei ihm landen
        var px = Enumerable.Repeat((byte)255, 60 * 40).ToArray();
        for (int y = 10; y < 14; y++) for (int x = 40; x < 44; x++) px[y * 60 + x] = 0;
        var img = new GrayImage(60, 40, px);

        var rotated = img.Rotate(angle, out var map);

        Assert.Equal(GrayImage.RotationMap(60, 40, angle), map);
        int darkest = Array.IndexOf(rotated.Pixels, rotated.Pixels.Min());
        var (sx, sy) = map.Apply(darkest % rotated.Width + 0.5, darkest / rotated.Width + 0.5);
        Assert.InRange(sx, 39, 45);
        Assert.InRange(sy, 9, 15);
        // Ecken außerhalb des Originals sind weiß
        Assert.Equal(255, rotated.Pixels[0]);
    }

    [Fact]
    public void Drehen_um_90_Grad_ohne_Rand()
    {
        var rotated = Ramp(30, 20).Rotate(90, out _);
        Assert.Equal((20, 30), (rotated.Width, rotated.Height));
    }
}

public sealed class PointMapTests
{
    [Fact]
    public void Verkettung_wendet_erst_innen_dann_aussen_an()
    {
        var inner = PointMap.ScaleOffset(1, -10, -10); // Rand abziehen
        var outer = PointMap.ScaleOffset(2, 100, 50);   // Kachel einer halbierten Übersicht
        var (x, y) = outer.After(inner).Apply(30, 20);
        Assert.Equal((140.0, 70.0), (x, y));
        Assert.Equal((5.0, 6.0), PointMap.Identity.Apply(5, 6));
    }
}

public sealed class TileTests
{
    [Theory]
    [InlineData(3840, 2160)]
    [InlineData(2500, 1100)]
    [InlineData(1024, 1024)]
    [InlineData(800, 3000)]
    public void Kacheln_decken_das_Bild_mit_Ueberlappung_ab(int width, int height)
    {
        var tiles = BarcodeDecoder.Tiles(width, height, 1024, 320).ToList();

        foreach (var (x, y, w, h) in tiles)
        {
            Assert.InRange(x, 0, width - 1);
            Assert.InRange(y, 0, height - 1);
            Assert.True(x + w <= width && y + h <= height);
            Assert.True(w <= 1024 && h <= 1024);
        }
        // Die Kacheln bilden ein Gitter: jede Spalte und jede Zeile des Bildes liegt in einer Kachel
        Assert.All(Enumerable.Range(0, width), px => Assert.Contains(tiles, t => t.X <= px && px < t.X + t.Width));
        Assert.All(Enumerable.Range(0, height), py => Assert.Contains(tiles, t => t.Y <= py && py < t.Y + t.Height));
        Assert.Equal(tiles.Select(t => t.X).Distinct().Count() * tiles.Select(t => t.Y).Distinct().Count(), tiles.Count);

        // Jedes Quadrat bis zur Überlappung liegt ganz in mindestens einer Kachel
        var rnd = new Random(1);
        for (int i = 0; i < 200; i++)
        {
            int s = 320, x = rnd.Next(Math.Max(1, width - s)), y = rnd.Next(Math.Max(1, height - s));
            Assert.Contains(tiles, t => t.X <= x && t.Y <= y && t.X + t.Width >= Math.Min(width, x + s) && t.Y + t.Height >= Math.Min(height, y + s));
        }
    }

    [Fact]
    public void Foto_mit_24_Megapixeln_ergibt_12_Kacheln()
    {
        Assert.Equal(12, BarcodeDecoder.Tiles(6000, 4000, BarcodeDecoder.TileSize, BarcodeDecoder.TileOverlap).Count());
        // 40 Megapixel (die Obergrenze beim Laden) passen ohne Verkleinern in die Höchstzahl der Kacheln
        Assert.InRange(BarcodeDecoder.Tiles(7746, 5164, BarcodeDecoder.TileSize, BarcodeDecoder.TileOverlap).Count(), 1, BarcodeDecoder.MaxTiles);
    }
}

public sealed class FormatCatalogTests
{
    [Fact]
    public void Leer_oder_null_heisst_alle()
    {
        Assert.Equal(FormatCatalog.All, FormatCatalog.Parse(null));
        Assert.Equal(FormatCatalog.All, FormatCatalog.Parse([]));
        Assert.Equal(BarcodeDecoder.AllFormats, new BarcodeDecoder().ActiveFormats);
    }

    [Fact]
    public void Namen_tolerant()
    {
        var decoder = new BarcodeDecoder(["qr_code", " Data-Matrix ", "EAN 13", "", "  "]);
        Assert.Equal(["QR_CODE", "DATA_MATRIX", "EAN_13"], decoder.ActiveFormats);
    }

    [Fact]
    public void Unbekannte_Namen_werden_uebergangen()
    {
        Assert.Equal(["CODE_128"], new BarcodeDecoder(["CODE_128", "GIBT_ES_NICHT", "PLESSEY"]).ActiveFormats);
        // Bleibt nichts Bekanntes übrig, gelten alle – lieber zu viel als stillschweigend gar nichts erkennen
        Assert.Equal(BarcodeDecoder.AllFormats, new BarcodeDecoder(["MSI", "Unsinn"]).ActiveFormats);
        // IMB las nur ZXing.Net – eine alte Einstellung „nur IMB“ heißt jetzt „alle“
        Assert.Equal(BarcodeDecoder.AllFormats, new BarcodeDecoder(["IMB"]).ActiveFormats);
    }

    [Fact]
    public void Alle_Formate_ohne_Phantom_Lieferanten()
    {
        var all = BarcodeDecoder.AllFormats;
        Assert.Equal(22, all.Count);
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.All(all, f => Assert.True(Enum.TryParse<BarcodeFormat>(f, out _)));
        // Die Namen von ZXing.Net bleiben (Einstellungen, Verlauf, CSV, Befehlszeile) …
        foreach (var f in new[] { "QR_CODE", "DATA_MATRIX", "AZTEC", "PDF_417", "MAXICODE", "CODE_128", "CODE_39", "CODE_93", "CODABAR",
                     "ITF", "EAN_13", "EAN_8", "UPC_A", "UPC_E", "RSS_14", "RSS_EXPANDED" })
            Assert.Contains(f, all);
        // … dazu die Formate, die erst zxing-cpp liest
        foreach (var f in new[] { "MICRO_QR_CODE", "RMQR_CODE", "MICRO_PDF_417", "RSS_LIMITED", "TELEPEN", "DX_FILM_EDGE" })
            Assert.Contains(f, all);
        // MSI und Pharmacode lieferten Phantomcodes und kann zxing-cpp nicht lesen, ebenso Plessey und IMB;
        // All_1D und UPC_EAN_EXTENSION sind keine Formate
        foreach (var f in new[] { "MSI", "PHARMA_CODE", "PLESSEY", "IMB", "All_1D", "UPC_EAN_EXTENSION" })
            Assert.DoesNotContain(f, all);
        Assert.Equal("QR_CODE", all[0]);
    }

    [Fact]
    public void Zuordnung_zu_zxing_cpp_ist_eindeutig()
    {
        // Jedes gesuchte zxing-cpp-Format gehört zu genau einem Lupe-Format und wird beim Lesen wieder dorthin zurückgeführt
        foreach (var f in FormatCatalog.All)
        {
            Assert.NotEmpty(f.Read);
            Assert.All(f.Read, z => Assert.Same(f, FormatCatalog.FromResult(z)));
        }
        var read = FormatCatalog.All.SelectMany(f => f.Read.Select(z => (int)z)).ToList();
        Assert.Equal(read.Count, read.Distinct().Count());

        // Was zxing-cpp als Familie oder Spielart meldet
        Assert.Equal("QR_CODE", FormatCatalog.FromResult(Zx.QRCode)?.Name);
        Assert.Equal("MICRO_QR_CODE", FormatCatalog.FromResult(Zx.MicroQRCode)?.Name);
        Assert.Equal("CODE_39", FormatCatalog.FromResult(Zx.Code39)?.Name);
        Assert.Equal("CODE_39", FormatCatalog.FromResult(Zx.PZN)?.Name);
        Assert.Equal("EAN_13", FormatCatalog.FromResult(Zx.ISBN)?.Name);
        Assert.Equal("ITF", FormatCatalog.FromResult(Zx.ITF14)?.Name);
        Assert.Equal("RSS_14", FormatCatalog.FromResult(Zx.DataBar)?.Name);
        Assert.Equal("RSS_EXPANDED", FormatCatalog.FromResult(Zx.DataBarExpStk)?.Name);
        Assert.Equal("TELEPEN", FormatCatalog.FromResult(Zx.TelepenAlpha)?.Name);
        Assert.Null(FormatCatalog.FromResult(Zx.OtherBarcode));
    }

    [Fact]
    public void Strichcode_oder_Flaeche()
    {
        Assert.True(FormatCatalog.Is2D("qr_code"));
        Assert.True(FormatCatalog.Is2D("MICRO_PDF_417"));
        Assert.False(FormatCatalog.Is2D("RSS_LIMITED"));
        Assert.False(FormatCatalog.Is2D("IMB"));
        Assert.All(FormatCatalog.All, f => Assert.Equal(TestCodes.IsLinear(Enum.Parse<BarcodeFormat>(f.Name)), f.Linear));
    }
}

public sealed class ResultHandlingTests
{
    private static readonly CodeFormat[] All = FormatCatalog.All;
    private const char GS = (char)0x1D;

    private static NativeCode Native(string format, string text, Zx native, string? upce = null) =>
        new(FormatCatalog.Find(format)!, native, text, System.Text.Encoding.Latin1.GetBytes(text), upce, []);

    [Fact]
    public void UPC_A_wie_bisher_mit_12_Ziffern()
    {
        // zxing-cpp meldet einen UPC-A als EAN-13 mit führender 0, solange EAN-13 mitgesucht wird
        Assert.Equal(("UPC_A", "036000291452"), BarcodeDecoder.Presentation(Native("EAN_13", "0036000291452", Zx.EAN13), All));
        // Wird UPC-A nicht gesucht, bleibt es EAN-13 – mit 13 Ziffern
        Assert.Equal(("EAN_13", "0036000291452"),
            BarcodeDecoder.Presentation(Native("EAN_13", "0036000291452", Zx.EAN13), FormatCatalog.Parse(["EAN_13"])));
        // Nur UPC-A gesucht: zxing-cpp meldet UPC-A, aber mit 13 Ziffern
        Assert.Equal(("UPC_A", "036000291452"), BarcodeDecoder.Presentation(Native("UPC_A", "0036000291452", Zx.UPCA), FormatCatalog.Parse(["UPC_A"])));
        Assert.Equal(("EAN_13", "4006381333931"), BarcodeDecoder.Presentation(Native("EAN_13", "4006381333931", Zx.EAN13), All));
    }

    [Fact]
    public void Weitere_Darstellungen_wie_bisher()
    {
        Assert.Equal(("UPC_E", "01234565"), BarcodeDecoder.Presentation(Native("UPC_E", "0012345000065", Zx.UPCE, upce: "01234565"), All));
        Assert.Equal(("CODABAR", "40156"), BarcodeDecoder.Presentation(Native("CODABAR", "A40156B", Zx.Codabar), All));
        Assert.Equal(("RSS_14", "09501101530010"), BarcodeDecoder.Presentation(Native("RSS_14", "0109501101530010", Zx.DataBarOmni), All));
        Assert.Equal(("RSS_LIMITED", "09501101530010"), BarcodeDecoder.Presentation(Native("RSS_LIMITED", "0109501101530010", Zx.DataBarLtd), All));
        Assert.Equal(("RSS_EXPANDED", "(01)98898765432106(3202)012345(15)991231"),
            BarcodeDecoder.Presentation(Native("RSS_EXPANDED", "0198898765432106320201234515991231", Zx.DataBarExp), All));
        // GS1-128 bleibt in der Rohform mit Gruppentrennzeichen
        var gs1 = $"010401234500000910ABC123{GS}21XYZ987";
        Assert.Equal(("CODE_128", gs1), BarcodeDecoder.Presentation(Native("CODE_128", gs1, Zx.Code128), All));
    }

    [Fact]
    public void GS1_in_Klammern()
    {
        Assert.Equal("(01)98898765432106(10)AB12(15)991231", Gs1Text.Bracketed($"019889876543210610AB12{GS}15991231"));
        Assert.Equal("(01)09501101530003(3102)000100(21)XYZ", Gs1Text.Bracketed("01095011015300033102000100" + "21XYZ"));
        // Kein gültiger GS1-Inhalt: dann bleibt die Rohform
        Assert.Null(Gs1Text.Bracketed("ABC"));
        Assert.Null(Gs1Text.Bracketed("0112345"));
        Assert.Null(Gs1Text.Bracketed(""));
    }

    [Fact]
    public void Nullbytes_werden_selbst_dekodiert()
    {
        Assert.Equal("A\0Bÿ", NativeReader.DecodeWithNul([0x41, 0x00, 0x42, 0xFF]));
        Assert.Equal("Ä\0", NativeReader.DecodeWithNul([0xC3, 0x84, 0x00]));
    }

    [Fact]
    public void Punkte_Strichcode_Lesezeile_und_2D_im_Uhrzeigersinn()
    {
        // Strichcode: Mitte der ersten und letzten Lesezeile, links zuerst – auch wenn zxing-cpp ihn rückwärts las
        (double, double)[] line = [(300, 10), (20, 10), (20, 50), (300, 50)];
        Assert.Equal([(300.0, 30.0), (20.0, 30.0)], BarcodeDecoder.MarkerPoints(line, linear: true));
        var code = BarcodeDecoder.ToCode(new NativeCode(FormatCatalog.Find("CODE_128")!, Zx.Code128, "x", [], null, line),
            PointMap.Identity, 400, 100, All);
        Assert.Equal([(20f, 30f), (300f, 30f)], code.Points);

        // 2D: vier Ecken im Uhrzeigersinn ab oben links, auch für einen auf dem Kopf liegenden Code
        (double, double)[] upsideDown = [(10, 10), (0, 10), (0, 0), (10, 0)];
        var dm = BarcodeDecoder.ToCode(new NativeCode(FormatCatalog.Find("DATA_MATRIX")!, Zx.DataMatrix, "x", [], null, upsideDown),
            PointMap.Identity, 100, 100, All);
        Assert.Equal([(0f, 0f), (10f, 0f), (10f, 10f), (0f, 10f)], dm.Points);
        Assert.Null(dm.RawBytes);
    }

    /// <summary>Ein Treffer mit achsparallelem Umriss von (x0, y0) bis (x1, y1).</summary>
    private static BarcodeDecoder.Hit Hit(string format, string text, float x0, float y0, float x1, float y1, double? uec = null, int order = 0) =>
        new(new DecodedCode { Format = format, Text = text }, [(x0, y0), (x1, y0), (x1, y1), (x0, y1)], uec, order);

    private static IEnumerable<string> Names(IEnumerable<BarcodeDecoder.Hit> hits) => hits.Select(h => h.Code.Format + ":" + h.Code.Text);

    [Fact]
    public void Bruchstuecke_schwacher_1D_Codes_fallen_weg()
    {
        var hits = new List<BarcodeDecoder.Hit>
        {
            Hit("UPC_A", "036000291452", 10, 30, 200, 70),
            Hit("EAN_8", "60002914", 60, 40, 150, 60),        // Phantom mitten im UPC-A
            Hit("ITF", "15400141288763", 10, 280, 300, 320),
            Hit("ITF", "1540014128", 20, 295, 200, 315),      // Teil-Lesung desselben ITF
            Hit("EAN_8", "96385074", 600, 580, 700, 620),     // echter EAN-8 woanders
            Hit("UPC_E", "01234565", 900, 30, 980, 70),
            Hit("QR_CODE", "x01234565x", 880, 0, 1000, 100), // 2D zählt nicht als „Ganzes“
        };

        BarcodeDecoder.RemoveFragments(hits);

        Assert.Equal(["UPC_A:036000291452", "ITF:15400141288763", "EAN_8:96385074", "UPC_E:01234565", "QR_CODE:x01234565x"], Names(hits));
    }

    [Fact]
    public void Bruchstueck_eines_Bruchstuecks_faellt_auch_weg()
    {
        var hits = new List<BarcodeDecoder.Hit>
        {
            Hit("ITF", "15400141288763", 10, 280, 300, 320),
            Hit("ITF", "1540014128", 20, 295, 200, 315),
            Hit("ITF", "54001412", 30, 290, 150, 310),
            Hit("EAN_8", "96385074", 600, 580, 700, 620),
        };

        BarcodeDecoder.RemoveFragments(hits);

        Assert.Equal(["ITF:15400141288763", "EAN_8:96385074"], Names(hits));
    }

    [Fact]
    public void Phantom_im_Strichcode_faellt_auch_mit_fremdem_Text_weg()
    {
        // Um 40° gedreht gelesen, machte eine Fassung aus einem Teil der Striche eines EAN-13 einen UPC-E mit anderen Ziffern
        var hits = new List<BarcodeDecoder.Hit>
        {
            Hit("EAN_13", "4012345600070", 100, 100, 400, 220),
            Hit("UPC_E", "10123454", 200, 140, 330, 180),
            Hit("EAN_8", "96385074", 100, 260, 300, 330), // knapp darunter, eigener Code
        };

        BarcodeDecoder.RemoveFragments(hits);

        Assert.Equal(["EAN_13:4012345600070", "EAN_8:96385074"], Names(hits));
    }

    [Fact]
    public void Gleicher_Teiltext_an_anderer_Stelle_bleibt()
    {
        var hits = new List<BarcodeDecoder.Hit>
        {
            Hit("CODE_128", "0104006381333931", 0, 0, 300, 20),
            Hit("EAN_8", "40063813", 2000, 1500, 2100, 1540),
        };
        BarcodeDecoder.RemoveFragments(hits);
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void Widerspruch_an_derselben_Stelle_der_sicherere_Treffer_gilt()
    {
        // Ein schräg angeschnittenes PDF417 aus einer gedrehten Fassung: die Fehlerkorrektur ganz verbraucht (0), Text verwürfelt
        var right = Hit("PDF_417", "https://ict-baden.de/lupe?x=1", 80, 50, 320, 110, uec: 1.0, order: 5);
        var scrambled = Hit("PDF_417", "BVO.DEBORiv wl=78, ://0581-ZTFN_V", 80, 52, 321, 109, uec: 0.0, order: 3);
        foreach (var arrival in new[] { new[] { right, scrambled }, [scrambled, right] })
        {
            var hits = new List<BarcodeDecoder.Hit>();
            foreach (var h in arrival) BarcodeDecoder.Add(hits, h);
            Assert.Equal(["PDF_417:https://ict-baden.de/lupe?x=1"], Names(hits));
        }

        // Ohne Angabe zur Fehlerkorrektur (Strichcodes): die vorrangige Aufgabe – zuerst das ganze Bild
        var strichcodes = new List<BarcodeDecoder.Hit>();
        BarcodeDecoder.Add(strichcodes, Hit("EAN_13", "4012345600070", 100, 100, 400, 220, order: 0));
        BarcodeDecoder.Add(strichcodes, Hit("UPC_E", "10123454", 200, 140, 330, 180, order: 4));
        Assert.Equal(["EAN_13:4012345600070"], Names(strichcodes));
    }

    [Fact]
    public void Kein_Widerspruch_innerhalb_einer_Aufgabe_oder_an_anderer_Stelle()
    {
        var hits = new List<BarcodeDecoder.Hit>();
        // QR-Code in einem QR-Code: beide aus derselben Aufgabe – das hat zxing-cpp entschieden
        BarcodeDecoder.Add(hits, Hit("QR_CODE", "außen", 0, 0, 300, 300, uec: 1.0));
        BarcodeDecoder.Add(hits, Hit("QR_CODE", "innen", 120, 120, 180, 180, uec: 0.5));
        // Gleiches Format, aber woanders
        BarcodeDecoder.Add(hits, Hit("QR_CODE", "daneben", 400, 0, 600, 200, uec: 0.0, order: 2));
        // Anderes 2D-Format an derselben Stelle (z. B. Data Matrix in einem Etikett mit QR-Code)
        BarcodeDecoder.Add(hits, Hit("DATA_MATRIX", "DM", 100, 100, 200, 200, uec: 1.0, order: 2));
        // Doppelt: gleiches Format, gleicher Text
        BarcodeDecoder.Add(hits, Hit("QR_CODE", "außen", 1, 1, 301, 301, uec: 1.0, order: 3));

        Assert.Equal(["QR_CODE:außen", "QR_CODE:innen", "QR_CODE:daneben", "DATA_MATRIX:DM"], Names(hits));
    }

    [Fact]
    public void Lesereihenfolge_nach_Umriss()
    {
        // Ein hoher Strichcode und ein flacher 2D-Code nebeneinander in einer Zeile: maßgeblich ist der Umriss des
        // Strichcodes (so weit zxing-cpp Zeilen las), nicht nur seine eine Leselinie in der Mitte
        var hits = new[]
        {
            Hit("CODE_128", "rechts", 520, 480, 880, 600),
            Hit("PDF_417", "links", 40, 545, 400, 615),
            Hit("QR_CODE", "oben", 600, 40, 800, 240),
            new BarcodeDecoder.Hit(new DecodedCode { Format = "MAXICODE", Text = "ohne Umriss" }, [], null, 0),
        };
        Assert.Equal(["QR_CODE:oben", "PDF_417:links", "CODE_128:rechts", "MAXICODE:ohne Umriss"], Names(BarcodeDecoder.InReadingOrder(hits)));
    }
}

public sealed class LineConfirmationTests
{
    /// <summary>Weißes Bild, in dem nur <paramref name="rows"/> Pixelzeilen einen Strichcode tragen.</summary>
    private static GrayImage Strip(int rows)
    {
        var full = TestCodes.Render("EINE ZEILE", BarcodeFormat.CODE_128, 300, 60);
        var px = new byte[300 * 51 * 4];
        Array.Fill(px, (byte)255);
        for (int k = 0; k < rows; k++) Buffer.BlockCopy(full.Bgra, 30 * full.Stride, px, (23 + k) * 300 * 4, 300 * 4);
        return GrayImage.FromBgra(new PixelImage(300, 51, px));
    }

    [Fact]
    public void Ein_einzeiliger_Treffer_reicht_nicht()
    {
        // zxing-cpp nimmt einen Strichcode erst an, wenn er auf zwei Zeilen gleich gelesen wurde – sonst gäbe es Phantome
        // aus schräg angeschnittenen Zeilen (das hatte Lupe für ZXing.Net selbst nachgebaut)
        var settings = new ReadSettings(FormatCatalog.Parse(["CODE_128"]));
        Assert.Empty(NativeReader.Read(Strip(1), settings));
        Assert.Equal("EINE ZEILE", Assert.Single(NativeReader.Read(Strip(2), settings)).Text);
    }

    /// <summary>Ein Code 128, der nur eine einzige Pixelzeile hoch ist, als Bild.</summary>
    internal static PixelImage OneRowBarcode()
    {
        var img = Strip(1);
        var bgra = new byte[img.Width * img.Height * 4];
        for (int i = 0; i < img.Pixels.Length; i++) { bgra[i * 4] = bgra[i * 4 + 1] = bgra[i * 4 + 2] = img.Pixels[i]; bgra[i * 4 + 3] = 255; }
        return new PixelImage(img.Width, img.Height, bgra);
    }

    [Fact]
    public void Der_Decoder_findet_auch_sehr_flache_Strichcodes()
    {
        // Über die vergrößerte Variante wird aus einer Zeile eine bestätigte
        Assert.Equal("EINE ZEILE", Assert.Single(new BarcodeDecoder(["CODE_128"]).Decode(OneRowBarcode())).Text);
    }
}

public sealed class StagePlanTests
{
    private static IReadOnlyList<BarcodeDecoder.Stage> Plan(int width, int height, string[]? formats = null, bool live = false) =>
        BarcodeDecoder.Stages(new GrayImage(width, height, new byte[width * height]), FormatCatalog.Parse(formats), live);

    private static IEnumerable<string> Names(BarcodeDecoder.ScanTask t) => t.Settings.Formats.Select(f => f.Name);

    [Fact]
    public void Full_HD_ganzes_Bild_invertiert_und_schraeg()
    {
        var stages = Plan(1920, 1080);
        var first = Assert.Single(stages);
        Assert.False(first.OnlyIfNothingFound);
        // Das ganze Bild in fünf Lesegruppen, die zusammen jedes Format genau einmal lesen
        var whole = first.Tasks.Where(t => t.Variant.Name == "Original").ToList();
        Assert.Equal(5, whole.Count);
        Assert.Equal(FormatCatalog.All.Select(f => f.Name).Order(), whole.SelectMany(Names).Order());
        Assert.Equal(["QR_CODE", "MICRO_QR_CODE", "RMQR_CODE"], Names(whole[0]));
        Assert.All(whole[4].Settings.Formats, f => Assert.True(f.Linear));
        // Invertiert nur, was zxing-cpp nicht selbst umkehrt: Strichcodes, PDF417 und MaxiCode
        var inverted = first.Tasks.Where(t => t.Variant.Name == "Original invertiert").ToList();
        Assert.Equal(2, inverted.Count);
        Assert.Equal(["PDF_417", "MAXICODE", "MICRO_PDF_417"], Names(inverted[0]));
        Assert.All(inverted[1].Settings.Formats, f => Assert.True(f.Linear));
        Assert.All(inverted, t => Assert.False(t.Settings.TryInvert));
        // Schräglage: ±20/±40 für Strichcodes und PDF417, ±10/±30 nur PDF417
        var skew = first.Tasks.Where(t => t.Variant.Name.StartsWith("Gedreht", StringComparison.Ordinal)).ToList();
        Assert.Equal(8, skew.Count);
        Assert.All(skew, t => Assert.DoesNotContain(t.Settings.Formats, f => f.Name is "QR_CODE" or "DATA_MATRIX"));
        Assert.Equal(["PDF_417"], Names(Assert.Single(skew, t => t.Variant.Name == "Gedreht +10°")));
        Assert.Contains("CODE_128", Names(Assert.Single(skew, t => t.Variant.Name == "Gedreht -40°")));
        Assert.Equal(15, first.Tasks.Count);
    }

    [Fact]
    public void Viertel_K_am_Stueck()
    {
        // 4K liest zxing-cpp je Lesegruppe in höchstens rund 0,3 s – ohne Kacheln, damit kein Code an einer Grenze zerfällt
        var first = Plan(3840, 2160)[0].Tasks;
        Assert.DoesNotContain(first, t => t.Variant.Name.StartsWith("Kachel", StringComparison.Ordinal));
        Assert.Equal("Original", first[0].Variant.Name);
    }

    [Fact]
    public void Grosses_Foto_Uebersicht_und_Kacheln()
    {
        var first = Plan(6000, 4000)[0].Tasks;
        var tiles = first.Where(t => t.Variant.Name.StartsWith("Kachel", StringComparison.Ordinal)).ToList();
        // 12 Kacheln mit je fünf Lesegruppen, ohne zxing-cpps eigene Verkleinerung (große Codes liest die Übersicht)
        Assert.Equal(12, tiles.Select(t => t.Variant).Distinct().Count());
        Assert.Equal(60, tiles.Count);
        Assert.All(tiles, t => Assert.False(t.Settings.TryDownscale));
        Assert.Equal("Übersicht 1:3", first[0].Variant.Name);
        Assert.Contains(first, t => t.Variant.Name == "Übersicht 1:3 invertiert");
    }

    [Fact]
    public void Kleiner_Ausschnitt_wird_auch_vergroessert()
    {
        var stages = Plan(300, 200);
        Assert.Equal(2, stages.Count);
        Assert.True(stages[1].OnlyIfNothingFound);
        Assert.All(stages[1].Tasks, t => Assert.Equal("Vergrößert ×2", t.Variant.Name));
        Assert.Equal(FormatCatalog.All.Select(f => f.Name).Order(), stages[1].Tasks.SelectMany(Names).Order());
    }

    [Fact]
    public void Live_nur_ganzes_Bild_und_invertiert()
    {
        var tasks = Assert.Single(Plan(1280, 720, live: true)).Tasks;
        // Live nur zwei Lesevorgänge fürs ganze Bild: alle 2D-Codes, alle Strichcodes
        Assert.Equal(["Original", "Original", "Original invertiert"], tasks.Select(t => t.Variant.Name));
        Assert.All(tasks[0].Settings.Formats, f => Assert.False(f.Linear));
        Assert.All(tasks[1].Settings.Formats, f => Assert.True(f.Linear));
        Assert.Equal(FormatCatalog.All.Length, tasks[0].Settings.Formats.Count + tasks[1].Settings.Formats.Count);
        // Invertiert live wie bisher nur PDF417 und MaxiCode (QR, Data Matrix und Aztec kehrt zxing-cpp selbst um)
        Assert.Equal(["PDF_417", "MAXICODE", "MICRO_PDF_417"], Names(tasks[2]));
    }

    [Fact]
    public void Formatfilter_bestimmt_die_Aufgaben()
    {
        // QR: ein Lesevorgang – zxing-cpp kehrt QR-Codes selbst um und liest sie in jeder Lage
        var stages = Plan(1280, 720, ["QR_CODE"]);
        Assert.Equal(["Original"], Assert.Single(stages).Tasks.Select(t => t.Variant.Name));
        Assert.All(stages.SelectMany(s => s.Tasks), t => Assert.Equal(["QR_CODE"], Names(t)));
        // Nur Strichcodes: ganzes Bild, invertiert und schräg, aber ohne die Winkel nur für PDF417
        Assert.Equal(6, Plan(1280, 720, ["EAN_13"])[0].Tasks.Count);
    }

    [Fact]
    public void Pixelfaeden_und_Streifen_ohne_unsinnige_Varianten()
    {
        Assert.DoesNotContain(Plan(9000, 1).SelectMany(s => s.Tasks), t => t.Variant.Name.StartsWith("Gedreht", StringComparison.Ordinal));
        Assert.DoesNotContain(Plan(3_000_000, 2).SelectMany(s => s.Tasks), t => t.Variant.Name.StartsWith("Kachel", StringComparison.Ordinal));
        Assert.Equal(1, BarcodeDecoder.FactorFor(100, 1000, 10, 10));
        Assert.Equal(3, BarcodeDecoder.FactorFor(9000, 1000, 100, 90));
        Assert.Equal(2, BarcodeDecoder.FactorFor(1e9, 1000, 2, 500_000)); // nie unter ein Pixel
    }
}
