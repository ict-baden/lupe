using System.Diagnostics;
using System.Text;
using Lupe.Core.Decoding;
using Lupe.Core.Model;
using Lupe.Tests.TestSupport;

namespace Lupe.Tests.Decoding;

/// <summary>
/// Was Lupe mit zxing-cpp neu oder besser kann: mehrere Codes verschiedener Art in einem Bildschirmfoto (auch ein
/// schwerer neben leichten), die neuen Formate, GS1 mit Trennzeichen, ECI-Zeichensätze – und dass im Dauerbetrieb
/// keine nativen Objekte liegen bleiben.
/// </summary>
// Misst den Speicher des ganzen Testprozesses – allein ausführen, sonst zählen parallele Tests mit
[Collection(EchtzeitCollection.Name)]
public sealed class ZxingCppCapabilityTests
{
    private static readonly BarcodeDecoder Decoder = new();
    private const char GS = (char)0x1D;

    private static IEnumerable<string> Found(PixelImage img) => Decoder.Decode(img).Select(c => c.Format + ":" + c.Text);

    [Fact]
    public void Schwere_Codes_neben_leichten_im_unruhigen_Bildschirmfoto()
    {
        // Mit ZXing.Net lief die gründliche Suche nur, wenn gar nichts gefunden wurde – der große QR-Code verdeckte die
        // schweren: einen verkleinerten Code 128 (1,5 Pixel je Modul), einen schrägen EAN-13, einen hellen Code 128 auf
        // dunklem Grund und eine kleine Data Matrix
        var canvas = DecodingImages.Busy(1600, 1000, seed: 77);
        canvas = DecodingImages.Paste(canvas, TestCodes.Render("https://example.org/gross", width: 320, height: 320), 60, 60);
        var small128 = TestCodes.Render("KLEIN-128-0815", BarcodeFormat.CODE_128, 440, 60, margin: 10);
        canvas = DecodingImages.Paste(canvas, DecodingImages.Resize(small128, 330, 45), 520, 120);
        canvas = DecodingImages.Paste(canvas, DecodingImages.Rotate(DecodingImages.RenderSized(BarcodeFormat.EAN_13, scale: 2), 28), 980, 80);
        canvas = DecodingImages.Paste(canvas, TestCodes.Invert(TestCodes.Render("HELL-AUF-DUNKEL", BarcodeFormat.CODE_128, 420, 90, margin: 10)), 520, 560);
        canvas = DecodingImages.Paste(canvas, TestCodes.Render("SN:4711-B", BarcodeFormat.DATA_MATRIX, 64, 64, margin: 2), 1200, 760);

        var found = Found(canvas).ToList();

        Assert.Contains("QR_CODE:https://example.org/gross", found);
        Assert.Contains("CODE_128:KLEIN-128-0815", found);
        Assert.Contains("EAN_13:4006381333931", found);
        Assert.Contains("CODE_128:HELL-AUF-DUNKEL", found);
        Assert.Contains("DATA_MATRIX:SN:4711-B", found);
        Assert.Equal(5, found.Count);
    }

    [Fact]
    public void Neue_Formate_nebeneinander_in_Lesereihenfolge()
    {
        var canvas = DecodingImages.Solid(1500, 760, 255);
        canvas = DecodingImages.Paste(canvas, TestCodes.Render("MQR 4711", BarcodeFormat.MICRO_QR_CODE, 150, 150, margin: 2), 40, 40);
        canvas = DecodingImages.Paste(canvas, TestCodes.Render("rMQR Lupe 42", BarcodeFormat.RMQR_CODE, 420, 120, margin: 2), 260, 60);
        canvas = DecodingImages.Paste(canvas, TestCodes.Render("MicroPDF 4711", BarcodeFormat.MICRO_PDF_417, 420, 120, margin: 2), 760, 60);
        canvas = DecodingImages.Paste(canvas, TestCodes.Render("0950110153001", BarcodeFormat.RSS_LIMITED, 300, 90, margin: 10), 40, 400);
        canvas = DecodingImages.Paste(canvas, TestCodes.Render("Lupe Telepen", BarcodeFormat.TELEPEN, 420, 100, margin: 10), 420, 400);
        canvas = DecodingImages.Paste(canvas, TestCodes.Render("77-2/18A", BarcodeFormat.DX_FILM_EDGE, 260, 70, margin: 10), 920, 410);
        canvas = DecodingImages.Paste(canvas, TestCodes.Render("daneben", width: 200, height: 200), 1250, 380);

        Assert.Equal(
            ["MICRO_QR_CODE:MQR 4711", "RMQR_CODE:rMQR Lupe 42", "MICRO_PDF_417:MicroPDF 4711",
             "RSS_LIMITED:09501101530010", "TELEPEN:Lupe Telepen", "DX_FILM_EDGE:77-2/18A", "QR_CODE:daneben"],
            Found(canvas));
    }

