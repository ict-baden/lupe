using Lupe.Core.Model;
using ZXing;
using ZXing.Common;

namespace Lupe.Tests.TestSupport;

/// <summary>
/// Erzeugt echte Codes als <see cref="PixelImage"/> (mit ZXings eigenem Schreiber), optional verkleinert, gedreht,
/// invertiert, verrauscht oder irgendwo in ein größeres Bild gesetzt. Gemeinsame Grundlage für Erkennung,
/// Kamera-Attrappe, Bildschirm- und App-Tests.
/// </summary>
public static class TestCodes
{
    /// <summary>Ein Code als Bild, Hintergrund weiß, Ruhezone inklusive.</summary>
    public static PixelImage Render(string text, BarcodeFormat format = BarcodeFormat.QR_CODE, int width = 300, int height = 300, int margin = 4)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = format,
            Options = new EncodingOptions { Width = width, Height = height, Margin = margin, PureBarcode = true },
        };
        var data = writer.Write(text);
        // ZXing liefert BGRA; Alpha auf undurchsichtig setzen
        for (int i = 3; i < data.Pixels.Length; i += 4) data.Pixels[i] = 255;
        return new PixelImage(data.Width, data.Height, data.Pixels);
    }

    /// <summary>Setzt ein Bild in eine größere, graue Fläche (z. B. Bildschirm- oder Kamerabild mit Code irgendwo).</summary>
    public static PixelImage Embed(PixelImage code, int canvasWidth, int canvasHeight, int x, int y, byte background = 200)
    {
        var px = new byte[canvasWidth * canvasHeight * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = px[i + 1] = px[i + 2] = background; px[i + 3] = 255; }
        for (int row = 0; row < code.Height; row++)
        {
            int ty = y + row;
            if (ty < 0 || ty >= canvasHeight) continue;
            for (int col = 0; col < code.Width; col++)
            {
                int tx = x + col;
                if (tx < 0 || tx >= canvasWidth) continue;
                Buffer.BlockCopy(code.Bgra, row * code.Stride + col * 4, px, (ty * canvasWidth + tx) * 4, 4);
            }
        }
        return new PixelImage(canvasWidth, canvasHeight, px);
    }

    /// <summary>Hell und dunkel vertauschen (heller Code auf dunklem Grund, wie auf manchen Displays).</summary>
    public static PixelImage Invert(PixelImage img)
    {
        var px = (byte[])img.Bgra.Clone();
        for (int i = 0; i < px.Length; i += 4) { px[i] = (byte)(255 - px[i]); px[i + 1] = (byte)(255 - px[i + 1]); px[i + 2] = (byte)(255 - px[i + 2]); }
        return new PixelImage(img.Width, img.Height, px, img.Stride);
    }

    /// <summary>Um 90°-Schritte drehen.</summary>
    public static PixelImage Rotate90(PixelImage img, int times = 1)
    {
        var cur = img;
        for (int t = 0; t < ((times % 4) + 4) % 4; t++)
        {
            int w = cur.Width, h = cur.Height;
            var px = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    Buffer.BlockCopy(cur.Bgra, y * cur.Stride + x * 4, px, (x * h + (h - 1 - y)) * 4, 4);
            cur = new PixelImage(h, w, px);
        }
        return cur;
    }

    /// <summary>Gleichmäßiges Rauschen ±amount auf jeden Kanal, reproduzierbar über den Seed.</summary>
    public static PixelImage Noise(PixelImage img, int amount, int seed = 1)
    {
        var rnd = new Random(seed);
        var px = (byte[])img.Bgra.Clone();
        for (int i = 0; i < px.Length; i += 4)
        {
            int d = rnd.Next(-amount, amount + 1);
            for (int c = 0; c < 3; c++) px[i + c] = (byte)Math.Clamp(px[i + c] + d, 0, 255);
        }
        return new PixelImage(img.Width, img.Height, px, img.Stride);
    }
}
