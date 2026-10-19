// SPDX-License-Identifier: MIT
using Lupe.Core.Camera;

namespace Lupe.Tests.Camera;

public sealed class FormatChooserTests
{
    private static VideoFormatCandidate F(int w, int h, double fps, string subtype) => new(w, h, fps, subtype);

    private static VideoFormatCandidate Pick(params VideoFormatCandidate[] formats)
    {
        int i = FormatChooser.Choose(formats);
        Assert.InRange(i, 0, formats.Length - 1);
        return formats[i];
    }

    [Fact]
    public void Keine_Formate_ergibt_minus_eins()
    {
        Assert.Equal(-1, FormatChooser.Choose([]));
        Assert.Equal(-1, FormatChooser.Choose([F(0, 0, 30, "NV12"), F(-1, 480, 30, "NV12")]));
    }

    [Fact]
    public void Typische_Full_HD_Webcam_bekommt_720p_und_kein_4K()
    {
        var chosen = Pick(
            F(3840, 2160, 30, "NV12"),
            F(1920, 1080, 30, "NV12"),
            F(1280, 720, 30, "NV12"),
            F(640, 480, 30, "NV12"),
            F(320, 240, 30, "NV12"));
        Assert.Equal(F(1280, 720, 30, "NV12"), chosen);
    }

    [Fact]
    public void USB2_Webcam_720p_nur_mit_MJPG_flüssig_dann_MJPG_statt_ruckelndem_YUY2()
    {
        var chosen = Pick(
            F(1280, 720, 10, "YUY2"),
            F(640, 480, 30, "YUY2"),
            F(1280, 720, 30, "MJPG"),
            F(1920, 1080, 30, "MJPG"));
        Assert.Equal(F(1280, 720, 30, "MJPG"), chosen);
    }

    [Fact]
    public void Bei_gleicher_Größe_unkomprimiert_vor_MJPG_und_H264()
    {
        Assert.Equal("NV12", Pick(F(1280, 720, 30, "H264"), F(1280, 720, 30, "MJPG"), F(1280, 720, 30, "NV12")).Subtype);
        Assert.Equal("MJPG", Pick(F(1280, 720, 30, "H264"), F(1280, 720, 30, "MJPG")).Subtype);
    }

    [Fact]
    public void Flüssige_Bildrate_schlägt_ruckelnde_aber_60_Bilder_bringen_nichts()
    {
        Assert.Equal(30, Pick(F(1280, 720, 5, "NV12"), F(1280, 720, 30, "NV12")).FramesPerSecond);
        Assert.Equal(30, Pick(F(1280, 720, 60, "NV12"), F(1280, 720, 30, "NV12")).FramesPerSecond);
        Assert.Equal(30, Pick(F(1280, 720, 30, "NV12"), F(1280, 720, 60, "NV12")).FramesPerSecond);
        // 29,97 (NTSC) zählt als volle Rate
        Assert.Equal(0, FormatChooser.Cost(F(1280, 720, 30000.0 / 1001, "NV12")));
    }

    [Fact]
    public void Ohne_720p_lieber_etwas_größer_als_viel_kleiner_aber_nie_4K_wenn_es_anders_geht()
    {
        Assert.Equal(1080, Pick(F(640, 480, 30, "YUY2"), F(1920, 1080, 30, "YUY2")).Height);
        Assert.Equal(480, Pick(F(640, 480, 30, "YUY2"), F(3840, 2160, 30, "YUY2")).Height);
        Assert.Equal(540, Pick(F(960, 540, 30, "NV12"), F(1920, 1080, 30, "NV12")).Height);
    }

    [Fact]
    public void Einziges_Format_wird_genommen_auch_wenn_4K()
    {
        Assert.Equal(0, FormatChooser.Choose([F(3840, 2160, 30, "NV12")]));
    }

    [Fact]
    public void Gleichstand_erstes_gewinnt()
    {
        Assert.Equal(0, FormatChooser.Choose([F(1280, 720, 30, "NV12"), F(1280, 720, 30, "NV12")]));
    }

    [Fact]
    public void Unbekannte_Bildrate_ist_kein_Ausschluss()
    {
        Assert.Equal(F(1280, 720, 0, "NV12"), Pick(F(1280, 720, 0, "NV12"), F(3840, 2160, 30, "NV12")));
        Assert.True(double.IsFinite(FormatChooser.Cost(F(1280, 720, double.NaN, "NV12"))));
    }

    [Theory]
    [InlineData(1280, 720, 1280, 720)]
    [InlineData(640, 480, 640, 480)]
    [InlineData(1920, 1080, 1280, 720)]
    [InlineData(3840, 2160, 1280, 720)]
    [InlineData(1920, 1440, 960, 720)]
    [InlineData(720, 1280, 720, 1280)]
    [InlineData(1080, 1920, 720, 1280)]
    [InlineData(2592, 1944, 960, 720)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(-5, 100, 0, 0)]
    public void Ausgabegröße_höchstens_720p_Seitenverhältnis_bleibt(int w, int h, int expectedW, int expectedH)
    {
        Assert.Equal((expectedW, expectedH), FormatChooser.OutputSize(w, h));
    }

    [Fact]
    public void Ausgabegröße_immer_gerade()
    {
        for (int w = 1281; w < 4000; w += 97)
            for (int h = 721; h < 3000; h += 89)
            {
                var (ow, oh) = FormatChooser.OutputSize(w, h);
                Assert.True(ow % 2 == 0 && oh % 2 == 0, $"{w}×{h} → {ow}×{oh}");
                Assert.True(Math.Max(ow, oh) <= 1280 && Math.Min(ow, oh) <= 720, $"{w}×{h} → {ow}×{oh}");
                Assert.InRange((double)ow / oh, (double)w / h * 0.97, (double)w / h * 1.03);
            }
    }
}
