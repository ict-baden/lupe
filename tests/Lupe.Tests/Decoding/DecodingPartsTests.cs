using Lupe.Core.Decoding;
using Lupe.Core.Model;
using Lupe.Tests.TestSupport;
using ZXing;
using ZXing.Common;
using ZXing.QrCode.Internal;

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
    public void Ausschnitt_Rahmen_Invertieren()
    {
        var img = Ramp(5, 4);
        var crop = img.Crop(1, 2, 3, 2);
        Assert.Equal(new byte[] { 11, 12, 13, 16, 17, 18 }, crop.Pixels);

        var padded = crop.Pad(2);
        Assert.Equal((7, 6), (padded.Width, padded.Height));
        Assert.Equal(255, padded.Pixels[0]);
        Assert.Equal(11, padded.Pixels[2 * 7 + 2]);
        Assert.Same(crop, crop.Pad(0));

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
    public void Viertel_K_ergibt_15_Kacheln() => Assert.Equal(15, BarcodeDecoder.Tiles(3840, 2160, 1024, 320).Count());
}

public sealed class GrayLuminanceSourceTests
{
    private static readonly GrayImage Image = new(4, 3, [0, 1, 2, 3, 10, 11, 12, 13, 20, 21, 22, 23]);

    [Fact]
    public void Zeilen_Matrix_und_Ausschnitt_ohne_Kopie()
    {
        var source = new GrayLuminanceSource(Image);
        Assert.Same(Image.Pixels, source.Matrix);
        Assert.Equal(new byte[] { 10, 11, 12, 13 }, source.getRow(1, null!));

        var crop = source.crop(1, 1, 2, 2);
        Assert.Equal((2, 2), (crop.Width, crop.Height));
        Assert.Equal(new byte[] { 21, 22 }, crop.getRow(1, new byte[8])[..2]);
        Assert.Equal(new byte[] { 11, 12, 21, 22 }, crop.Matrix);
        Assert.Equal(new byte[] { 22 }, crop.crop(1, 1, 1, 1).Matrix);
        Assert.Throws<ArgumentException>(() => source.crop(3, 0, 2, 1));
    }

    [Fact]
    public void Drehung_gegen_den_Uhrzeigersinn_wie_ZXing()
    {
        var rotated = new GrayLuminanceSource(Image).crop(1, 0, 3, 2).rotateCounterClockwise();
        // Ausschnitt [1 2 3 / 11 12 13] → gegen den Uhrzeigersinn: [3 13 / 2 12 / 1 11]
        Assert.Equal((2, 3), (rotated.Width, rotated.Height));
        Assert.Equal(new byte[] { 3, 13, 2, 12, 1, 11 }, rotated.Matrix);
        Assert.Equal(new byte[] { 255, 254, 253, 252 }, new GrayLuminanceSource(Image).invert().getRow(0, null!));
    }

    [Fact]
    public void Abbruch_greift_beim_Zeilenzugriff()
    {
        using var cts = new CancellationTokenSource();
        var source = new GrayLuminanceSource(Image, cts.Token);
        source.getRow(0, null!);
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => source.getRow(0, null!));
        Assert.Throws<OperationCanceledException>(() => source.Matrix);
    }
}

public sealed class FormatCatalogTests
{
    [Fact]
    public void Leer_oder_null_heisst_alle()
    {
        Assert.Equal(FormatCatalog.AllMask, FormatCatalog.Parse(null));
        Assert.Equal(FormatCatalog.AllMask, FormatCatalog.Parse([]));
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
    }

    [Fact]
    public void Alle_Formate_ohne_Phantom_Lieferanten()
    {
        var all = BarcodeDecoder.AllFormats;
        Assert.Equal(17, all.Count);
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.All(all, f => Assert.True(Enum.TryParse<BarcodeFormat>(f, out _)));
        foreach (var f in new[] { "QR_CODE", "DATA_MATRIX", "AZTEC", "PDF_417", "MAXICODE", "CODE_128", "CODE_39", "CODE_93", "CODABAR",
                     "ITF", "EAN_13", "EAN_8", "UPC_A", "UPC_E", "RSS_14", "RSS_EXPANDED" })
            Assert.Contains(f, all);
        // MSI und Pharmacode liefern Phantomcodes, Plessey kann ZXing nicht lesen, All_1D/UPC_EAN_EXTENSION sind keine Formate
        foreach (var f in new[] { "MSI", "PHARMA_CODE", "PLESSEY", "All_1D", "UPC_EAN_EXTENSION" })
            Assert.DoesNotContain(f, all);
        Assert.Equal("QR_CODE", all[0]);
    }

