using Lupe.Core.Decoding;
using Lupe.Core.Model;
using Lupe.Core.Platform;
using Lupe.Core.Resources;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Lupe.Platform.Windows.Imaging;

/// <summary>
/// Bilddateien über Windows.Graphics.Imaging (WIC): EXIF-Drehung, Farben nach sRGB, GIF/TIFF erstes Bild, ICO das größte,
/// Verkleinern über <c>maxPixels</c>, Transparenz auf Weiß.
/// </summary>
public sealed class WicImageDecoder : IImageDecoder
{
    /// <summary>Endungen, die angeboten werden, sofern Windows dafür einen Decoder hat – in dieser Reihenfolge.</summary>
    private static readonly string[] Candidates =
    [
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".bmp", ".dib", ".gif", ".tif", ".tiff", ".ico", ".jxr", ".wdp",
        ".webp", ".heic", ".heif", ".hif", ".avif", ".jxl",
    ];

    /// <summary>Diese Decoder bringt Windows immer mit.</summary>
    private static readonly string[] BuiltIn =
        [".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".bmp", ".dib", ".gif", ".tif", ".tiff", ".ico", ".jxr", ".wdp"];

    /// <summary>Formate, für die Windows eine Erweiterung aus dem Microsoft Store braucht.</summary>
    private static readonly string[] NeedsExtension = [".webp", ".heic", ".heif", ".hif", ".avif", ".jxl"];

    private static readonly Lazy<IReadOnlyList<string>> s_extensions = new(DetectExtensions);

    public IReadOnlyList<string> Extensions => s_extensions.Value;

    public async Task<PixelImage> DecodeAsync(Stream stream, string name, int maxPixels, CancellationToken ct)
    {
        using IRandomAccessStream ras = stream.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(ras).AsTask(ct).ConfigureAwait(false);
        var frame = await PickFrameAsync(decoder, ct).ConfigureAwait(false);
        return await ReadPixelsAsync(frame, name, maxPixels, ct).ConfigureAwait(false);
    }

    public Exception Translate(Exception ex, string name) => TranslateError(ex, name);

    /// <summary>Erstes Bild; bei Symbolen (ICO) das größte, damit nicht das 16×16-Bildchen gelesen wird.</summary>
    private static async Task<BitmapFrame> PickFrameAsync(BitmapDecoder decoder, CancellationToken ct)
    {
        var first = await decoder.GetFrameAsync(0).AsTask(ct).ConfigureAwait(false);
        if (decoder.FrameCount <= 1 || decoder.DecoderInformation?.CodecId != BitmapDecoder.IcoDecoderId) return first;
        var best = first;
        for (uint i = 1; i < decoder.FrameCount; i++)
        {
            var frame = await decoder.GetFrameAsync(i).AsTask(ct).ConfigureAwait(false);
            if ((long)frame.PixelWidth * frame.PixelHeight > (long)best.PixelWidth * best.PixelHeight) best = frame;
        }
        return best;
    }

    private static async Task<PixelImage> ReadPixelsAsync(BitmapFrame frame, string name, int maxPixels, CancellationToken ct)
    {
        uint pw = frame.PixelWidth, ph = frame.PixelHeight;
        if (pw == 0 || ph == 0) throw new InvalidDataException(string.Format(Strings.Win_Image_Empty, name));
        // Drehung um 90°/270° (EXIF) vertauscht Breite und Höhe
        bool swapped = frame.OrientedPixelWidth != pw || frame.OrientedPixelHeight != ph;

        var transform = new BitmapTransform();
        uint w = pw, h = ph;
        double total = (double)pw * ph;
        if (total > maxPixels)
        {
            double scale = Math.Sqrt(maxPixels / total);
            w = Math.Max(1, (uint)Math.Floor(pw * scale));
            h = Math.Max(1, (uint)Math.Floor(ph * scale));
            transform.ScaledWidth = w;
            transform.ScaledHeight = h;
            transform.InterpolationMode = BitmapInterpolationMode.Fant;
        }

        PixelDataProvider data;
        try
        {
            data = await frame.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Kaputte oder exotische Farbprofile: dann eben ohne Farbumrechnung
            data = await frame.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(false);
        }

        var pixels = data.DetachPixelData();
        int width = (int)(swapped ? h : w), height = (int)(swapped ? w : h);
        // Sicherheitsnetz, falls der Decoder anders skaliert hat als verlangt (eine vertauschte Lage verriete die Länge nicht)
        if (pixels.Length != (long)width * height * 4)
            throw new InvalidDataException(string.Format(Strings.Win_Image_UnexpectedPixelCount, name));
        ImageFile.FlattenOnWhite(pixels);
        return new PixelImage(width, height, pixels);
    }

    // ---------- Fehler ----------

    private const int WincodecErrComponentNotFound = unchecked((int)0x88982F50);
    private const int WincodecErrUnknownImageFormat = unchecked((int)0x88982F07);
    private const int WincodecErrComponentInitializeFailure = unchecked((int)0x88982F8B);
    private const int WincodecErrUnsupportedPixelFormat = unchecked((int)0x88982F80);
    private const int WincodecErrUnsupportedOperation = unchecked((int)0x88982F81);
    private const int WincodecErrImageSizeOutOfRange = unchecked((int)0x88982F51);
    private const int MfErrTopoCodecNotFound = unchecked((int)0xC00D5212);
    private const int EOutOfMemory = unchecked((int)0x8007000E);

    /// <summary>Übersetzt Fehler der Bildverarbeitung in eine deutsche, handlungsleitende Meldung.</summary>
    internal static Exception TranslateError(Exception ex, string name)
    {
        string extension = Path.GetExtension(name).ToLowerInvariant();
        int hr = ex.HResult;
        if (ex is OutOfMemoryException || hr == EOutOfMemory || hr == WincodecErrImageSizeOutOfRange)
            return new InvalidDataException(string.Format(Strings.Win_Image_TooLarge, name), ex);
        if (hr is WincodecErrComponentNotFound or WincodecErrUnknownImageFormat or WincodecErrComponentInitializeFailure
            or MfErrTopoCodecNotFound or WincodecErrUnsupportedPixelFormat or WincodecErrUnsupportedOperation)
        {
            if (NeedsExtension.Contains(extension))
                return new InvalidDataException(
                    string.Format(Strings.Win_Image_CodecMissing, name, extension.TrimStart('.').ToUpperInvariant()), ex);
            return new InvalidDataException(string.Format(Strings.Win_Image_Unsupported, name), ex);
        }
        if ((uint)hr >> 16 == 0x8898)
            return new InvalidDataException(string.Format(Strings.Win_Image_Corrupt, name), ex);
        if (ex is IOException io)
            return new IOException(string.Format(Strings.Win_Image_ReadFailed, name, io.Message), ex);
        return new InvalidDataException(string.Format(Strings.Win_Image_NotReadable, name, ex.Message.Trim()), ex);
    }

    // ---------- Endungen ----------

    private static IReadOnlyList<string> DetectExtensions()
    {
        try
        {
            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var codec in BitmapDecoder.GetDecoderInformationEnumerator())
                foreach (var ext in codec.FileExtensions)
                    installed.Add(ext);
            var list = Candidates.Where(installed.Contains).ToList();
            return list.Count > 0 ? list.AsReadOnly() : BuiltIn.AsReadOnly();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine($"Bild-Decoder nicht abfragbar: {ex.Message}");
            return BuiltIn.AsReadOnly();
        }
    }
}
