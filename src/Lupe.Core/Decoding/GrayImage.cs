using Lupe.Core.Model;

namespace Lupe.Core.Decoding;

/// <summary>
/// Graustufenbild mit einem Byte je Pixel (0 = schwarz, 255 = weiß), Zeilen ohne Lücke. Arbeitsform der Erkennung:
/// einmal aus BGRA umgerechnet, danach nur noch zugeschnitten, skaliert, gerahmt oder invertiert.
/// </summary>
internal sealed class GrayImage
{
    // Gewichte wie in ZXing (ITU-R BT.601, Summe 65536)
    private const int WeightR = 19562;
    private const int WeightG = 38550;
    private const int WeightB = 7424;

    /// <summary>Ab dieser Pixelzahl lohnt sich die Umrechnung auf mehreren Kernen.</summary>
    private const int ParallelThreshold = 1 << 20;

    public GrayImage(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length < (long)width * height) throw new ArgumentException("Puffer kleiner als Breite × Höhe.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    /// <summary>
    /// Rechnet BGRA in Helligkeit um. Durchsichtige Pixel werden auf weißem Grund verrechnet (schwarzer Code auf
    /// transparentem PNG bleibt lesbar). Ist der Alphakanal überall 0 – das liefern manche GDI-Aufnahmen –, gilt das
    /// Bild als undurchsichtig, sonst wäre es komplett weiß.
    /// </summary>
    public static GrayImage FromBgra(PixelImage image, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        int w = image.Width, h = image.Height, stride = image.Stride;
        var src = image.Bgra;
        var mode = DetectAlpha(src, w, h, stride);
        var dst = new byte[w * h];

        void Row(int y)
        {
            int s = y * stride, d = y * w;
            switch (mode)
            {
                case AlphaMode.Opaque:
                    for (int x = 0; x < w; x++, s += 4)
                        dst[d + x] = (byte)((src[s] * WeightB + src[s + 1] * WeightG + src[s + 2] * WeightR) >> 16);
                    break;
                default:
                    for (int x = 0; x < w; x++, s += 4)
                    {
                        int lum = (src[s] * WeightB + src[s + 1] * WeightG + src[s + 2] * WeightR) >> 16;
                        int a = src[s + 3];
                        // lum·α + Weiß·(1 − α), gerundet
                        dst[d + x] = (byte)((lum * a + 255 * (255 - a) + 127) / 255);
                    }
                    break;
            }
        }

        if ((long)w * h >= ParallelThreshold)
            Parallel.For(0, h, new ParallelOptions { CancellationToken = ct }, Row);
        else
            for (int y = 0; y < h; y++) Row(y);
        return new GrayImage(w, h, dst);
    }

    private enum AlphaMode { Opaque, Blend }

    private static AlphaMode DetectAlpha(byte[] bgra, int w, int h, int stride)
    {
        bool anyTransparent = false, anyVisible = false;
        for (int y = 0; y < h; y++)
        {
            int i = y * stride + 3, end = i + w * 4;
            for (; i < end; i += 4)
            {
                byte a = bgra[i];
                if (a != 255) anyTransparent = true;
                if (a != 0) anyVisible = true;
                if (anyTransparent && anyVisible) return AlphaMode.Blend;
            }
        }
        // Nur 255 (normal) oder nur 0 (Alpha nicht gepflegt): Farben unverändert nehmen
        return AlphaMode.Opaque;
    }

    /// <summary>Ausschnitt als eigenes Bild.</summary>
    public GrayImage Crop(int left, int top, int width, int height)
    {
        if (left < 0 || top < 0 || width <= 0 || height <= 0 || left + width > Width || top + height > Height)
            throw new ArgumentOutOfRangeException(nameof(width), "Ausschnitt liegt nicht im Bild.");
        var dst = new byte[width * height];
        for (int y = 0; y < height; y++)
            Buffer.BlockCopy(Pixels, (top + y) * Width + left, dst, y * width, width);
        return new GrayImage(width, height, dst);
    }

    /// <summary>Rahmen rundherum – gibt Codes, die bis an den Rand reichen, eine Ruhezone.</summary>
    public GrayImage Pad(int border, byte value = 255)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(border);
        if (border == 0) return this;
        int w = Width + 2 * border, h = Height + 2 * border;
        var dst = new byte[w * h];
        if (value != 0) Array.Fill(dst, value);
        for (int y = 0; y < Height; y++)
            Buffer.BlockCopy(Pixels, y * Width, dst, (y + border) * w + border, Width);
        return new GrayImage(w, h, dst);
    }