    [Fact]
    public void Lesergruppen_decken_genau_alle_Formate_ab()
    {
        var groups = BarcodeDecoder.TwoDGroups.Concat(BarcodeDecoder.OneDGroups).ToList();
        Assert.Equal(FormatCatalog.AllMask, groups.Aggregate((BarcodeFormat)0, (a, g) => a | g));
        for (int i = 0; i < groups.Count; i++)
            for (int j = i + 1; j < groups.Count; j++)
                Assert.Equal((BarcodeFormat)0, groups[i] & groups[j]);
    }
}

public sealed class ResultHandlingTests
{
    [Fact]
    public void PDF417_Punkte_werden_aus_der_gedrehten_Matrix_zurueckgerechnet()
    {
        // Bild 100×60; der Detektor meldet Punkte in der gedrehten Bitmatrix
        static ResultPoint Fix(int rotation, float x, float y)
        {
            var r = new Result("x", null, [new ResultPoint(x, y)], BarcodeFormat.PDF_417);
            r.putMetadata(ResultMetadataType.ORIENTATION, rotation);
            UnrotatedPdf417Reader.FixPoints(r, 100, 60);
            return r.ResultPoints[0];
        }

        Assert.Equal((10f, 20f), (Fix(0, 10, 20).X, Fix(0, 10, 20).Y));
        Assert.Equal((89f, 39f), (Fix(180, 10, 20).X, Fix(180, 10, 20).Y));
        // 270: Matrix ist 60 breit, 100 hoch – (x, y) → (y, H−1−x)
        Assert.Equal((20f, 49f), (Fix(270, 10, 20).X, Fix(270, 10, 20).Y));
        // 90: (x, y) → (W−1−y, x)
        Assert.Equal((79f, 10f), (Fix(90, 10, 20).X, Fix(90, 10, 20).Y));
    }

    [Fact]
    public void QR_Ecken_aus_den_Suchmustern()
    {
        // Suchmuster-Mitten eines achsparallelen QR-Codes mit 4 Pixel je Modul, Code von (100,100) bis (184,184)
        // FinderPattern hat nur einen internen Konstruktor
        static FinderPattern Finder(float x, float y) => (FinderPattern)Activator.CreateInstance(typeof(FinderPattern),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, [x, y, 4f], null)!;
        var bl = Finder(114, 170);
        var tl = Finder(114, 114);
        var tr = Finder(170, 114);
        var r = new Result("x", null, [bl, tl, tr], BarcodeFormat.QR_CODE);

        var corners = BarcodeDecoder.MarkerPoints(r);

        Assert.Equal([(100.0, 100.0), (184.0, 100.0), (184.0, 184.0), (100.0, 184.0)], corners);
    }

    [Fact]
    public void Ohne_Suchmuster_bleiben_die_ZXing_Punkte_im_Uhrzeigersinn()
    {
        var r = new Result("x", null, [new ResultPoint(0, 10), new ResultPoint(0, 0), new ResultPoint(10, 10), new ResultPoint(10, 0)], BarcodeFormat.DATA_MATRIX);
        Assert.Equal([(0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0)], BarcodeDecoder.MarkerPoints(r));

        var line = new Result("x", null, [new ResultPoint(5, 7), new ResultPoint(50, 7)], BarcodeFormat.CODE_128);
        Assert.Equal([(5.0, 7.0), (50.0, 7.0)], BarcodeDecoder.MarkerPoints(line));
    }

    private static DecodedCode Code(string format, string text, params (float X, float Y)[] points) =>
        new() { Format = format, Text = text, Points = points };

    [Fact]
    public void Bruchstuecke_schwacher_1D_Codes_fallen_weg()
    {
        var codes = new List<DecodedCode>
        {
            Code("UPC_A", "036000291452", (10, 50), (200, 52)),
            Code("EAN_8", "60002914", (60, 40), (150, 42)),      // Phantom mitten im UPC-A
            Code("ITF", "15400141288763", (10, 300), (300, 300)),
            Code("ITF", "1540014128", (20, 305), (200, 305)),    // Teil-Lesung desselben ITF
            Code("EAN_8", "96385074", (600, 600), (700, 600)),   // echter EAN-8 woanders
            Code("UPC_E", "01234565", (900, 50), (980, 50)),
            Code("QR_CODE", "x01234565x", (880, 0), (1000, 0), (1000, 100), (880, 100)), // 2D zählt nicht als „Ganzes“
        };

        BarcodeDecoder.RemoveFragments(codes);

        Assert.Equal(["UPC_A:036000291452", "ITF:15400141288763", "EAN_8:96385074", "UPC_E:01234565", "QR_CODE:x01234565x"],
            codes.Select(c => c.Format + ":" + c.Text));
    }

