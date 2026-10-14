using System.Text;
using Lupe.Core.Model;
using Lupe.Tests.Decoding;
using Lupe.Tests.TestSupport;
using Windows.Graphics.Imaging;

namespace Lupe.Tests.App;

/// <summary>
/// Ende zu Ende über die echte Exe: Bilddateien in allen gängigen Formaten, mit den Windows-Encodern geschrieben (wie sie
/// ein Scanner, ein Handy oder Paint ablegt), mit mehreren Codes, ohne Code und kaputt – und die Ausgabe mit Umlauten und
/// Emoji in eine Datei mit Umlauten und Emoji im Namen umgeleitet.
/// </summary>
public sealed class CommandLineEndToEndTests : IDisposable
{
    private readonly TestImageFiles _files = new();

    public void Dispose() => _files.Dispose();

    public static TheoryData<string, string> Encoders => new()
    {
        { "png", "PNG" },
        { "jpg", "JPEG" },
        { "bmp", "BMP" },
        { "gif", "GIF" },
        { "tif", "TIFF" },
    };

    private static Guid EncoderId(string kind) => kind switch
    {
        "PNG" => BitmapEncoder.PngEncoderId,
        "JPEG" => BitmapEncoder.JpegEncoderId,
        "BMP" => BitmapEncoder.BmpEncoderId,
        "GIF" => BitmapEncoder.GifEncoderId,
        "TIFF" => BitmapEncoder.TiffEncoderId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Ein Etikett mit drei Codes nebeneinander: QR, EAN-13 und Data Matrix.</summary>
    private static PixelImage Label(string qr)
    {
        var canvas = TestCodes.Embed(TestCodes.Render(qr, BarcodeFormat.QR_CODE, 280, 280), 1400, 520, 60, 120, background: 255);
        canvas = Overlay(canvas, TestCodes.Render("4012345000009", BarcodeFormat.EAN_13, 400, 170, 10), 420, 175);
        canvas = Overlay(canvas, TestCodes.Render("SN:LUP-000815", BarcodeFormat.DATA_MATRIX, 220, 220, 10), 1000, 150);
        return canvas;
    }

    [AppCoreTheory]
    [MemberData(nameof(Encoders))]
    public async Task Liest_alle_Codes_aus_jedem_Bildformat(string extension, string kind)
    {
        var path = await _files.WriteAsync($"etikett.{extension}", EncoderId(kind), null, Label($"https://example.org/{kind}"));

        var r = AppExe.Run("lesen", path);

        Assert.True(r.ExitCode == 0, $"{kind}: Exitcode {r.ExitCode}, {r.Err}");
        Assert.Equal(3, r.Lines.Length);
        Assert.Contains($"QR_CODE\thttps://example.org/{kind}", r.Lines);
        Assert.Contains("EAN_13\t4012345000009", r.Lines);
        Assert.Contains("DATA_MATRIX\tSN:LUP-000815", r.Lines);
        Assert.Equal("", r.Err);
    }

    [AppCoreFact]
    public async Task Mehrere_Dateien_verschiedener_Formate_in_einem_Aufruf_in_Reihenfolge()
    {
        var paths = new List<string>();
        foreach (var kind in new[] { "TIFF", "PNG", "GIF", "JPEG", "BMP" })
            paths.Add(await _files.WriteAsync($"{kind}.bild", EncoderId(kind), null, TestCodes.Render($"Datei {kind}", width: 360, height: 360)));

        var r = AppExe.Run(["lesen", .. paths]);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal(["QR_CODE\tDatei TIFF", "QR_CODE\tDatei PNG", "QR_CODE\tDatei GIF", "QR_CODE\tDatei JPEG", "QR_CODE\tDatei BMP"], r.Lines);
    }

    [AppCoreFact]
    public async Task Kein_Code_in_allen_Bildern_ergibt_1_und_nennt_jede_Datei()
    {
        var blank = DecodingImages.Solid(640, 480, 230);
        var png = await _files.WriteAsync("leer.png", BitmapEncoder.PngEncoderId, null, blank);
        var jpg = await _files.WriteAsync("leer.jpg", BitmapEncoder.JpegEncoderId, null, blank);

        var r = AppExe.Run("lesen", png, jpg);

        Assert.Equal(1, r.ExitCode);
        Assert.Equal("", r.Out);
        Assert.Contains($"Kein Code gefunden: {png}", r.Err);
        Assert.Contains($"Kein Code gefunden: {jpg}", r.Err);
    }

    [AppCoreFact]
    public async Task Ein_Bild_mit_Code_genuegt_fuer_0()
    {
        var empty = await _files.WriteAsync("leer.png", BitmapEncoder.PngEncoderId, null, DecodingImages.Solid(300, 300, 255));
        var code = await _files.PngAsync("code.png", TestCodes.Render("GEFUNDEN"));

        var r = AppExe.Run("lesen", empty, code);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal(["QR_CODE\tGEFUNDEN"], r.Lines);
        Assert.Contains("Kein Code gefunden", r.Err);
    }

    public static TheoryData<string, string> BrokenFiles => new()
    {
        { "abgeschnitten.png", "halbe PNG-Datei" },
        { "leer.jpg", "leere Datei" },
        { "text.gif", "Text statt Bild" },
        { "zufall.tif", "Zufallsbytes" },
    };

    [AppCoreTheory]
    [MemberData(nameof(BrokenFiles))]
    public async Task Kaputte_Datei_ergibt_3_mit_deutscher_Meldung(string name, string what)
    {
        var path = _files.PathFor(name);
        switch (what)
        {
            case "halbe PNG-Datei":
                var full = await File.ReadAllBytesAsync(await _files.PngAsync("ganz.png", TestCodes.Render("abgeschnitten")));
                await File.WriteAllBytesAsync(path, full[..(full.Length / 2)]);
                break;
            case "leere Datei":
                await File.WriteAllBytesAsync(path, []);
                break;
            case "Text statt Bild":
                await File.WriteAllTextAsync(path, "Das ist kein Bild, nur Text mit Umlauten: äöü");
                break;
            default:
                var bytes = new byte[4096];
                new Random(7).NextBytes(bytes);
                await File.WriteAllBytesAsync(path, bytes);
                break;
        }

        var r = AppExe.Run("lesen", path);

        Assert.True(r.ExitCode == 3, $"{what}: Exitcode {r.ExitCode}, {r.Err}");
        Assert.Equal("", r.Out);
        Assert.Contains(name, r.Err);
        // Nur ASCII prüfen: in die Pipe schreibt Lupe in der Codepage der Konsole (siehe AppExe)
        Assert.Contains("sich nicht lesen", r.Err);
        if (what == "halbe PNG-Datei") Assert.Contains("unvollst", r.Err);
        Assert.DoesNotContain("Exception", r.Err);
    }

    [AppCoreFact]
    public async Task Umleitung_in_Datei_mit_Umlauten_und_Emoji_im_Namen_und_Inhalt()
    {
        const string text = "Grüße aus Baden – 10 € – Maß – 😀";
        // Bild- und Ausgabedatei tragen selbst Umlaute und ein Emoji im Namen
        var image = await _files.WriteAsync("Etikett Größe 😀.png", BitmapEncoder.PngEncoderId, null, DecodingImages.RenderQr(text, "UTF-8"));
        string output = _files.PathFor("Codes Grüße 😀.txt"), errors = _files.PathFor("Fehler Ä.txt");

        int exit = RedirectionTests.RunInCmd($"lesen \"{image}\"", $"> \"{output}\" 2> \"{errors}\"");

        Assert.Equal(0, exit);
        var bytes = await File.ReadAllBytesAsync(output);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var content = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, 3, bytes.Length - 3);
        Assert.Equal($"QR_CODE\t{text}\r\n", content);
        Assert.Equal(0, new FileInfo(errors).Length);
    }

