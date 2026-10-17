using System.Runtime.InteropServices.WindowsRuntime;
using Lupe.Core.Camera;
using Lupe.Core.Model;
using Lupe.Tests.TestSupport;
using Windows.Graphics.Imaging;
using static Lupe.Tests.Camera.CameraTestSupport;

using Lupe.Platform.Windows.Camera;

namespace Lupe.Tests.Camera;

/// <summary>Umwandlung echter <see cref="SoftwareBitmap"/>s (wie sie der Frame-Reader liefert) in <see cref="PixelImage"/>.</summary>
public sealed class FrameConverterTests
{
    private static SoftwareBitmap ToBitmap(PixelImage image, BitmapAlphaMode alpha = BitmapAlphaMode.Straight)
    {
        var compact = new byte[image.Width * image.Height * 4];
        for (int y = 0; y < image.Height; y++)
            Buffer.BlockCopy(image.Bgra, y * image.Stride, compact, y * image.Width * 4, image.Width * 4);
        return SoftwareBitmap.CreateCopyFromBuffer(compact.AsBuffer(), BitmapPixelFormat.Bgra8, image.Width, image.Height, alpha);
    }

    [Fact]
    public void BGRA8_wird_kopiert_und_Alpha_auf_255_gesetzt()
    {
        // Kameras liefern bei „Alpha ignorieren“ oft Alpha 0
        var px = new byte[5 * 3 * 4];
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i] = (byte)i;
            px[i + 1] = (byte)(i + 1);
            px[i + 2] = (byte)(i + 2);
            px[i + 3] = 0;
        }
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(px.AsBuffer(), BitmapPixelFormat.Bgra8, 5, 3, BitmapAlphaMode.Ignore);

        var image = FrameConverter.ToPixelImage(bitmap);

        Assert.Equal(5, image.Width);
        Assert.Equal(3, image.Height);
        Assert.Equal(20, image.Stride);
        for (int i = 0; i < px.Length; i += 4)
        {
            Assert.Equal(px[i], image.Bgra[i]);
            Assert.Equal(px[i + 1], image.Bgra[i + 1]);
            Assert.Equal(px[i + 2], image.Bgra[i + 2]);
            Assert.Equal(255, image.Bgra[i + 3]);
        }
    }

    [Fact]
    public void Ergebnis_ist_unabhängig_vom_Kamerabild()
    {
        var source = TestCodes.Render("unabhängig", width: 64, height: 64);
        using var bitmap = ToBitmap(source);
        var a = FrameConverter.ToPixelImage(bitmap);
        var b = FrameConverter.ToPixelImage(bitmap);
        Assert.NotSame(a.Bgra, b.Bgra);
        a.Bgra[0] ^= 0xFF;
        Assert.NotEqual(a.Bgra[0], b.Bgra[0]);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 2)]
    [InlineData(301, 7)]
    [InlineData(17, 33)]
    public void Ungerade_Größen(int width, int height)
    {
        var rnd = new Random(width * 1000 + height);
        var px = new byte[width * height * 4];
        rnd.NextBytes(px);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(px.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Straight);

        var image = FrameConverter.ToPixelImage(bitmap);

        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        for (int i = 0; i < px.Length; i += 4)
        {
            Assert.Equal(px.AsSpan(i, 3).ToArray(), image.Bgra.AsSpan(i, 3).ToArray());
            Assert.Equal(255, image.Bgra[i + 3]);
        }
    }

    [Fact]
    public void RGBA8_wird_in_BGRA_umgedreht()
    {
        // Ein rotes, ein grünes, ein blaues Pixel in RGBA-Reihenfolge
        byte[] rgba = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255];
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(rgba.AsBuffer(), BitmapPixelFormat.Rgba8, 3, 1, BitmapAlphaMode.Straight);

        var image = FrameConverter.ToPixelImage(bitmap);

        Assert.Equal([0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255], image.Bgra);
    }

    [Theory]
    [InlineData(BitmapPixelFormat.Nv12)]
    [InlineData(BitmapPixelFormat.Yuy2)]
    [InlineData(BitmapPixelFormat.Gray8)]
    public void Kameraformate_YUV_und_Grau_werden_umgewandelt_und_der_QR_Code_bleibt_lesbar(BitmapPixelFormat format)
    {
        var code = TestCodes.Embed(TestCodes.Render("Lupe Kamera 42", width: 220, height: 220), 640, 480, 200, 120);
        using var bgra = ToBitmap(code);
        using var camera = SoftwareBitmap.Convert(bgra, format);
        Assert.Equal(format, camera.BitmapPixelFormat);

        var image = FrameConverter.ToPixelImage(camera);

        Assert.Equal(640, image.Width);
        Assert.Equal(480, image.Height);
        Assert.True(image.Bgra.Where((_, i) => i % 4 == 3).All(a => a == 255), "Alpha muss überall 255 sein");
        var decoded = Assert.Single(ZxingDecode(image));
        Assert.Equal("Lupe Kamera 42", decoded.Text);
        Assert.Equal("QR_CODE", decoded.Format);
        // Graue Fläche bleibt grau (YUV-Rundweg verfälscht höchstens wenig)
        Assert.InRange(image.Bgra[(10 * 640 + 10) * 4], 190, 210);
    }

    [Fact]
    public void Leeres_Argument_ist_ein_Programmierfehler()
    {
        Assert.Throws<ArgumentNullException>(() => FrameConverter.ToPixelImage(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(1000)]
    public void Alpha_setzen_für_jede_Länge(int pixels)
    {
        var px = new byte[pixels * 4];
        new Random(pixels).NextBytes(px);
        var before = (byte[])px.Clone();

        FrameConverter.MakeOpaque(px);

        for (int i = 0; i < px.Length; i += 4)
        {
            Assert.Equal(before.AsSpan(i, 3).ToArray(), px.AsSpan(i, 3).ToArray());
            Assert.Equal(255, px[i + 3]);
        }
    }
}
