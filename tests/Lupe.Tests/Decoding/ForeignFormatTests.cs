// SPDX-License-Identifier: MIT
using Lupe.Core.Decoding;
using Lupe.Core.Model;
using Lupe.Tests.TestSupport;

namespace Lupe.Tests.Decoding;

/// <summary>
/// DataBar (RSS_14, RSS_EXPANDED, auch gestapelt) und MaxiCode aus fertigen PNG-Dateien (<see cref="ForeignCodeImages"/>).
/// </summary>
public sealed class ForeignFormatTests
{
    private static readonly BarcodeDecoder Decoder = new();

    public static TheoryData<string, string, string> Readable() => new()
    {
        { nameof(ForeignCodeImages.Rss14), "RSS_14", ForeignCodeImages.Gtin },
        { nameof(ForeignCodeImages.Rss14Gestapelt), "RSS_14", ForeignCodeImages.Gtin },
        { nameof(ForeignCodeImages.RssExpanded), "RSS_EXPANDED", ForeignCodeImages.Gs1 },
        { nameof(ForeignCodeImages.MaxiCode), "MAXICODE", ForeignCodeImages.MaxiText },
    };

    [Theory]
    [MemberData(nameof(Readable))]
    public async Task Wird_gelesen_auch_invertiert_und_verrauscht(string image, string format, string text)
    {
        var img = await ForeignCodeImages.LoadAsync(image);

        var code = Assert.Single(Decoder.Decode(img));

        Assert.Equal((format, text), (code.Format, code.Text));
        Assert.Equal(text, Assert.Single(Decoder.Decode(TestCodes.Invert(img))).Text);
        Assert.Equal(text, Assert.Single(Decoder.Decode(TestCodes.Noise(img, 50, seed: 3))).Text);
        // Formatfilter in beide Richtungen
        Assert.Single(new BarcodeDecoder([format]).Decode(img));
        Assert.Empty(new BarcodeDecoder(["QR_CODE", "CODE_128"]).Decode(img));
    }

    [Theory]
    [InlineData(nameof(ForeignCodeImages.Rss14), "RSS_14", ForeignCodeImages.Gtin, true)]
    // Gestapelt auf ruhigem Grund (so war der Test schon für ZXing.Net angelegt, das gestapelt um 180° gedreht in manchen
    // unruhigen Bildern scheiterte)
    [InlineData(nameof(ForeignCodeImages.Rss14Gestapelt), "RSS_14", ForeignCodeImages.Gtin, false)]
    [InlineData(nameof(ForeignCodeImages.RssExpanded), "RSS_EXPANDED", ForeignCodeImages.Gs1, true)]
    public async Task DataBar_gedreht_mit_zwei_Punkten_auf_dem_Code(string image, string format, string text, bool busy)
    {
        var code0 = await ForeignCodeImages.LoadAsync(image);
        // zxing-cpp lässt um DataBar kaum Ruhezone; ein Etikett auf dem Bildschirm hat einen hellen Rand
        var img = TestCodes.Embed(code0, code0.Width + 24, code0.Height + 24, 12, 12, background: 255);

        for (int turns = 0; turns < 4; turns++)
        {
            var part = TestCodes.Rotate90(img, turns);
            var canvas = busy
                ? DecodingImages.Paste(DecodingImages.Busy(1280, 720, seed: 70 + turns), part, 300, 200)
                : TestCodes.Embed(part, 1280, 720, 300, 200);

            var code = Assert.Single(Decoder.Decode(canvas));

            Assert.Equal((format, text), (code.Format, code.Text));
            // Strichcodes: zwei Punkte, beide auf dem Code
            Assert.Equal(2, code.Points.Count);
            Assert.All(code.Points, p =>
            {
                Assert.InRange(p.X, 300, 300 + part.Width);
                Assert.InRange(p.Y, 200, 200 + part.Height);
            });
        }
    }

    [Fact]
    public async Task Gestapeltes_DataBar_Expanded_wird_gelesen()
    {
        // Mit ZXing.Net las Lupe DataBar Expanded bewusst nur einzeilig: dessen gestapelte Suche setzte Zeilen aus dem ganzen
        // Bild zusammen und erfand dabei Codes (schon das invertierte Bild dieses Codes ergab ein zweites, falsches
        // RSS_EXPANDED). zxing-cpp liest die gestapelte Form ohne diese Phantome – die Phantom-Tests
        // (BarcodeDecoderRobustnessTests.Keine_zusammengestueckelten_DataBar_Codes) laufen mit.
        var img = await ForeignCodeImages.LoadAsync(nameof(ForeignCodeImages.RssExpandedGestapelt));
        var code = Assert.Single(Decoder.Decode(img));
        Assert.Equal(("RSS_EXPANDED", ForeignCodeImages.Gs1), (code.Format, code.Text));
        Assert.Equal(ForeignCodeImages.Gs1, Assert.Single(Decoder.Decode(TestCodes.Invert(img))).Text);
    }

    [Fact]
    public async Task MaxiCode_mitten_im_Bild()
    {
        var img = await ForeignCodeImages.LoadAsync(nameof(ForeignCodeImages.MaxiCode));
        var framed = TestCodes.Embed(img, img.Width + 400, img.Height + 300, 200, 150, background: 255);

        var code = Assert.Single(Decoder.Decode(framed));

        Assert.Equal(ForeignCodeImages.MaxiText, code.Text);
        // ZXing.Net lieferte für MaxiCode keine Punkte, zxing-cpp die vier Ecken
        Assert.Equal(4, code.Points.Count);
        Assert.All(code.Points, p =>
        {
            Assert.InRange(p.X, 190, 210 + img.Width);
            Assert.InRange(p.Y, 140, 160 + img.Height);
        });
    }

    [Fact]
    public async Task Mehrere_Formate_mit_DataBar_in_Lesereihenfolge()
    {
        var canvas = DecodingImages.Solid(1200, 700, 255);
        canvas = DecodingImages.Paste(canvas, await ForeignCodeImages.LoadAsync(nameof(ForeignCodeImages.RssExpanded)), 40, 60);
        canvas = DecodingImages.Paste(canvas, TestCodes.Render("daneben", width: 200, height: 200), 700, 40);
        canvas = DecodingImages.Paste(canvas, await ForeignCodeImages.LoadAsync(nameof(ForeignCodeImages.Rss14)), 40, 400);

        Assert.Equal(["RSS_EXPANDED", "QR_CODE", "RSS_14"], Decoder.Decode(canvas).Select(c => c.Format));
    }
}
