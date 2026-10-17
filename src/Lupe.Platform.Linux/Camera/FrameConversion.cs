using System.Buffers.Binary;
using Lupe.Core.Model;
using SkiaSharp;

namespace Lupe.Platform.Linux.Camera;

/// <summary>Rohformate einer Kamera, die Lupe selbst in BGRA umwandeln kann.</summary>
internal enum RawFormat
{
    Unknown,
    Yuyv,
    Uyvy,
    Nv12,
    Mjpeg,
    Rgb24,
    Bgra32,
}

/// <summary>
/// Kamerabilder (V4L2-Rohdaten) → BGRA. YUV nach BT.601 mit eingeschränktem Wertebereich (16–235), wie es Webcams liefern;
/// MJPEG über SkiaSharp. Reine Rechenfunktionen, ohne Kamera testbar.
/// </summary>
internal static class FrameConversion
{
    /// <summary>
    /// FlashCap legt unkomprimierten Bildern einen BMP-Kopf (BITMAPFILEHEADER + BITMAPINFOHEADER) voran; der wird
    /// übersprungen. Liefert die reinen Pixeldaten und ob sie laut Kopf von unten nach oben liegen.
    /// </summary>
    public static ReadOnlySpan<byte> StripBitmapHeader(ReadOnlySpan<byte> data, out bool bottomUp)
    {
        bottomUp = false;
        if (data.Length < 54 || data[0] != (byte)'B' || data[1] != (byte)'M') return data;
        int offset = BinaryPrimitives.ReadInt32LittleEndian(data[10..]);
        if (offset < 54 || offset > data.Length) return data;
        int height = BinaryPrimitives.ReadInt32LittleEndian(data[22..]);
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(data[30..]);
        // Nur echte RGB-Bitmaps (BI_RGB/BI_BITFIELDS) mit positiver Höhe liegen von unten nach oben
        bottomUp = height > 0 && compression is 0 or 3;
        return data[offset..];
    }

    /// <summary>Wandelt ein Bild um; null, wenn die Daten nicht passen (zu kurz, kaputtes JPEG).</summary>
    public static PixelImage? ToBgra(RawFormat format, int width, int height, ReadOnlySpan<byte> data)
    {
        if (format == RawFormat.Mjpeg) return DecodeJpeg(data);
        if (width <= 0 || height <= 0) return null;
        var pixels = StripBitmapHeader(data, out bool bottomUp);
        var bgra = GC.AllocateUninitializedArray<byte>(checked(width * height * 4));
        bool ok = format switch
        {
            RawFormat.Yuyv => Packed422(pixels, width, height, bgra, yFirst: true),
            RawFormat.Uyvy => Packed422(pixels, width, height, bgra, yFirst: false),
            RawFormat.Nv12 => Nv12(pixels, width, height, bgra),
            RawFormat.Rgb24 => Bgr24(pixels, width, height, bgra, bottomUp),
            RawFormat.Bgra32 => Bgra32(pixels, width, height, bgra, bottomUp),
            _ => false,
        };
        return ok ? new PixelImage(width, height, bgra) : null;
    }

    private static bool Packed422(ReadOnlySpan<byte> src, int width, int height, byte[] dst, bool yFirst)
    {
        int stride = ((width + 1) / 2) * 4;
        if (src.Length < (long)stride * height) return false;
        int y0 = yFirst ? 0 : 1, u = yFirst ? 1 : 0, y1 = yFirst ? 2 : 3, v = yFirst ? 3 : 2;
        for (int row = 0; row < height; row++)
        {
            var line = src.Slice(row * stride, stride);
            int o = row * width * 4;
            for (int x = 0; x < width; x += 2)
            {
                int i = x * 2;
                int cu = line[i + u] - 128, cv = line[i + v] - 128;
                Write(dst, o, line[i + y0], cu, cv);
                o += 4;
                if (x + 1 < width)
                {
                    Write(dst, o, line[i + y1], cu, cv);
                    o += 4;
                }
            }
        }
        return true;
    }

    private static bool Nv12(ReadOnlySpan<byte> src, int width, int height, byte[] dst)
    {
        int chromaWidth = (width + 1) / 2 * 2, chromaHeight = (height + 1) / 2;
        long need = (long)width * height + (long)chromaWidth * chromaHeight;
        if (src.Length < need) return false;
        var luma = src[..(width * height)];
        var chroma = src[(width * height)..];
        for (int row = 0; row < height; row++)
        {
            var c = chroma.Slice(row / 2 * chromaWidth, chromaWidth);
            int o = row * width * 4;
            for (int x = 0; x < width; x++, o += 4)
            {
                int ci = x / 2 * 2;
                Write(dst, o, luma[row * width + x], c[ci] - 128, c[ci + 1] - 128);
            }
        }
        return true;
    }

    private static bool Bgr24(ReadOnlySpan<byte> src, int width, int height, byte[] dst, bool bottomUp)
    {
        int stride = (width * 3 + 3) & ~3;
        if (src.Length < (long)stride * (height - 1) + width * 3) return false;
        for (int row = 0; row < height; row++)
        {
            var line = src.Slice((bottomUp ? height - 1 - row : row) * stride, width * 3);
            int o = row * width * 4;
            for (int x = 0; x < width; x++, o += 4)
            {
                dst[o] = line[x * 3];
                dst[o + 1] = line[x * 3 + 1];
                dst[o + 2] = line[x * 3 + 2];
                dst[o + 3] = 255;
            }
        }
        return true;
    }

    private static bool Bgra32(ReadOnlySpan<byte> src, int width, int height, byte[] dst, bool bottomUp)
    {
        int stride = width * 4;
        if (src.Length < (long)stride * height) return false;
        for (int row = 0; row < height; row++)
        {
            var line = src.Slice((bottomUp ? height - 1 - row : row) * stride, stride);
            line.CopyTo(dst.AsSpan(row * stride));
            for (int o = row * stride + 3; o < (row + 1) * stride; o += 4) dst[o] = 255;
        }
        return true;
    }

    /// <summary>BT.601, eingeschränkter Bereich, ganzzahlig.</summary>
    private static void Write(byte[] dst, int o, int y, int u, int v)
    {
        int c = (y - 16) * 298 + 128;
        dst[o] = Clamp((c + 516 * u) >> 8);
        dst[o + 1] = Clamp((c - 100 * u - 208 * v) >> 8);
        dst[o + 2] = Clamp((c + 409 * v) >> 8);
        dst[o + 3] = 255;
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

    /// <summary>JPEG (MJPEG-Einzelbild) → BGRA; null bei kaputten Daten.</summary>
    public static PixelImage? DecodeJpeg(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) return null;
        using var stream = new SKMemoryStream(data.ToArray());
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0) return null;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        // Abgeschnittene Bilder (letzte Zeilen fehlen) sind für die Vorschau noch brauchbar
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) return null;
        return FromBitmap(bitmap);
    }

    /// <summary>Kopiert ein BGRA-SKBitmap in ein <see cref="PixelImage"/> (Alpha 255).</summary>
    internal static PixelImage FromBitmap(SKBitmap bitmap)
    {
        int w = bitmap.Width, h = bitmap.Height, stride = w * 4;
        var bytes = GC.AllocateUninitializedArray<byte>(stride * h);
        var src = bitmap.GetPixelSpan();
        for (int row = 0; row < h; row++)
            src.Slice(row * bitmap.RowBytes, stride).CopyTo(bytes.AsSpan(row * stride));
        for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
        return new PixelImage(w, h, bytes);
    }
}
