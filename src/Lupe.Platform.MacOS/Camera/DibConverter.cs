using System.Buffers.Binary;
using System.Numerics;
using Lupe.Core.Model;

namespace Lupe.Platform.MacOS.Camera;

/// <summary>
/// FlashCap liefert unkomprimierte Kamerabilder (nach dem Umrechnen von YUV) als BMP-Datei im Speicher. Das hier macht
/// daraus BGRA mit Alpha 255: 24 und 32 Bit, BI_RGB und BI_BITFIELDS, von unten nach oben (positive Höhe) oder von
/// oben nach unten (negative Höhe).
/// </summary>
internal static class DibConverter
{
    private const int FileHeaderSize = 14;
    private const uint BI_RGB = 0, BI_BITFIELDS = 3, BI_ALPHABITFIELDS = 6;

    public static bool IsBitmap(ReadOnlySpan<byte> data) => data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M';

    public static bool IsJpeg(ReadOnlySpan<byte> data) => data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

    /// <summary>Wandelt die BMP-Datei um; wirft <see cref="InvalidDataException"/> bei Unbekanntem.</summary>
    public static PixelImage Convert(ReadOnlySpan<byte> bmp)
    {
        if (!IsBitmap(bmp) || bmp.Length < FileHeaderSize + 40) throw new InvalidDataException("Kein BMP-Bild.");
        uint offBits = BinaryPrimitives.ReadUInt32LittleEndian(bmp[10..]);
        var info = bmp[FileHeaderSize..];
        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(info);
        int width = BinaryPrimitives.ReadInt32LittleEndian(info[4..]);
        int rawHeight = BinaryPrimitives.ReadInt32LittleEndian(info[8..]);
        int bitCount = BinaryPrimitives.ReadUInt16LittleEndian(info[14..]);
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(info[16..]);
        if (headerSize < 40 || width <= 0 || rawHeight == 0 || rawHeight == int.MinValue)
            throw new InvalidDataException("BMP-Kopf ungültig.");
        bool topDown = rawHeight < 0;
        int height = Math.Abs(rawHeight);

        // Farbmasken: im V4/V5-Kopf oder (bei 40-Byte-Kopf + BI_BITFIELDS) direkt dahinter
        uint rMask = 0x00FF0000, gMask = 0x0000FF00, bMask = 0x000000FF;
        if (compression is BI_BITFIELDS or BI_ALPHABITFIELDS)
        {
            if (bitCount != 32) throw new InvalidDataException($"BMP mit Bitfeldern und {bitCount} Bit wird nicht unterstützt.");
            var masks = info[40..];
            if (masks.Length < 12) throw new InvalidDataException("BMP-Farbmasken fehlen.");
            rMask = BinaryPrimitives.ReadUInt32LittleEndian(masks);
            gMask = BinaryPrimitives.ReadUInt32LittleEndian(masks[4..]);
            bMask = BinaryPrimitives.ReadUInt32LittleEndian(masks[8..]);
            if (rMask == 0 || gMask == 0 || bMask == 0) throw new InvalidDataException("BMP-Farbmasken ungültig.");
        }
        else if (compression != BI_RGB)
        {
            throw new InvalidDataException($"BMP-Kompression {compression} wird nicht unterstützt.");
        }
        if (bitCount is not (24 or 32)) throw new InvalidDataException($"BMP mit {bitCount} Bit wird nicht unterstützt.");

        int bytesPerPixel = bitCount / 8;
        long srcStride = ((long)width * bitCount + 31) / 32 * 4;
        if (offBits > bmp.Length || srcStride * height > bmp.Length - offBits) throw new InvalidDataException("BMP-Daten unvollständig.");

        var pixels = bmp[(int)offBits..];
        int stride = checked(width * 4);
        var bgra = GC.AllocateUninitializedArray<byte>(checked(stride * height));
        bool standard = rMask == 0x00FF0000 && gMask == 0x0000FF00 && bMask == 0x000000FF;
        int rShift = BitOperations.TrailingZeroCount(rMask), gShift = BitOperations.TrailingZeroCount(gMask), bShift = BitOperations.TrailingZeroCount(bMask);

        for (int y = 0; y < height; y++)
        {
            int srcRow = topDown ? y : height - 1 - y;
            var src = pixels.Slice((int)(srcRow * srcStride), (int)srcStride);
            int dst = y * stride;
            if (bytesPerPixel == 3 || standard)
            {
                for (int x = 0, s = 0; x < width; x++, s += bytesPerPixel, dst += 4)
                {
                    bgra[dst] = src[s];
                    bgra[dst + 1] = src[s + 1];
                    bgra[dst + 2] = src[s + 2];
                    bgra[dst + 3] = 255;
                }
            }
            else
            {
                for (int x = 0, s = 0; x < width; x++, s += 4, dst += 4)
                {
                    uint v = BinaryPrimitives.ReadUInt32LittleEndian(src[s..]);
                    bgra[dst] = (byte)((v & bMask) >> bShift);
                    bgra[dst + 1] = (byte)((v & gMask) >> gShift);
                    bgra[dst + 2] = (byte)((v & rMask) >> rShift);
                    bgra[dst + 3] = 255;
                }
            }
        }
        return new PixelImage(width, height, bgra, stride);
    }
}
