using Lupe.Core.Model;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Lupe.Core.Decoding;

/// <summary>
/// Lädt Bilddateien (PNG, JPEG, BMP, GIF, TIFF, ICO, JPEG XR und – wenn der Codec installiert ist – WebP, HEIC/AVIF,
/// JPEG XL) über Windows.Graphics.Imaging als <see cref="PixelImage"/>.
/// <list type="bullet">
/// <item>EXIF-Drehung wird angewendet, Farben werden nach sRGB umgerechnet.</item>
/// <item>Transparenz wird auf weißem Grund verrechnet (schwarzer Code auf transparentem PNG bleibt lesbar); das
/// Ergebnis ist immer undurchsichtig.</item>
/// <item>GIF und TIFF: das erste Bild; ICO: das größte.</item>
/// <item>Sehr große Bilder werden auf höchstens 40 Megapixel verkleinert (Schutz vor Speicherüberlauf).</item>
/// </list>
/// Fehler kommen als Ausnahme mit deutscher, anzeigbarer Meldung: <see cref="FileNotFoundException"/>,
/// <see cref="UnauthorizedAccessException"/>, <see cref="InvalidDataException"/> (unbekanntes Format, beschädigt, leer)
/// oder <see cref="IOException"/>; Abbruch als <see cref="OperationCanceledException"/>.
/// </summary>
public static class ImageFile
{
    /// <summary>Größte geladene Bildfläche (40 Megapixel ≈ 160 MB im Speicher); größere Bilder werden verkleinert.</summary>
    internal const int MaxPixels = 40_000_000;

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

    public static Task<PixelImage> LoadAsync(string path, CancellationToken ct = default) => LoadAsync(path, MaxPixels, ct);

    /// <summary>Dateiendungen, die im Öffnen-Dialog angeboten werden (z. B. ".png"), kleingeschrieben.</summary>
    public static IReadOnlyList<string> Extensions => s_extensions.Value;

    internal static async Task<PixelImage> LoadAsync(string path, int maxPixels, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Es wurde keine Datei angegeben.", nameof(path));
        ct.ThrowIfCancellationRequested();
        string name = DisplayName(path);

        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new FileNotFoundException($"Die Datei „{name}“ wurde nicht gefunden.", path, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException($"Kein Zugriff auf „{name}“ – fehlen Leserechte, oder ist es ein Ordner?", ex);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new FileNotFoundException($"„{path}“ ist kein gültiger Dateipfad.", path, ex);
        }
        catch (IOException ex)
        {
            throw new IOException($"„{name}“ konnte nicht geöffnet werden: {ex.Message}", ex);
        }

        await using (stream)
        {
            if (stream.Length == 0) throw new InvalidDataException($"Die Datei „{name}“ ist leer.");
            return await LoadAsync(stream, name, maxPixels, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Lädt ein Bild aus einem Datenstrom (z. B. PNG aus der Zwischenablage); <paramref name="name"/> nur für Meldungen.</summary>
    internal static async Task<PixelImage> LoadAsync(Stream stream, string name, int maxPixels, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPixels);
        ct.ThrowIfCancellationRequested();
        try
        {
            using IRandomAccessStream ras = stream.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(ras).AsTask(ct).ConfigureAwait(false);
            var frame = await PickFrameAsync(decoder, ct).ConfigureAwait(false);
            return await ReadPixelsAsync(frame, maxPixels, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not (InvalidDataException or OperationCanceledException))
        {
            throw Translate(ex, name);
        }
    }

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

    private static async Task<PixelImage> ReadPixelsAsync(BitmapFrame frame, int maxPixels, CancellationToken ct)
    {
        uint pw = frame.PixelWidth, ph = frame.PixelHeight;
        if (pw == 0 || ph == 0) throw new InvalidDataException("Das Bild hat keine Pixel.");
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
        if (pixels.Length != (long)width * height * 4)
        {
            // Sicherheitsnetz, falls der Decoder anders gedreht/skaliert hat als erwartet
            if (pixels.Length == (long)height * width * 4 && width != height) (width, height) = (height, width);
            else throw new InvalidDataException("Der Decoder hat unerwartet viele Pixel geliefert.");
        }
        FlattenOnWhite(pixels);
        return new PixelImage(width, height, pixels);
    }

    /// <summary>
    /// Verrechnet Transparenz (nicht vormultipliziert) auf weißem Grund und setzt Alpha auf 255. Ist der Alphakanal
    /// überall 0 (kommt bei manchen 32-Bit-BMPs vor), gilt das Bild als undurchsichtig.
    /// </summary>
    internal static void FlattenOnWhite(byte[] bgra)
    {
        bool anyVisible = false;
        for (int i = 3; i < bgra.Length; i += 4)
            if (bgra[i] != 0) { anyVisible = true; break; }
        for (int i = 0; i < bgra.Length; i += 4)
        {
            int a = anyVisible ? bgra[i + 3] : 255;
            if (a != 255)
            {
                int white = 255 * (255 - a) + 127;
                bgra[i] = (byte)((bgra[i] * a + white) / 255);
                bgra[i + 1] = (byte)((bgra[i + 1] * a + white) / 255);
                bgra[i + 2] = (byte)((bgra[i + 2] * a + white) / 255);
            }
            bgra[i + 3] = 255;
        }
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
    internal static Exception Translate(Exception ex, string name)
    {
        string extension = Path.GetExtension(name).ToLowerInvariant();
        int hr = ex.HResult;
        if (ex is OutOfMemoryException || hr == EOutOfMemory || hr == WincodecErrImageSizeOutOfRange)
            return new InvalidDataException($"„{name}“ ist zu groß, um geladen zu werden.", ex);
        if (hr is WincodecErrComponentNotFound or WincodecErrUnknownImageFormat or WincodecErrComponentInitializeFailure
            or MfErrTopoCodecNotFound or WincodecErrUnsupportedPixelFormat or WincodecErrUnsupportedOperation)
        {
            if (NeedsExtension.Contains(extension))
                return new InvalidDataException(
                    $"„{name}“ kann nicht gelesen werden: Für {extension.TrimStart('.').ToUpperInvariant()} fehlt Windows der passende Codec " +
                    "(Bilderweiterung aus dem Microsoft Store, z. B. „HEIF-Bilderweiterungen“ oder „Webp-Bilderweiterungen“).", ex);
            return new InvalidDataException($"„{name}“ ist kein unterstütztes Bildformat (PNG, JPEG, BMP, GIF, TIFF …) oder beschädigt.", ex);
        }
        if ((uint)hr >> 16 == 0x8898)
            return new InvalidDataException($"„{name}“ ist beschädigt oder kein gültiges Bild.", ex);
        if (ex is IOException io)
            return new IOException($"„{name}“ konnte nicht gelesen werden: {io.Message}", ex);
        return new InvalidDataException($"„{name}“ konnte nicht als Bild gelesen werden ({ex.Message.Trim()}).", ex);
    }

    private static string DisplayName(string path)
    {
        try
        {
            var file = Path.GetFileName(path);
            return string.IsNullOrEmpty(file) ? path : file;
        }
        catch (ArgumentException)
        {
            return path;
        }
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