    [Theory]
    [InlineData(BarcodeFormat.CODE_128)]
    [InlineData(BarcodeFormat.DATA_MATRIX)]
    [InlineData(BarcodeFormat.QR_CODE)]
    public void GS1_mit_Trennzeichen_nach_Feldern_variabler_Laenge(BarcodeFormat format)
    {
        // Mit ZXing.Net fehlte bei GS1-128 das Trennzeichen nach der Charge (10) – „…10ABC12321XYZ987“ war nicht mehr zu zerlegen
        var img = TestCodes.Render("(01)04012345000009(10)ABC123(21)XYZ987", format,
            TestCodes.IsLinear(format) ? 700 : 260, TestCodes.IsLinear(format) ? 120 : 260, margin: 10, options: "gs1");

        var code = Assert.Single(Decoder.Decode(img));

        Assert.Equal(format.ToString(), code.Format);
        Assert.Equal($"010401234500000910ABC123{GS}21XYZ987", code.Text);
    }

    [Theory]
    [InlineData("ISO-8859-7", "Καλημέρα από το Μπάντεν")]
    [InlineData("windows-1251", "Привет из Бадена")]
    public void Zeichensatz_nach_ECI_Kennung(string charset, string text)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var img = TestCodes.RenderBytes(Encoding.GetEncoding(charset).GetBytes(text), BarcodeFormat.QR_CODE, $"eci={charset}", 260, 260);
        Assert.Equal(text, Assert.Single(Decoder.Decode(img)).Text);
    }

    [Fact]
    public void Keine_liegengebliebenen_nativen_Objekte_im_Dauerbetrieb()
    {
        // Wie im Kamerastrom: Tausende Bilder hintereinander. Jeder Lesevorgang legt in zxing-cpp Leseoptionen, ein
        // Bild, Trefferlisten und Treffer an; blieben sie liegen, wüchse der Speicher mit jedem Bild.
        var live = new LiveBarcodeDecoder();
        var frame = TestCodes.Embed(TestCodes.Render("Dauerbetrieb", width: 100, height: 100), 220, 160, 60, 30);
        for (int i = 0; i < 300; i++) live.Decode(frame);
        long before = Settled();

        // Zwei Durchgänge: Ein Leck wächst in beiden. Einmaliges Wachstum (glibc legt unter Linux neue Speicherbereiche je
        // Thread an, die es nicht zurückgibt) nur im ersten – gezählt wird der kleinere Wert.
        long growth = long.MaxValue;
        for (int round = 0; round < 2 && growth >= 4 * 1024 * 1024; round++)
        {
            for (int i = 0; i < 3000; i++) Assert.Single(live.Decode(frame));
            long after = Settled();
            growth = Math.Min(growth, after - before);
            before = after;
        }

        // Je Bild legt zxing-cpp Leseoptionen, ein Bild, eine Trefferliste und einen Treffer mit Text und Bytes an; blieben
        // sie liegen, wüchse der Speicher hier um mehrere Megabyte
        Assert.True(growth < 4 * 1024 * 1024, $"Speicher um {growth / 1024} KB gewachsen");

        static long Settled()
        {
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            using var p = Process.GetCurrentProcess();
            return p.PrivateMemorySize64;
        }
    }
}
