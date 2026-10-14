using System.Text;
using Lupe.Core.Model;
using Lupe.Tests.TestSupport;

namespace Lupe.Tests.Decoding;

/// <summary>
/// Zusätzliche Bildwerkzeuge für die Erkennungstests (ergänzt <see cref="TestSupport.TestCodes"/>): beliebige Drehung,
/// Skalierung wie im Browser, unruhiger Hintergrund, Transparenz, QR mit UTF-8.
/// </summary>
internal static class DecodingImages
{
    /// <summary>Beispieltexte, die das jeweilige Format kodieren kann (Prüfziffern schon enthalten).</summary>
    public static string SampleText(BarcodeFormat format) => format switch
    {
        BarcodeFormat.EAN_13 => "4006381333931",
        BarcodeFormat.EAN_8 => "96385074",
        BarcodeFormat.UPC_A => "036000291452",
        BarcodeFormat.UPC_E => "01234565",
        BarcodeFormat.ITF => "15400141288763",
        BarcodeFormat.CODABAR => "A40156B",
        BarcodeFormat.CODE_39 => "LUPE-123",
        BarcodeFormat.CODE_93 => "LUPE93",
        BarcodeFormat.CODE_128 => "Lupe 128/ok",
        // GTIN ohne Prüfziffer – zxing-cpp ergänzt sie, gelesen wird 09501101530010
        BarcodeFormat.RSS_14 or BarcodeFormat.RSS_LIMITED => "0950110153001",
        BarcodeFormat.RSS_EXPANDED => "(01)98898765432106(3202)012345(15)991231",
        BarcodeFormat.TELEPEN => "Lupe Telepen",
        BarcodeFormat.DX_FILM_EDGE => "77-2/18A",
        BarcodeFormat.MAXICODE => "Lupe MaxiCode 4711",
        BarcodeFormat.MICRO_QR_CODE => "LUPE 42",
        BarcodeFormat.MICRO_PDF_417 => "MicroPDF 4711",
        _ => "https://ict-baden.de/lupe?x=1",
    };

    public static bool IsOneD(BarcodeFormat f) => TestCodes.IsLinear(f);

    /// <summary>Code in passender Größe: 2D quadratisch, 1D breit und flach.</summary>
    public static PixelImage RenderSized(BarcodeFormat format, string? text = null, int scale = 1)
    {
        text ??= SampleText(format);
        var (w, h) = format switch
        {
            BarcodeFormat.PDF_417 or BarcodeFormat.MICRO_PDF_417 or BarcodeFormat.RMQR_CODE => (400, 160),
            _ when IsOneD(format) => (360, 120),
            _ => (240, 240),
        };
        return TestSupport.TestCodes.Render(text, format, w * scale, h * scale, margin: 10);
    }

    /// <summary>QR mit ausdrücklicher Zeichenkodierung (z. B. UTF-8 mit ECI-Kennung oder ohne) – TestCodes.Render nimmt die Voreinstellung.</summary>
    public static PixelImage RenderQr(string text, string characterSet, bool disableEci = false, int size = 300) =>
        TestCodes.RenderBytes(Encoding.GetEncoding(characterSet).GetBytes(text), BarcodeFormat.QR_CODE,
            disableEci ? "ecLevel=L,eci=0" : $"ecLevel=L,eci={characterSet}", size, size);

    /// <summary>Gleichmäßig gefülltes Bild.</summary>
    public static PixelImage Solid(int width, int height, byte gray = 200, byte alpha = 255)
    {
        var px = new byte[width * height * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = px[i + 1] = px[i + 2] = gray; px[i + 3] = alpha; }
        return new PixelImage(width, height, px);
    }