    /// <summary>Hell und dunkel vertauscht (für helle Codes auf dunklem Grund).</summary>
    public GrayImage Invert()
    {
        var dst = new byte[Width * Height];
        var src = Pixels;
        for (int i = 0; i < dst.Length; i++) dst[i] = (byte)~src[i];
        return new GrayImage(Width, Height, dst);
    }

    /// <summary>Verkleinert um einen ganzzahligen Faktor (Mittelwert je Block) – glättet Rauschen und spart Zeit.</summary>
    public GrayImage Downscale(int factor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(factor, 1);
        if (factor == 1) return this;
        int w = Width / factor, h = Height / factor;
        if (w < 1 || h < 1) throw new ArgumentOutOfRangeException(nameof(factor), "Bild zu klein für diesen Faktor.");
        var dst = new byte[w * h];
        int area = factor * factor, half = area / 2;
        var src = Pixels;
        int srcW = Width;

        void Row(int y)
        {
            int baseRow = y * factor * srcW;
            for (int x = 0; x < w; x++)
            {
                int sum = 0, p = baseRow + x * factor;
                for (int dy = 0; dy < factor; dy++, p += srcW)
                    for (int dx = 0; dx < factor; dx++)
                        sum += src[p + dx];
                dst[y * w + x] = (byte)((sum + half) / area);
            }
        }

        if ((long)Width * Height >= ParallelThreshold) Parallel.For(0, h, Row);
        else for (int y = 0; y < h; y++) Row(y);
        return new GrayImage(w, h, dst);
    }

    /// <summary>Vergrößert um einen ganzzahligen Faktor (bilinear) – hilft bei sehr kleinen Codes mit 1–2 Pixel je Modul.</summary>
    public GrayImage Upscale(int factor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(factor, 1);
        if (factor == 1) return this;
        int w = Width * factor, h = Height * factor;
        var dst = new byte[w * h];
        var src = Pixels;
        int srcW = Width, srcH = Height;

        // Vorberechnete Spalten: linker Nachbar und Gewicht des rechten (in 1/256)
        var x0 = new int[w];
        var fx = new int[w];
        for (int x = 0; x < w; x++)
        {
            double sx = (x + 0.5) / factor - 0.5;
            int ix = (int)Math.Floor(sx);
            double f = sx - ix;
            if (ix < 0) { ix = 0; f = 0; }
            if (ix >= srcW - 1) { ix = srcW - 1; f = 0; }
            x0[x] = ix;
            fx[x] = (int)Math.Round(f * 256);
        }

        void Row(int y)
        {
            double sy = (y + 0.5) / factor - 0.5;
            int iy = (int)Math.Floor(sy);
            double g = sy - iy;
            if (iy < 0) { iy = 0; g = 0; }
            if (iy >= srcH - 1) { iy = srcH - 1; g = 0; }
            int fy = (int)Math.Round(g * 256);
            int r0 = iy * srcW, r1 = Math.Min(iy + 1, srcH - 1) * srcW, d = y * w;
            for (int x = 0; x < w; x++)
            {
                int ix = x0[x], ix1 = Math.Min(ix + 1, srcW - 1), wx = fx[x];
                int top = src[r0 + ix] * (256 - wx) + src[r0 + ix1] * wx;
                int bottom = src[r1 + ix] * (256 - wx) + src[r1 + ix1] * wx;
                dst[d + x] = (byte)((top * (256 - fy) + bottom * fy + 32768) >> 16);
            }
        }

        if ((long)w * h >= ParallelThreshold) Parallel.For(0, h, Row);
        else for (int y = 0; y < h; y++) Row(y);
        return new GrayImage(w, h, dst);
    }

