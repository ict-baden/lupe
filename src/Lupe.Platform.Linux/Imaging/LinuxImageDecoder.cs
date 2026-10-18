using Lupe.Core.Decoding;
using Lupe.Core.Model;
using Lupe.Core.Platform;
using Lupe.Core.Resources;
using SkiaSharp;

namespace Lupe.Platform.Linux.Imaging;

/// <summary>
/// Bilddateien über SkiaSharp (PNG, JPEG, BMP, GIF, WebP, ICO, WBMP; HEIF/AVIF nur, wenn libskia sie kann): EXIF-Drehung,
/// erstes Bild, ICO das größte, Verkleinern über <c>maxPixels</c>, Transparenz auf Weiß.
/// </summary>
public sealed class LinuxImageDecoder : IImageDecoder
{
    public IReadOnlyList<string> Extensions { get; } =
        [".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".bmp", ".dib", ".gif", ".webp", ".ico", ".wbmp"];

    public Task<PixelImage> DecodeAsync(Stream stream, string name, int maxPixels, CancellationToken ct) =>
        Task.Run(() => Decode(stream, name, maxPixels), ct);

    public Exception Translate(Exception ex, string name) => ex switch
    {
        InvalidDataException or IOException => ex,
        OutOfMemoryException => new InvalidDataException(string.Format(Strings.Linux_Image_TooLarge, name), ex),
        _ => new InvalidDataException(string.Format(Strings.Linux_Image_NotReadable, name, ex.Message.Trim()), ex),
    };

    internal static PixelImage Decode(Stream stream, string name, int maxPixels)
    {
        // Skia liest den Strom selbst; schließen darf es ihn nicht
        using var managed = new SKManagedStream(stream, disposeManagedStream: false);
        using var codec = SKCodec.Create(managed, out var result)
            ?? throw new InvalidDataException(string.Format(Strings.Linux_Image_Unsupported, name, result));
        var size = codec.Info;
        if (size.Width <= 0 || size.Height <= 0) throw new InvalidDataException(string.Format(Strings.Linux_Image_Empty, name));

        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var full = new SKBitmap(info);
        var options = new SKCodecOptions(0);
        var decoded = codec.GetPixels(info, full.GetPixels(), options);
        if (decoded is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            throw new InvalidDataException(string.Format(Strings.Linux_Image_Corrupt, name, decoded));

        using var oriented = Orient(full, codec.EncodedOrigin);
        using var scaled = Scale(oriented ?? full, maxPixels);
        var source = scaled ?? oriented ?? full;
        int w = source.Width, h = source.Height, stride = w * 4;
        var bytes = GC.AllocateUninitializedArray<byte>(stride * h);
        var span = source.GetPixelSpan();
        for (int row = 0; row < h; row++) span.Slice(row * source.RowBytes, stride).CopyTo(bytes.AsSpan(row * stride));
        ImageFile.FlattenOnWhite(bytes);
        return new PixelImage(w, h, bytes);
    }

    /// <summary>EXIF-Ausrichtung anwenden; null, wenn das Bild schon richtig liegt.</summary>
    private static SKBitmap? Orient(SKBitmap src, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return null;
        bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        int w = swap ? src.Height : src.Width, h = swap ? src.Width : src.Height;
        var dst = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(dst);
        canvas.SetMatrix(OriginMatrix(origin, src.Width, src.Height));
        canvas.DrawBitmap(src, 0, 0);
        return dst;
    }

    private static SKMatrix OriginMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        _ => SKMatrix.Identity,
    };

    private static SKBitmap? Scale(SKBitmap src, int maxPixels)
    {
        double total = (double)src.Width * src.Height;
        if (maxPixels <= 0 || total <= maxPixels) return null;
        double factor = Math.Sqrt(maxPixels / total);
        int w = Math.Max(1, (int)Math.Floor(src.Width * factor)), h = Math.Max(1, (int)Math.Floor(src.Height * factor));
        return src.Resize(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }
}