    /// <summary>
    /// Unruhiger Hintergrund wie ein Bildschirmfoto: Flächen, Rahmen, kleine „Buchstaben“ in Zeilen, Symbole –
    /// reproduzierbar über den Seed.
    /// </summary>
    public static PixelImage Busy(int width, int height, int seed = 7)
    {
        var rnd = new Random(seed);
        var px = new byte[width * height * 4];
        void Rect(int x, int y, int w, int h, byte b, byte g, byte r)
        {
            for (int yy = Math.Max(0, y); yy < Math.Min(height, y + h); yy++)
                for (int xx = Math.Max(0, x); xx < Math.Min(width, x + w); xx++)
                {
                    int i = (yy * width + xx) * 4;
                    px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255;
                }
        }
        Rect(0, 0, width, height, 245, 245, 245);
        // Fensterflächen
        for (int i = 0; i < 12; i++)
        {
            int x = rnd.Next(width), y = rnd.Next(height), w = rnd.Next(200, 900), h = rnd.Next(150, 600);
            byte c = (byte)rnd.Next(180, 256);
            Rect(x, y, w, h, c, (byte)rnd.Next(180, 256), (byte)rnd.Next(180, 256));
            Rect(x, y, w, 2, 90, 90, 90);
            Rect(x, y + h - 2, w, 2, 90, 90, 90);
        }
        // Textzeilen aus kleinen Glyphen
        for (int line = 0; line < height / 18; line++)
        {
            int y = line * 18 + 4, x = rnd.Next(0, 40);
            while (x < width - 10)
            {
                if (rnd.NextDouble() < 0.15) { x += rnd.Next(8, 30); continue; }
                int gw = rnd.Next(4, 9), gh = rnd.Next(7, 11);
                for (int k = 0; k < 4; k++)
                {
                    int sx = x + rnd.Next(gw), sy = y + rnd.Next(gh);
                    bool horizontal = rnd.Next(2) == 0;
                    Rect(sx, sy, horizontal ? rnd.Next(2, gw + 1) : 1, horizontal ? 1 : rnd.Next(2, gh + 1), 30, 30, 30);
                }
                x += gw + 2;
            }
        }
        // Symbole: verschachtelte Quadrate (fiese Suchmuster-Kandidaten)
        for (int i = 0; i < 40; i++)
        {
            int x = rnd.Next(width - 40), y = rnd.Next(height - 40), s = rnd.Next(12, 40);
            Rect(x, y, s, s, 20, 20, 20);
            Rect(x + s / 7, y + s / 7, s - 2 * (s / 7), s - 2 * (s / 7), 250, 250, 250);
            Rect(x + 2 * (s / 7), y + 2 * (s / 7), s - 4 * (s / 7), s - 4 * (s / 7), 20, 20, 20);
        }
        return new PixelImage(width, height, px);
    }

    /// <summary>Setzt <paramref name="part"/> an (x, y) in eine Kopie von <paramref name="canvas"/>.</summary>
    public static PixelImage Paste(PixelImage canvas, PixelImage part, int x, int y)
    {
        var px = (byte[])canvas.Bgra.Clone();
        for (int row = 0; row < part.Height; row++)
        {
            int ty = y + row;
            if (ty < 0 || ty >= canvas.Height) continue;
            for (int col = 0; col < part.Width; col++)
            {
                int tx = x + col;
                if (tx < 0 || tx >= canvas.Width) continue;
                Buffer.BlockCopy(part.Bgra, row * part.Stride + col * 4, px, ty * canvas.Stride + tx * 4, 4);
            }
        }
        return new PixelImage(canvas.Width, canvas.Height, px, canvas.Stride);
    }