    /// <summary>
    /// Dreht um einen beliebigen Winkel (Grad, im Uhrzeigersinn auf dem Bildschirm) um die Bildmitte, bilinear; das
    /// Ergebnis ist so groß, dass nichts abgeschnitten wird, freie Flächen sind weiß. Liefert die Abbildung zurück ins Original.
    /// </summary>
    public GrayImage Rotate(double degrees, out PointMap toSource)
    {
        toSource = RotationMap(Width, Height, degrees, out int w, out int h);
        var map = toSource;
        var dst = new byte[w * h];
        var src = Pixels;
        int srcW = Width, srcH = Height;
        const int One = 1 << 16;
        // Schrittweiten je Zielpixel in 16.16-Festkomma (Quellposition in Pixelmitten-Koordinaten)
        long stepX = (long)Math.Round(map.A * One), stepY = (long)Math.Round(map.D * One);

        void Row(int y)
        {
            var (sx0, sy0) = map.Apply(0.5, y + 0.5);
            long sx = (long)Math.Round((sx0 - 0.5) * One), sy = (long)Math.Round((sy0 - 0.5) * One);
            int d = y * w;
            for (int x = 0; x < w; x++, sx += stepX, sy += stepY)
            {
                int ix = (int)(sx >> 16), iy = (int)(sy >> 16);
                if (ix < -1 || iy < -1 || ix >= srcW || iy >= srcH) { dst[d + x] = 255; continue; }
                int fx = (int)(sx & 0xFFFF) >> 8, fy = (int)(sy & 0xFFFF) >> 8; // Gewichte in 1/256
                int p00, p10, p01, p11;
                if (ix >= 0 && iy >= 0 && ix + 1 < srcW && iy + 1 < srcH)
                {
                    int i = iy * srcW + ix;
                    p00 = src[i]; p10 = src[i + 1]; p01 = src[i + srcW]; p11 = src[i + srcW + 1];
                }
                else
                {
                    p00 = Sample(ix, iy); p10 = Sample(ix + 1, iy); p01 = Sample(ix, iy + 1); p11 = Sample(ix + 1, iy + 1);
                }
                int top = p00 * (256 - fx) + p10 * fx, bottom = p01 * (256 - fx) + p11 * fx;
                dst[d + x] = (byte)((top * (256 - fy) + bottom * fy + 32768) >> 16);
            }
        }

        int Sample(int x, int y) => x < 0 || y < 0 || x >= srcW || y >= srcH ? 255 : src[y * srcW + x];

        if ((long)w * h >= ParallelThreshold) Parallel.For(0, h, Row);
        else for (int y = 0; y < h; y++) Row(y);
        return new GrayImage(w, h, dst);
    }

    /// <summary>Die Rückabbildung von <see cref="Rotate"/> für ein Bild der Größe <paramref name="width"/>×<paramref name="height"/>, ohne zu drehen.</summary>
    public static PointMap RotationMap(int width, int height, double degrees) => RotationMap(width, height, degrees, out _, out _);

    private static PointMap RotationMap(int width, int height, double degrees, out int rotatedWidth, out int rotatedHeight)
    {
        double rad = degrees * Math.PI / 180;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        // Rundungsrauschen (z. B. cos 90° ≈ 6e-17) nicht zu einem Pixel mehr aufblähen
        rotatedWidth = Math.Max(1, (int)Math.Ceiling(Math.Abs(width * cos) + Math.Abs(height * sin) - 1e-9));
        rotatedHeight = Math.Max(1, (int)Math.Ceiling(Math.Abs(width * sin) + Math.Abs(height * cos) - 1e-9));
        return PointMap.Rotation(cos, sin, rotatedWidth / 2.0, rotatedHeight / 2.0, width / 2.0, height / 2.0);
    }
}

/// <summary>
/// Abbildung von Punkten eines abgeleiteten Bildes (Ausschnitt, skaliert, gerahmt, gedreht) zurück ins Original.
/// Koordinaten wie bei ZXing: Pixel i reicht von i bis i + 1.
/// </summary>
internal readonly record struct PointMap(double A, double B, double C, double D, double E, double F)
{
    /// <summary>x' = A·x + B·y + C, y' = D·x + E·y + F.</summary>
    public (double X, double Y) Apply(double x, double y) => (A * x + B * y + C, D * x + E * y + F);

    public static PointMap Identity { get; } = new(1, 0, 0, 0, 1, 0);

    /// <summary>Original = Punkt · scale + offset (Ausschnitt: Versatz, verkleinert: Faktor, vergrößert: 1/Faktor, Rahmen: −Breite).</summary>
    public static PointMap ScaleOffset(double scale, double offsetX, double offsetY) => new(scale, 0, offsetX, 0, scale, offsetY);

    public static PointMap Rotation(double cos, double sin, double cxd, double cyd, double cxs, double cys) =>
        new(cos, sin, cxs - cos * cxd - sin * cyd, -sin, cos, cys + sin * cxd - cos * cyd);

    /// <summary>Erst <paramref name="inner"/> (Ableitung → Zwischenbild), dann diese Abbildung (Zwischenbild → Original).</summary>
    public PointMap After(PointMap inner) => new(
        A * inner.A + B * inner.D, A * inner.B + B * inner.E, A * inner.C + B * inner.F + C,
        D * inner.A + E * inner.D, D * inner.B + E * inner.E, D * inner.C + E * inner.F + F);
}
