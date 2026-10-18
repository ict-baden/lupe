using Lupe.Core.Model;
using Lupe.Core.Platform;
using Lupe.Core.Resources;
using Lupe.Platform.MacOS.Native;

namespace Lupe.Platform.MacOS.Imaging;

/// <summary>
/// Bilddateien über ImageIO (das Gegenstück zu WIC): PNG, JPEG, GIF, BMP, TIFF, HEIC, WebP, ICO … EXIF-Drehung über
/// <c>kCGImageSourceCreateThumbnailWithTransform</c>, Transparenz auf Weiß (das Bild wird auf eine weiße Fläche gezeichnet).
/// </summary>
public sealed class MacOSImageDecoder : IImageDecoder
{
    public IReadOnlyList<string> Extensions { get; } =
        [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".heic", ".heif", ".webp", ".ico"];

    public async Task<PixelImage> DecodeAsync(Stream stream, string name, int maxPixels, CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS()) throw new InvalidDataException(string.Format(Strings.Mac_NotHere_Image, name));
        var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        bool largest = name.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".cur", StringComparison.OrdinalIgnoreCase);
        return ImageIOReader.Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), name, maxPixels, largest);
    }

    public Exception Translate(Exception ex, string name) => ex switch
    {
        InvalidDataException or IOException => ex,
        OutOfMemoryException or InsufficientExecutionStackException => new InvalidDataException(string.Format(Strings.Mac_Image_TooLarge, name), ex),
        DllNotFoundException or EntryPointNotFoundException =>
            new InvalidDataException(string.Format(Strings.Mac_Image_ImageIOMissing, name), ex),
        _ => new InvalidDataException(string.Format(Strings.Mac_Image_NotReadable, name, ex.Message.Trim()), ex),
    };
}

/// <summary>ImageIO + CoreGraphics: Bytes einer Bilddatei → BGRA (Alpha 255).</summary>
internal static class ImageIOReader
{
    public static unsafe PixelImage Decode(ReadOnlySpan<byte> bytes, string name, int maxPixels, bool largestFrame)
    {
        if (bytes.IsEmpty) throw new InvalidDataException(string.Format(Strings.Mac_Image_EmptyFile, name));
        nint data = 0, source = 0, image = 0, oriented = 0, options = 0;
        try
        {
            fixed (byte* p = bytes) data = CF.CFDataCreate(0, p, bytes.Length);
            if (data == 0) throw new OutOfMemoryException();
            source = ImageIO.CGImageSourceCreateWithData(data, 0);
            nuint count = source == 0 ? 0 : ImageIO.CGImageSourceGetCount(source);
            if (count == 0) throw Unsupported(name);

            nuint index = 0;
            if (largestFrame && count > 1)
            {
                long best = -1;
                for (nuint i = 0; i < count; i++)
                {
                    nint frame = ImageIO.CGImageSourceCreateImageAtIndex(source, i, 0);
                    if (frame == 0) continue;
                    long area = (long)CG.CGImageGetWidth(frame) * (long)CG.CGImageGetHeight(frame);
                    CG.CGImageRelease(frame);
                    if (area > best)
                    {
                        best = area;
                        index = i;
                    }
                }
            }

            image = ImageIO.CGImageSourceCreateImageAtIndex(source, index, 0);
            if (image == 0) throw new InvalidDataException(string.Format(Strings.Mac_Image_Corrupt, name));
            long rawW = (long)CG.CGImageGetWidth(image), rawH = (long)CG.CGImageGetHeight(image);
            if (rawW <= 0 || rawH <= 0) throw new InvalidDataException(string.Format(Strings.Mac_Image_Empty, name));

            // Mit EXIF-Drehung und schon passend verkleinert: das „Vorschaubild“ in Zielgröße
            var (fitW, fitH) = FitInto(checked((int)rawW), checked((int)rawH), maxPixels);
            nint always = ImageIO.Key("kCGImageSourceCreateThumbnailFromImageAlways");
            nint transform = ImageIO.Key("kCGImageSourceCreateThumbnailWithTransform");
            nint maxSize = ImageIO.Key("kCGImageSourceThumbnailMaxPixelSize");
            if (always != 0 && transform != 0 && maxSize != 0)
            {
                nint number = CF.Number(Math.Max(fitW, fitH));
                try
                {
                    options = CF.Dictionary([always, transform, maxSize], [CF.True, CF.True, number]);
                }
                finally
                {
                    CF.CFRelease(number);
                }
                if (options != 0) oriented = ImageIO.CGImageSourceCreateThumbnailAtIndex(source, index, options);
            }
            nint drawn = oriented != 0 ? oriented : image;
            int w = checked((int)CG.CGImageGetWidth(drawn)), h = checked((int)CG.CGImageGetHeight(drawn));
            var (tw, th) = FitInto(w, h, maxPixels);
            return Render(drawn, tw, th);
        }
        finally
        {
            if (options != 0) CF.CFRelease(options);
            if (oriented != 0) CG.CGImageRelease(oriented);
            if (image != 0) CG.CGImageRelease(image);
            if (source != 0) CF.CFRelease(source);
            if (data != 0) CF.CFRelease(data);
        }
    }

    /// <summary>Zielgröße: höchstens <paramref name="maxPixels"/> Pixel, Seitenverhältnis bleibt, mindestens 1×1.</summary>
    internal static (int Width, int Height) FitInto(int width, int height, int maxPixels)
    {
        long area = (long)width * height;
        if (maxPixels <= 0 || area <= maxPixels) return (width, height);
        double f = Math.Sqrt((double)maxPixels / area);
        int w = Math.Max(1, (int)Math.Floor(width * f));
        // Bei extremen Seitenverhältnissen kann die schmale Seite nicht unter 1 – dann die lange Seite kürzen
        int h = Math.Max(1, (int)Math.Min(Math.Floor(height * f), maxPixels / w));
        return (w, h);
    }

    /// <summary>Zeichnet das Bild auf Weiß in einen BGRA-Puffer (premultiplied first + 32 Bit little = B,G,R,A im Speicher).</summary>
    private static unsafe PixelImage Render(nint image, int width, int height)
    {
        int stride = checked(width * 4);
        var pixels = GC.AllocateUninitializedArray<byte>(checked(stride * height));
        nint space = CG.CGColorSpaceCreateDeviceRGB();
        try
        {
            fixed (byte* p = pixels)
            {
                nint context = CG.CGBitmapContextCreate(p, (nuint)width, (nuint)height, 8, (nuint)stride, space,
                    CG.kCGImageAlphaPremultipliedFirst | CG.kCGBitmapByteOrder32Little);
                if (context == 0) throw new InvalidDataException(Strings.Mac_Image_ConvertFailed);
                try
                {
                    var rect = new CGRect(0, 0, width, height);
                    CG.CGContextSetRGBFillColor(context, 1, 1, 1, 1);
                    CG.CGContextFillRect(context, rect);
                    CG.CGContextSetInterpolationQuality(context, CG.kCGInterpolationHigh);
                    CG.CGContextDrawImage(context, rect, image);
                }
                finally
                {
                    CG.CGContextRelease(context);
                }
            }
        }
        finally
        {
            if (space != 0) CG.CGColorSpaceRelease(space);
        }
        // Auf Weiß gezeichnet: überall deckend; zur Sicherheit Alpha setzen
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        return new PixelImage(width, height, pixels, stride);
    }

    private static InvalidDataException Unsupported(string name) =>
        new(string.Format(Strings.Mac_Image_Unsupported, name));
}