    [AppCoreFact]
    public async Task Fehler_mit_Umlauten_landen_lesbar_in_der_Fehlerdatei()
    {
        var broken = _files.PathFor("Prüfbericht ß.png");
        await File.WriteAllTextAsync(broken, "kaputt");
        string output = _files.PathFor("aus.txt"), errors = _files.PathFor("Fehler Grüße.txt");

        int exit = RedirectionTests.RunInCmd($"lesen \"{broken}\"", $"> \"{output}\" 2> \"{errors}\"");

        Assert.Equal(3, exit);
        Assert.Equal(0, new FileInfo(output).Length);
        var bytes = await File.ReadAllBytesAsync(errors);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var message = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, 3, bytes.Length - 3);
        Assert.Contains("„Prüfbericht ß.png“", message);
    }

    /// <summary>Kopiert ein Bild an eine Stelle eines anderen (gleiches BGRA-Format).</summary>
    private static PixelImage Overlay(PixelImage canvas, PixelImage image, int x, int y)
    {
        var px = (byte[])canvas.Bgra.Clone();
        for (int row = 0; row < image.Height && y + row < canvas.Height; row++)
            Buffer.BlockCopy(image.Bgra, row * image.Stride, px, (y + row) * canvas.Stride + x * 4, Math.Min(image.Width, canvas.Width - x) * 4);
        return new PixelImage(canvas.Width, canvas.Height, px, canvas.Stride);
    }
}

/// <summary>Wie <see cref="AppCoreFactAttribute"/>, für Theorien.</summary>
public sealed class AppCoreTheoryAttribute : TheoryAttribute
{
    public AppCoreTheoryAttribute()
    {
        if (AppExe.Path is null) Skip = "Lupe.exe ist nicht gebaut – vorher dotnet build Lupe.sln.";
        else if (!AppExe.HasCore) Skip = "Die Core-Bausteine sind in diesem Zweig noch Stubs – erst nach dem Zusammenführen prüfbar.";
    }
}
