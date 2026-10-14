using Lupe.Core.Decoding;
using Lupe.Tests.TestSupport;
using Windows.Graphics.Imaging;

namespace Lupe.Tests.Decoding;

/// <summary>
/// Abgeschnittene Dateien: Die Windows-Decoder lesen PNG, JPEG und GIF auch dann ohne Fehler, wenn die Datei mitten im Bild
/// endet (der Rest wird grau oder weiß) – Lupe meldete dann „Kein Code gefunden“ statt „Datei kaputt“.
/// </summary>
public sealed class ImageIntegrityTests : IDisposable
{
    private readonly TestImageFiles _files = new();

    public void Dispose() => _files.Dispose();

    public static TheoryData<string> Kinds => ["png", "jpg", "gif", "bmp", "tif"];

    private static Guid Encoder(string kind) => kind switch
    {
        "png" => BitmapEncoder.PngEncoderId,
        "jpg" => BitmapEncoder.JpegEncoderId,
        "gif" => BitmapEncoder.GifEncoderId,
        "bmp" => BitmapEncoder.BmpEncoderId,
        _ => BitmapEncoder.TiffEncoderId,
    };

    private async Task<byte[]> Complete(string kind, string text = "vollständig")
    {
        var path = await _files.WriteAsync($"ganz-{Guid.NewGuid():N}.{kind}", Encoder(kind), null, TestCodes.Render(text, width: 600, height: 600));
        return await File.ReadAllBytesAsync(path);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Vollstaendige_Datei_gilt_als_vollstaendig(string kind)
    {
        using var stream = new MemoryStream(await Complete(kind));

        Assert.Null(ImageIntegrity.Truncation(stream));
        Assert.Equal(0, stream.Position); // Position bleibt
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Abgeschnittene_Datei_ist_ein_Fehler_mit_deutscher_Meldung(string kind)
    {
        var full = await Complete(kind, "abgeschnitten");
        foreach (var fraction in new[] { 0.98, 0.6, 0.2 })
        {
            var path = _files.PathFor($"halb-{(int)(fraction * 100)}.{kind}");
            await File.WriteAllBytesAsync(path, full[..(int)(full.Length * fraction)]);

            var ex = await Assert.ThrowsAsync<InvalidDataException>(() => ImageFile.LoadAsync(path));

            // PNG, JPEG, GIF: unsere Prüfung; BMP und TIFF lehnt Windows selbst ab
            if (kind is "png" or "jpg" or "gif")
                Assert.Equal($"„{Path.GetFileName(path)}“ ist unvollständig – die Datei endet mitten im Bild (abgebrochener Download oder Kopiervorgang?).", ex.Message);
            else
                Assert.Contains(Path.GetFileName(path), ex.Message);
        }
    }

    [Fact]
    public async Task Daten_hinter_dem_Ende_sind_erlaubt()
    {
        // Manche Kameras und Programme hängen hinter JPEG-Endmarke bzw. IEND noch Daten an
        foreach (var kind in new[] { "png", "jpg", "gif" })
        {
            var path = _files.PathFor($"anhang.{kind}");
            await File.WriteAllBytesAsync(path, [.. await Complete(kind, $"mit Anhang {kind}"), .. "ANHANG-DER-KAMERA"u8, 0xFF, 0xD8]);

            Assert.Equal($"mit Anhang {kind}", Assert.Single(new BarcodeDecoder().Decode(await ImageFile.LoadAsync(path))).Text);
        }
    }

    [Fact]
    public async Task Animiertes_GIF_braucht_nur_das_erste_Bild_vollstaendig()
    {
        var path = await _files.WriteAsync("animiert.gif", BitmapEncoder.GifEncoderId, null,
            TestCodes.Render("erstes Bild", width: 300, height: 300), TestCodes.Render("zweites Bild", width: 300, height: 300));
        var full = await File.ReadAllBytesAsync(path);
        // Das zweite Bild (hinten in der Datei) abschneiden
        await File.WriteAllBytesAsync(path, full[..(full.Length - 200)]);

        Assert.Equal("erstes Bild", Assert.Single(new BarcodeDecoder().Decode(await ImageFile.LoadAsync(path))).Text);
    }

    [Fact]
    public async Task JPEG_mit_Neustartmarken_und_maskierten_FF_ist_vollstaendig()
    {
        // Ein von Hand gebauter Mini-Scan: FF 00 (maskiert) und FF D3 (Neustart) gehören zu den Daten, erst FF D9 beendet
        byte[] jpeg =
        [
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x04, 0x4A, 0x46, // APP0 (verkürzt)
            0xFF, 0xDA, 0x00, 0x04, 0x01, 0x00, // SOS
            0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD3, 0x56, 0xFF, 0xFF, 0x00, 0x78,
            0xFF, 0xD9,
        ];
        using var complete = new MemoryStream(jpeg);
        Assert.Null(ImageIntegrity.Truncation(complete));

        using var cut = new MemoryStream(jpeg[..^2]);
        Assert.NotNull(ImageIntegrity.Truncation(cut));
    }

    [Fact]
    public void Andere_Inhalte_beurteilt_der_Decoder()
    {
        Assert.Null(ImageIntegrity.Truncation(new MemoryStream("kein Bild"u8.ToArray())));
        Assert.Null(ImageIntegrity.Truncation(new MemoryStream([])));
        Assert.Null(ImageIntegrity.Truncation(new MemoryStream([0x89, (byte)'P'])));
    }

    [Fact]
    public async Task Pruefung_beginnt_an_der_aktuellen_Position()
    {
        var png = await Complete("png");
        using var stream = new MemoryStream([.. "VORSPANN"u8, .. png[..(png.Length / 2)]]);
        stream.Position = 8;

        Assert.NotNull(ImageIntegrity.Truncation(stream));
        Assert.Equal(8, stream.Position);
    }
}