    [Fact]
    public void Bruchstueck_eines_Bruchstuecks_faellt_auch_weg()
    {
        var codes = new List<DecodedCode>
        {
            Code("ITF", "15400141288763", (10, 300), (300, 300)),
            Code("ITF", "1540014128", (20, 305), (200, 305)),
            Code("ITF", "54001412", (30, 302), (150, 302)),
            Code("EAN_8", "96385074", (600, 600), (700, 600)),
        };

        BarcodeDecoder.RemoveFragments(codes);

        Assert.Equal(["ITF:15400141288763", "EAN_8:96385074"], codes.Select(c => c.Format + ":" + c.Text));
    }

    [Fact]
    public void DataBar_Punkte_werden_zu_zwei_aeusseren()
    {
        // DataBar liefert Anfang und Ende des ersten und letzten Suchmusters, alle auf der Lesezeile
        var r = new Result("x", null, [new ResultPoint(332, 33), new ResultPoint(362, 33), new ResultPoint(38, 33), new ResultPoint(68, 33)], BarcodeFormat.RSS_EXPANDED);
        Assert.Equal([(38.0, 33.0), (362.0, 33.0)], BarcodeDecoder.MarkerPoints(r));

        var upright = new Result("x", null, [new ResultPoint(34, 362), new ResultPoint(34, 332), new ResultPoint(34, 68), new ResultPoint(34, 38)], BarcodeFormat.RSS_14);
        Assert.Equal([(34.0, 38.0), (34.0, 362.0)], BarcodeDecoder.MarkerPoints(upright));
    }

    [Fact]
    public void Rueckwaerts_gelesene_Zeile_spiegelt_alle_Punkte()
    {
        var r = new Result("x", null, [new ResultPoint(10, 5), new ResultPoint(40, 5), new ResultPoint(300, 5), null!, new ResultPoint(330, 5)], BarcodeFormat.RSS_EXPANDED);

        OneDFormatsReader.Mirror(r, 400);

        Assert.Equal([389f, 359f, 99f, 69f], r.ResultPoints.Where(p => p != null).Select(p => p.X));
        Assert.Null(r.ResultPoints[3]);
        Assert.Equal(180, r.ResultMetadata[ResultMetadataType.ORIENTATION]);
    }

    [Fact]
    public void Leerer_Inhalt_ist_kein_Treffer()
    {
        Assert.False(DecoderSupport.HasText(null));
        Assert.False(DecoderSupport.HasText(new Result("", null, [], BarcodeFormat.RSS_EXPANDED)));
        Assert.True(DecoderSupport.HasText(new Result("0", null, [], BarcodeFormat.QR_CODE)));
    }

    [Fact]
    public void Gleicher_Teiltext_an_anderer_Stelle_bleibt()
    {
        var codes = new List<DecodedCode>
        {
            Code("CODE_128", "0104006381333931", (0, 0), (300, 0)),
            Code("EAN_8", "40063813", (2000, 1500), (2100, 1500)),
        };
        BarcodeDecoder.RemoveFragments(codes);
        Assert.Equal(2, codes.Count);
    }
}

public sealed class WindowSearchTests
{
    [Fact]
    public void Bitausschnitt_stimmt_mit_Einzelpixeln_ueberein()
    {
        var rnd = new Random(5);
        var m = new BitMatrix(150, 40);
        for (int y = 0; y < 40; y++) for (int x = 0; x < 150; x++) if (rnd.Next(2) == 0) m[x, y] = true;

        foreach (var (left, top, w, h) in new[] { (0, 0, 150, 40), (1, 2, 33, 10), (31, 5, 64, 30), (37, 0, 113, 40), (100, 39, 50, 1) })
        {
            var crop = WindowSearch.CropBits(m, left, top, w, h);
            Assert.Equal((w, h), (crop.Width, crop.Height));
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    Assert.Equal(m[left + x, top + y], crop[x, y]);
        }
    }

    [Fact]
    public void Data_Matrix_im_unruhigen_Bild_ist_ein_Kandidat()
    {
        var code = TestCodes.Render("Kandidat", BarcodeFormat.DATA_MATRIX, 160, 160);
        var img = DecodingImages.Paste(DecodingImages.Busy(1280, 720, seed: 11), code, 700, 380);
        var matrix = new HybridBinarizer(new GrayLuminanceSource(GrayImage.FromBgra(img))).BlackMatrix;

        var windows = WindowSearch.Candidates(matrix);

        Assert.InRange(windows.Count, 1, WindowSearch.MaxWindows);
        Assert.Contains(windows, w => w.X <= 760 && w.Y <= 440 && w.X + w.Width >= 800 && w.Y + w.Height >= 480);
    }

    [Fact]
    public void Leeres_Bild_hat_keine_Kandidaten()
    {
        var matrix = new HybridBinarizer(new GrayLuminanceSource(GrayImage.FromBgra(DecodingImages.Solid(400, 300)))).BlackMatrix;
        Assert.Empty(WindowSearch.Candidates(matrix));
        Assert.Empty(WindowSearch.Candidates(new BitMatrix(10, 10)));
    }
}

