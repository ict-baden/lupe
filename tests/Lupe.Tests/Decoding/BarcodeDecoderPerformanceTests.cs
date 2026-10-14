using System.Diagnostics;
using Lupe.Core.Decoding;
using Lupe.Core.Model;
using Lupe.Tests.TestSupport;
using Xunit.Abstractions;
using ZXing;

namespace Lupe.Tests.Decoding;

/// <summary>Zeitmessungen laufen allein, damit parallele Tests sie nicht verfälschen.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LeistungCollection
{
    public const string Name = "Leistung";
}

/// <summary>
/// Zeitbudget laut Vorgabe: Full-HD-Bild typischerweise unter 300 ms. Gemessen wird der Median mehrerer Durchläufe
/// nach einem Aufwärmlauf (JIT); die Werte stehen in der Testausgabe.
/// </summary>
[Collection(LeistungCollection.Name)]
public sealed class BarcodeDecoderPerformanceTests(ITestOutputHelper output)
{
    private static readonly BarcodeDecoder Decoder = new();

    private long MedianMs(string name, PixelImage img, int expectedCodes, int runs = 7, Func<PixelImage, IReadOnlyList<DecodedCode>>? decode = null)
    {
        decode ??= i => Decoder.Decode(i);
        Assert.Equal(expectedCodes, decode(img).Count); // Aufwärmen und Ergebnis prüfen
        var times = new List<long>();
        var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
        for (int i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();
            decode(img);
            times.Add(sw.ElapsedMilliseconds);
        }
        var cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalMilliseconds / runs;
        times.Sort();
        long median = times[runs / 2];
        output.WriteLine($"{name} ({img.Width}×{img.Height}): Median {median} ms, min {times[0]} ms, max {times[^1]} ms, Rechenzeit ≈ {cpu:0} ms je Aufruf");
        return median;
    }

    [Fact]
    public void Full_HD_Bildschirmfoto_mit_Code()
    {
        var img = DecodingImages.Paste(DecodingImages.Busy(1920, 1080, seed: 40), TestCodes.Render("https://ict-baden.de", width: 180, height: 180), 1300, 700);
        Assert.InRange(MedianMs("Full HD, unruhig, ein QR-Code", img, 1), 0, 300);
    }

    [Fact]
    public void Full_HD_ohne_Code()
    {
        // Ungünstigster Fall: nichts gefunden, also auch alle Varianten der zweiten Stufe
        Assert.InRange(MedianMs("Full HD, unruhig, kein Code", DecodingImages.Busy(1920, 1080, seed: 41), 0), 0, 300);
    }

    [Fact]
    public void Kamerabild_720p()
    {
        var frame = DecodingImages.Paste(DecodingImages.Busy(1280, 720, seed: 42), TestCodes.Render("Kamera", width: 240, height: 240), 520, 240);
        Assert.InRange(MedianMs("Kamerabild 720p mit QR-Code", frame, 1), 0, 150);
        Assert.InRange(MedianMs("Kamerabild 720p ohne Code", DecodingImages.Busy(1280, 720, seed: 43), 0), 0, 250);
    }

    [Fact]
    public void Kamerastrom_720p_mit_LiveBarcodeDecoder()
    {
        var live = new LiveBarcodeDecoder();
        var frame = DecodingImages.Paste(DecodingImages.Busy(1280, 720, seed: 42), TestCodes.Render("Kamera", width: 240, height: 240), 520, 240);
        Assert.InRange(MedianMs("Live 720p mit QR-Code", frame, 1, decode: i => live.Decode(i)), 0, 100);
        Assert.InRange(MedianMs("Live 720p ohne Code", DecodingImages.Busy(1280, 720, seed: 43), 0, decode: i => live.Decode(i)), 0, 100);
        Assert.InRange(MedianMs("Live 1080p ohne Code", DecodingImages.Busy(1920, 1080, seed: 45), 0, decode: i => live.Decode(i)), 0, 150);
    }

    [Fact]
    public void Viertel_K_Bildschirmfoto_mit_kleinem_Code()
    {
        var img = DecodingImages.Paste(DecodingImages.Busy(3840, 2160, seed: 44), TestCodes.Render("Lupe", width: 60, height: 60), 3400, 1900);
        Assert.InRange(MedianMs("4K, unruhig, 60-px-QR-Code", img, 1, runs: 5), 0, 1000);
    }

    [Fact]
    public void Foto_mit_24_Megapixeln()
    {
        var photo = TestCodes.Noise(TestCodes.Embed(TestCodes.Render("Foto 24 MP", BarcodeFormat.DATA_MATRIX, 600, 600), 6000, 4000, 4100, 2500, background: 150), 12, seed: 46);
        Assert.InRange(MedianMs("Foto 24 MP mit Data Matrix", photo, 1, runs: 3), 0, 1500);
    }

    [Fact]
    public void Bildschirmausschnitt_klein()
    {
        var img = TestCodes.Render("Ausschnitt", BarcodeFormat.DATA_MATRIX, 160, 160);
        Assert.InRange(MedianMs("Ausschnitt 160×160 mit Data Matrix", img, 1), 0, 100);
    }
}