    /// <summary>Dreht um einen beliebigen Winkel (Grad, im Uhrzeigersinn) mit bilinearer Abtastung; Ecken weiß.</summary>
    public static PixelImage Rotate(PixelImage img, double degrees)
    {
        double rad = degrees * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
        int w = (int)Math.Ceiling(Math.Abs(img.Width * cos) + Math.Abs(img.Height * sin));
        int h = (int)Math.Ceiling(Math.Abs(img.Width * sin) + Math.Abs(img.Height * cos));
        var px = new byte[w * h * 4];
        double cxs = img.Width / 2.0, cys = img.Height / 2.0, cxd = w / 2.0, cyd = h / 2.0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double dx = x + 0.5 - cxd, dy = y + 0.5 - cyd;
                double sx = cos * dx + sin * dy + cxs - 0.5, sy = -sin * dx + cos * dy + cys - 0.5;
                int i = (y * w + x) * 4;
                for (int c = 0; c < 3; c++) px[i + c] = (byte)Math.Round(Bilinear(img, sx, sy, c));
                px[i + 3] = 255;
            }
        return new PixelImage(w, h, px);
    }

    /// <summary>Verkleinert/vergrößert auf eine Zielgröße mit Flächenmittelung (wie ein Browser, der ein Bild skaliert).</summary>
    public static PixelImage Resize(PixelImage img, int width, int height)
    {
        var px = new byte[width * height * 4];
        double fx = (double)img.Width / width, fy = (double)img.Height / height;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                if (fx <= 1 && fy <= 1)
                {
                    double sx = (x + 0.5) * fx - 0.5, sy = (y + 0.5) * fy - 0.5;
                    for (int c = 0; c < 3; c++) px[i + c] = (byte)Math.Round(Bilinear(img, sx, sy, c));
                }
                else
                {
                    // Flächenmittel über das Quellrechteck
                    double x0 = x * fx, x1 = (x + 1) * fx, y0 = y * fy, y1 = (y + 1) * fy;
                    var sum = new double[3];
                    double area = 0;
                    for (int sy = (int)y0; sy < Math.Min(img.Height, (int)Math.Ceiling(y1)); sy++)
                    {
                        double wy = Math.Min(sy + 1, y1) - Math.Max(sy, y0);
                        for (int sx = (int)x0; sx < Math.Min(img.Width, (int)Math.Ceiling(x1)); sx++)
                        {
                            double wx = Math.Min(sx + 1, x1) - Math.Max(sx, x0);
                            double wgt = wx * wy;
                            int s = sy * img.Stride + sx * 4;
                            for (int c = 0; c < 3; c++) sum[c] += img.Bgra[s + c] * wgt;
                            area += wgt;
                        }
                    }
                    for (int c = 0; c < 3; c++) px[i + c] = (byte)Math.Round(sum[c] / area);
                }
                px[i + 3] = 255;
            }
        return new PixelImage(width, height, px);
    }

    /// <summary>Einfache Unschärfe (Kastenfilter mit Radius r).</summary>
    public static PixelImage Blur(PixelImage img, int radius)
    {
        var px = new byte[img.Width * img.Height * 4];
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                int i = (y * img.Width + x) * 4;
                for (int c = 0; c < 3; c++)
                {
                    int sum = 0, n = 0;
                    for (int dy = -radius; dy <= radius; dy++)
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int sx = Math.Clamp(x + dx, 0, img.Width - 1), sy = Math.Clamp(y + dy, 0, img.Height - 1);
                            sum += img.Bgra[sy * img.Stride + sx * 4 + c];
                            n++;
                        }
                    px[i + c] = (byte)(sum / n);
                }
                px[i + 3] = 255;
            }
        return new PixelImage(img.Width, img.Height, px);
    }

    /// <summary>Schwarze Codepixel bleiben, weiße werden durchsichtig (schwarzer Code auf transparentem Grund, Farbwerte 0).</summary>
    public static PixelImage TransparentBackground(PixelImage img)
    {
        var px = (byte[])img.Bgra.Clone();
        for (int i = 0; i < px.Length; i += 4)
        {
            if (px[i] > 128) { px[i] = px[i + 1] = px[i + 2] = 0; px[i + 3] = 0; }
            else px[i + 3] = 255;
        }
        return new PixelImage(img.Width, img.Height, px, img.Stride);
    }

    /// <summary>Bild mit größerem Zeilenabstand (Stride) als nötig – so liefern es manche Quellen.</summary>
    public static PixelImage WithStride(PixelImage img, int extraBytes)
    {
        int stride = img.Width * 4 + extraBytes;
        var px = new byte[stride * img.Height];
        Array.Fill(px, (byte)0x7F);
        for (int y = 0; y < img.Height; y++) Buffer.BlockCopy(img.Bgra, y * img.Stride, px, y * stride, img.Width * 4);
        return new PixelImage(img.Width, img.Height, px, stride);
    }

    private static double Bilinear(PixelImage img, double sx, double sy, int c)
    {
        int ix = (int)Math.Floor(sx), iy = (int)Math.Floor(sy);
        double fx = sx - ix, fy = sy - iy;
        double P(int x, int y) => x < 0 || y < 0 || x >= img.Width || y >= img.Height ? 255 : img.Bgra[y * img.Stride + x * 4 + c];
        return (P(ix, iy) * (1 - fx) + P(ix + 1, iy) * fx) * (1 - fy) + (P(ix, iy + 1) * (1 - fx) + P(ix + 1, iy + 1) * fx) * fy;
    }
}