public sealed class OneDConfirmationTests
{
    /// <summary>Weißes Bild, in dem nur <paramref name="rows"/> Pixelzeilen einen Strichcode tragen.</summary>
    private static GrayImage Strip(int rows)
    {
        var full = TestCodes.Render("EINE ZEILE", BarcodeFormat.CODE_128, 300, 60);
        var px = new byte[300 * 21 * 4];
        Array.Fill(px, (byte)255);
        for (int k = 0; k < rows; k++) Buffer.BlockCopy(full.Bgra, 30 * full.Stride, px, (8 + k) * 300 * 4, 300 * 4);
        return GrayImage.FromBgra(new PixelImage(300, 21, px)).Pad(15);
    }

    [Fact]
    public void Ein_einzeiliger_Treffer_reicht_nicht()
    {
        Assert.Empty(MultiDecode.Run(Strip(1), BarcodeFormat.CODE_128, CancellationToken.None));
        Assert.Equal("EINE ZEILE", Assert.Single(MultiDecode.Run(Strip(2), BarcodeFormat.CODE_128, CancellationToken.None)).Result.Text);
    }

    [Fact]
    public void Der_Decoder_findet_auch_sehr_flache_Strichcodes()
    {
        // Über die vergrößerte Variante wird aus einer Zeile eine bestätigte
        var img = Strip(1);
        var bgra = new byte[img.Width * img.Height * 4];
        for (int i = 0; i < img.Pixels.Length; i++) { bgra[i * 4] = bgra[i * 4 + 1] = bgra[i * 4 + 2] = img.Pixels[i]; bgra[i * 4 + 3] = 255; }
        Assert.Equal("EINE ZEILE", Assert.Single(new BarcodeDecoder(["CODE_128"]).Decode(new PixelImage(img.Width, img.Height, bgra))).Text);
    }
}

public sealed class StagePlanTests
{
    private static IReadOnlyList<BarcodeDecoder.Stage> Plan(int width, int height, BarcodeFormat mask = 0, bool live = false) =>
        BarcodeDecoder.Stages(new GrayImage(width, height, new byte[width * height]), mask == 0 ? FormatCatalog.AllMask : mask, live);

    [Fact]
    public void Full_HD_ganzes_Bild_dann_Varianten()
    {
        var stages = Plan(1920, 1080);
        Assert.Equal(2, stages.Count);
        // 2 + 6 Lesergruppen normal, 2 Gruppen invertiert (nur 2D)
        Assert.Equal(10, stages[0].Tasks.Count);
        Assert.All(stages[0].Tasks, t => Assert.Equal("Original", t.Variant.Name));
        Assert.False(stages[0].OnlyIfNothingFound);
        Assert.True(stages[1].OnlyIfNothingFound);
        var second = stages[1].Tasks.Select(t => t.Variant.Name).Distinct().ToList();
        Assert.Contains("Verkleinert 1:2", second);
        Assert.Contains("Gedreht +40°", second);
        Assert.DoesNotContain("Vergrößert ×2", second);
    }

    [Fact]
    public void Viertel_K_Uebersicht_und_Kacheln()
    {
        var first = Plan(3840, 2160)[0].Tasks;
        Assert.Equal(10 + 15 * 3, first.Count);
        Assert.Equal(16, first.Select(t => t.Variant).Distinct().Count());
        Assert.StartsWith("Übersicht 1:2", first[0].Variant.Name);
    }

    [Fact]
    public void Kleiner_Ausschnitt_wird_auch_vergroessert()
    {
        var second = Plan(300, 200)[1].Tasks.Select(t => t.Variant.Name).Distinct().ToList();
        Assert.Contains("Vergrößert ×2", second);
        Assert.Contains("Vergrößert ×3", second);
        Assert.Contains("Verkleinert 1:2", second);
    }

    [Fact]
    public void Live_nur_erste_Stufe()
    {
        var stages = Plan(1280, 720, live: true);
        Assert.Equal(10, Assert.Single(stages).Tasks.Count);
    }

    [Fact]
    public void Formatfilter_bestimmt_die_Aufgaben()
    {
        var stages = Plan(1280, 720, BarcodeFormat.QR_CODE);
        // QR: normal und invertiert, keine 1D-Gruppen; in Stufe 2 nichts Gedrehtes (QR ist drehungsunabhängig)
        Assert.Equal(2, stages[0].Tasks.Count);
        Assert.All(stages.SelectMany(s => s.Tasks), t => Assert.Equal(BarcodeFormat.QR_CODE, t.Formats));
        Assert.DoesNotContain(stages[1].Tasks, t => t.Variant.Name.StartsWith("Gedreht", StringComparison.Ordinal));
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
