using System.Runtime.InteropServices;
using Lupe.Core.Decoding;
using Lupe.Core.Model;
using Lupe.Tests.TestSupport;
using Windows.Graphics.Imaging;
using ZXing;

namespace Lupe.Tests.Decoding;

/// <summary>Echte Bilddateien laden – geschrieben mit den Windows-Encodern, gelesen mit <see cref="ImageFile"/>.</summary>
public sealed class ImageFileTests : IDisposable
{
    private readonly TestImageFiles _files = new();

    public void Dispose() => _files.Dispose();

    private static string DecodeSingle(PixelImage img) => Assert.Single(new BarcodeDecoder().Decode(img)).Text;

    private static (byte B, byte G, byte R, byte A) Pixel(PixelImage img, int x, int y)
    {
        int i = y * img.Stride + x * 4;
        return (img.Bgra[i], img.Bgra[i + 1], img.Bgra[i + 2], img.Bgra[i + 3]);
    }

    /// <summary>Weißes Bild mit schwarzem Block oben links – zeigt, wohin gedreht wurde.</summary>
    private static PixelImage Marker(int width, int height, int block = 20)
    {
        var img = DecodingImages.Solid(width, height, 255);
        for (int y = 0; y < block; y++)
            for (int x = 0; x < block; x++)
            {
                int i = y * img.Stride + x * 4;
                img.Bgra[i] = img.Bgra[i + 1] = img.Bgra[i + 2] = 0;
            }
        return img;
    }

    // ---------- Endungen ----------

    [Fact]
    public void Endungen_enthalten_die_eingebauten_Formate()
    {
        var ext = ImageFile.Extensions;
        foreach (var e in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".jxr" })
            Assert.Contains(e, ext);
        Assert.All(ext, e => Assert.Matches("^\\.[a-z0-9]+$", e));
        Assert.Equal(ext.Count, ext.Distinct().Count());
        Assert.Equal(".png", ext[0]);
        // Rohformate von Kameras und DDS bietet der Dialog nicht an
        Assert.DoesNotContain(".dds", ext);
        Assert.DoesNotContain(".cr2", ext);
    }

    [CodecFact(".webp")]
    public void Endungen_enthalten_WebP_wenn_der_Codec_da_ist() => Assert.Contains(".webp", ImageFile.Extensions);

    // ---------- Formate ----------

    public static TheoryData<string, string> Formate() => new()
    {
        { "code.png", "PNG" }, { "code.jpg", "JPEG" }, { "code.bmp", "BMP" }, { "code.gif", "GIF" }, { "code.tif", "TIFF" }, { "code.jxr", "JPEGXR" },
    };

    private static Guid Encoder(string kind) => kind switch
    {
        "PNG" => BitmapEncoder.PngEncoderId,
        "JPEG" => BitmapEncoder.JpegEncoderId,
        "BMP" => BitmapEncoder.BmpEncoderId,
        "GIF" => BitmapEncoder.GifEncoderId,
        "TIFF" => BitmapEncoder.TiffEncoderId,
        "JPEGXR" => BitmapEncoder.JpegXREncoderId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    [Theory]
    [MemberData(nameof(Formate))]
    public async Task Laedt_jedes_eingebaute_Format_und_der_Code_wird_erkannt(string fileName, string kind)
    {
        var qr = TestCodes.Render("Lupe liest " + kind, width: 240, height: 240);
        var path = await _files.WriteAsync(fileName, Encoder(kind), null, qr);

        var img = await ImageFile.LoadAsync(path);

        Assert.Equal((240, 240), (img.Width, img.Height));
        Assert.Equal(240 * 4, img.Stride);
        Assert.All(Enumerable.Range(0, img.Width * img.Height), i => Assert.Equal(255, img.Bgra[i * 4 + 3]));
        Assert.Equal("Lupe liest " + kind, DecodeSingle(img));
        // Ruhezone bleibt weiß (JPEG darf leicht abweichen)
        Assert.True(Pixel(img, 2, 2).R > 240);
    }

    [Fact]
    public async Task Laedt_aus_einem_Datenstrom()
    {
        var path = await _files.PngAsync("strom.png", TestCodes.Render("aus dem Speicher"));
        using var stream = new MemoryStream(await File.ReadAllBytesAsync(path));

        var img = await ImageFile.LoadAsync(stream, "Zwischenablage", ImageFile.MaxPixels, CancellationToken.None);

        Assert.Equal("aus dem Speicher", DecodeSingle(img));
    }

    // ---------- Transparenz ----------

    [Fact]
    public async Task Transparentes_PNG_mit_schwarzem_Code_wird_auf_Weiss_gelegt()
    {
        var transparent = DecodingImages.TransparentBackground(TestCodes.Render("durchsichtig", width: 200, height: 200));
        Assert.Equal((0, 0, 0, 0), Pixel(transparent, 1, 1)); // so speichern es viele Programme: Schwarz, ganz durchsichtig
        var path = await _files.PngAsync("transparent.png", transparent);

        var img = await ImageFile.LoadAsync(path);

        Assert.Equal((255, 255, 255, 255), Pixel(img, 1, 1));
        Assert.Equal("durchsichtig", DecodeSingle(img));
    }

    [Fact]
    public async Task Halbtransparenz_wird_mit_Weiss_gemischt()
    {
        var img = DecodingImages.Solid(10, 10, 0, alpha: 128);
        var path = await _files.PngAsync("halb.png", img);

        var loaded = await ImageFile.LoadAsync(path);

        var (b, g, r, a) = Pixel(loaded, 5, 5);
        Assert.Equal(255, a);
        Assert.InRange(r, 125, 129);
        Assert.Equal(r, g);
        Assert.Equal(r, b);
    }

    [Fact]
    public void Alpha_ueberall_null_gilt_als_undurchsichtig()
    {
        // Manche 32-Bit-BMPs pflegen den Alphakanal nicht – sonst würde das Bild komplett weiß
        byte[] px = [10, 20, 30, 0, 200, 210, 220, 0];
        ImageFile.FlattenOnWhite(px);
        Assert.Equal(new byte[] { 10, 20, 30, 255, 200, 210, 220, 255 }, px);
    }

    [Fact]
    public void Gemischter_Alpha_wird_verrechnet()
    {
        byte[] px = [0, 0, 0, 0, 0, 0, 0, 255, 100, 100, 100, 51];
        ImageFile.FlattenOnWhite(px);
        Assert.Equal(new byte[] { 255, 255, 255, 255, 0, 0, 0, 255, 224, 224, 224, 255 }, px);
    }

    // ---------- EXIF-Drehung und Größe ----------

    /// <summary>EXIF 6 = im Uhrzeigersinn um 90° drehen, 3 = 180°, 8 = gegen den Uhrzeigersinn; 1 = wie gespeichert.</summary>
    [Theory]
    [InlineData((ushort)1, 200, 100, 0, 0)]
    [InlineData((ushort)6, 100, 200, 90, 0)]
    [InlineData((ushort)3, 200, 100, 190, 90)]
    [InlineData((ushort)8, 100, 200, 0, 190)]
    public async Task Exif_Drehung_wird_angewendet(ushort orientation, int width, int height, int markerX, int markerY)
    {
        var path = await _files.WriteAsync($"exif{orientation}.jpg", BitmapEncoder.JpegEncoderId, orientation, Marker(200, 100));

        var img = await ImageFile.LoadAsync(path);

        Assert.Equal((width, height), (img.Width, img.Height));
        // Der Block (gespeichert oben links) liegt jetzt in der passenden Ecke
        Assert.True(Pixel(img, markerX + 5, markerY + 5).R < 60, "Markierung nicht in der erwarteten Ecke");
        Assert.True(Pixel(img, width - 1 - markerX - 5, height - 1 - markerY - 5).R > 200);
    }

    [Fact]
    public async Task Exif_gedrehtes_Foto_mit_Code_wird_erkannt()
    {
        // Foto „hochkant aufgenommen“: gespeichert liegt der Code quer, EXIF sagt „90° drehen“
        var stored = TestCodes.Rotate90(DecodingImages.RenderSized(BarcodeFormat.CODE_128, "HOCHKANT-42", scale: 2), 3);
        var path = await _files.WriteAsync("hochkant.jpg", BitmapEncoder.JpegEncoderId, 6, stored);

        var img = await ImageFile.LoadAsync(path);

        Assert.Equal((stored.Height, stored.Width), (img.Width, img.Height));
        var code = Assert.Single(new BarcodeDecoder().Decode(img));
        Assert.Equal("HOCHKANT-42", code.Text);
        // Nach dem Drehen liegt der Strichcode waagerecht: beide Punkte auf (fast) gleicher Höhe
        Assert.True(Math.Abs(code.Points[0].Y - code.Points[1].Y) < 3);
    }

    [Fact]
    public async Task Grosse_Bilder_werden_verkleinert()
    {
        var path = await _files.PngAsync("gross.png", Marker(300, 200, block: 30));

        var img = await ImageFile.LoadAsync(path, 6000, CancellationToken.None);

        Assert.True(img.Width * img.Height <= 6000);
        Assert.InRange(img.Width, 90, 95);
        Assert.InRange(img.Height, 60, 64);
        Assert.True(Pixel(img, 3, 3).R < 60);
    }

    [Fact]
    public async Task Verkleinern_und_Exif_Drehung_zusammen()
    {
        var path = await _files.WriteAsync("gross-exif.jpg", BitmapEncoder.JpegEncoderId, 6, Marker(400, 200, block: 40));

        var img = await ImageFile.LoadAsync(path, 20_000, CancellationToken.None);

        Assert.Equal((100, 200), (img.Width, img.Height));
        Assert.True(Pixel(img, 95, 3).R < 60, "Markierung gehört nach oben rechts");
        Assert.True(Pixel(img, 3, 196).R > 200);
    }

    // ---------- Mehrbild-Formate ----------

    [Fact]
    public async Task GIF_liefert_das_erste_Bild()
    {
        var path = await _files.WriteAsync("animiert.gif", BitmapEncoder.GifEncoderId, null,
            TestCodes.Render("erstes Bild", width: 200, height: 200), TestCodes.Render("zweites Bild", width: 200, height: 200));

        Assert.Equal("erstes Bild", DecodeSingle(await ImageFile.LoadAsync(path)));
    }

    [Fact]
    public async Task TIFF_liefert_die_erste_Seite()
    {
        var path = await _files.WriteAsync("seiten.tif", BitmapEncoder.TiffEncoderId, null,
            TestCodes.Render("Seite 1", width: 200, height: 200), TestCodes.Render("Seite 2", width: 260, height: 260));

        var img = await ImageFile.LoadAsync(path);

        Assert.Equal(200, img.Width);
        Assert.Equal("Seite 1", DecodeSingle(img));
    }

    [Fact]
    public async Task ICO_liefert_das_groesste_Bild()
    {
        var path = await _files.IcoAsync("symbol.ico", Marker(16, 16, 4), TestCodes.Render("im Symbol", width: 128, height: 128), Marker(32, 32, 8));

        var img = await ImageFile.LoadAsync(path);

        Assert.Equal((128, 128), (img.Width, img.Height));
        Assert.Equal("im Symbol", DecodeSingle(img));
    }

    // ---------- Optionale Codecs ----------

    /// <summary>Verlustfreies WebP mit dem QR-Code „Lupe WebP“ (50×50, mit Pillow erzeugt) – Windows hat keinen WebP-Encoder.</summary>
    private const string WebpQr =
        "UklGRoIAAABXRUJQVlA4THYAAAAvMUAMAA8w//M///MfeGCbUIgd4VrBVTLAJVn91wquErA9eJWSESL6HwAwfpr4JVcN1reiuTTV2+g9W+a1opq4y1WBF6p9KuYVsWtcTczMt5qMHNmk6vlWiNSoJlNeajCxr7mmb6vzlGx9rgadZ1QTn7EqAPwF";

    [CodecFact(".webp", sampleBase64: WebpQr)]
    public async Task WebP_wird_gelesen()
    {
        var path = _files.PathFor("code.webp");
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(WebpQr));

        var img = await ImageFile.LoadAsync(path);

        Assert.Equal((50, 50), (img.Width, img.Height));
        Assert.Equal("Lupe WebP", DecodeSingle(img));
    }

    [CodecFact(".heic", needsEncoder: true)]
    public async Task HEIC_wird_gelesen()
    {
        var path = await _files.WriteAsync("foto.heic", BitmapEncoder.HeifEncoderId, null, TestCodes.Render("aus dem iPhone", width: 300, height: 300));

        Assert.Equal("aus dem iPhone", DecodeSingle(await ImageFile.LoadAsync(path)));
    }

    // ---------- Fehler ----------

    [Fact]
    public async Task Fehlende_Datei_meldet_deutsch()
    {
        var path = _files.PathFor("gibt-es-nicht.png");

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => ImageFile.LoadAsync(path));

        Assert.Equal("Die Datei „gibt-es-nicht.png“ wurde nicht gefunden.", ex.Message);
        Assert.Equal(path, ex.FileName);
    }

    [Fact]
    public async Task Fehlender_Ordner_meldet_fehlende_Datei()
    {
        var path = Path.Combine(_files.Folder, "kein-ordner", "bild.png");
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => ImageFile.LoadAsync(path));
        Assert.Contains("„bild.png“ wurde nicht gefunden", ex.Message);
    }

    [Fact]
    public async Task Leere_Datei_meldet_deutsch()
    {
        var path = _files.PathFor("leer.png");
        await File.WriteAllBytesAsync(path, []);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => ImageFile.LoadAsync(path));

        Assert.Equal("Die Datei „leer.png“ ist leer.", ex.Message);
    }

    [Fact]
    public async Task Textdatei_ist_kein_Bild()
    {
        var path = _files.PathFor("notiz.png");
        await File.WriteAllTextAsync(path, "Das ist kein Bild, nur Text.");

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => ImageFile.LoadAsync(path));

        Assert.StartsWith("„notiz.png“ ist kein unterstütztes Bildformat", ex.Message);
    }

    [Theory]
    [InlineData(20)] // mitten im Dateikopf
    [InlineData(40)] // Kopf vollständig, aber keine Bilddaten
    public async Task Abgeschnittenes_PNG_ist_beschaedigt(int length)
    {
        // (Ein erst in den Bilddaten abgeschnittenes PNG lädt Windows nachsichtig – der Rest bleibt dann leer.)
        var full = await File.ReadAllBytesAsync(await _files.PngAsync("ganz.png", TestCodes.Render("abgeschnitten", width: 300, height: 300)));
        var path = _files.PathFor("kaputt.png");
        await File.WriteAllBytesAsync(path, full[..length]);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => ImageFile.LoadAsync(path));

        Assert.Contains("„kaputt.png“", ex.Message);
        Assert.Matches("beschädigt|kein unterstütztes Bildformat|nicht als Bild gelesen", ex.Message);
    }

    [Fact]
    public async Task Zufallsdaten_sind_kein_Bild()
    {
        var bytes = new byte[5000];
        new Random(3).NextBytes(bytes);
        var path = _files.PathFor("rauschen.jpg");
        await File.WriteAllBytesAsync(path, bytes);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => ImageFile.LoadAsync(path));

        Assert.Contains("„rauschen.jpg“", ex.Message);
    }

    [Fact]
    public async Task Ordner_statt_Datei_meldet_fehlenden_Zugriff()
    {
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ImageFile.LoadAsync(_files.Folder));
        Assert.StartsWith("Kein Zugriff auf", ex.Message);
    }

    [Fact]
    public async Task Exklusiv_geoeffnete_Datei_meldet_deutsch()
    {
        var path = await _files.PngAsync("gesperrt.png", TestCodes.Render("gesperrt"));
        // Ein anderes Programm schreibt gerade und lässt niemanden mitlesen
        await using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var ex = await Assert.ThrowsAsync<IOException>(() => ImageFile.LoadAsync(path));

        Assert.Equal("„gesperrt.png“ wird gerade von einem anderen Programm benutzt und kann nicht gelesen werden.", ex.Message);
    }

    [Fact]
    public async Task Pipe_ist_keine_Bilddatei()
    {
        var name = "lupe-test-" + Guid.NewGuid().ToString("N")[..8];
        await using var server = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.Out, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => ImageFile.LoadAsync(@"\\.\pipe\" + name));

        Assert.Equal($"„{name}“ ist keine Bilddatei.", ex.Message);
    }

    [Fact]
    public async Task Leerer_Pfad_ist_ein_Argumentfehler()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ImageFile.LoadAsync(" "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ImageFile.LoadAsync(null!));
    }

    [Fact]
    public async Task Abbruch_vor_dem_Laden()
    {
        var path = await _files.PngAsync("abbruch.png", TestCodes.Render("x"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImageFile.LoadAsync(path, cts.Token));
    }

    [Fact]
    public async Task Datei_ist_danach_nicht_gesperrt_und_darf_offen_sein()
    {
        var path = await _files.PngAsync("offen.png", TestCodes.Render("offen"));
        // Ein anderes Programm hat die Datei gerade offen (z. B. Bildbetrachter)
        await using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            Assert.Equal("offen", DecodeSingle(await ImageFile.LoadAsync(path)));

        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData(unchecked((int)0x88982F50), "bild.heic", "fehlt Windows der passende Codec")]
    [InlineData(unchecked((int)0x88982F50), "bild.webp", "Webp-Bilderweiterungen")]
    [InlineData(unchecked((int)0x88982F50), "bild.xyz", "ist kein unterstütztes Bildformat")]
    [InlineData(unchecked((int)0x88982F07), "bild.png", "ist kein unterstütztes Bildformat")]
    [InlineData(unchecked((int)0x88982F60), "bild.png", "ist beschädigt oder kein gültiges Bild")]
    [InlineData(unchecked((int)0x88982F61), "bild.png", "ist beschädigt oder kein gültiges Bild")]
    [InlineData(unchecked((int)0x8007000E), "bild.png", "ist zu groß")]
    public void Fehlercodes_werden_uebersetzt(int hresult, string name, string expected)
    {
        var ex = ImageFile.Translate(new COMException("intern", hresult), name);

        Assert.IsType<InvalidDataException>(ex);
        Assert.Contains($"„{name}“", ex.Message);
        Assert.Contains(expected, ex.Message);
        Assert.Equal(hresult, ex.InnerException!.HResult);
    }

    // ---------- Zusammenspiel ----------

    [Fact]
    public async Task Bildschirmfoto_als_Datei_mit_mehreren_Codes()
    {
        var canvas = DecodingImages.Busy(1600, 900);
        canvas = DecodingImages.Paste(canvas, TestCodes.Render("Ticket 4711", BarcodeFormat.AZTEC, 220, 220), 100, 120);
        canvas = DecodingImages.Paste(canvas, TestCodes.Render("https://ict-baden.de", width: 200, height: 200), 900, 500);
        canvas = DecodingImages.Paste(canvas, DecodingImages.RenderSized(BarcodeFormat.EAN_13, scale: 2), 700, 80);
        var path = await _files.PngAsync("bildschirm.png", canvas);

        var codes = new BarcodeDecoder().Decode(await ImageFile.LoadAsync(path));

        Assert.Equal(["AZTEC:Ticket 4711", "EAN_13:4006381333931", "QR_CODE:https://ict-baden.de"],
            codes.Select(c => c.Format + ":" + c.Text).Order(StringComparer.Ordinal));
    }
}
