using Lupe.Core.Model;
using Lupe.Core.Platform;
using Lupe.Core.Resources;

namespace Lupe.Core.Decoding;

/// <summary>
/// Lädt Bilddateien als <see cref="PixelImage"/> – Datei- und Stromverwaltung, Prüfung auf abgeschnittene Dateien und
/// Meldungen hier, das Dekodieren über <see cref="IPlatform.Images"/> der aktiven Plattform (Windows:
/// Windows.Graphics.Imaging mit PNG, JPEG, BMP, GIF, TIFF, ICO, JPEG XR und – wenn der Codec installiert ist – WebP,
/// HEIC/AVIF, JPEG XL).
/// <list type="bullet">
/// <item>EXIF-Drehung wird angewendet, Farben werden nach sRGB umgerechnet.</item>
/// <item>Transparenz wird auf weißem Grund verrechnet (schwarzer Code auf transparentem PNG bleibt lesbar); das
/// Ergebnis ist immer undurchsichtig.</item>
/// <item>GIF und TIFF: das erste Bild; ICO: das größte.</item>
/// <item>Abgeschnittene Dateien (PNG, JPEG, GIF – Windows füllte den Rest sonst stillschweigend auf) gelten als beschädigt.</item>
/// <item>Sehr große Bilder werden auf höchstens 40 Megapixel verkleinert (Schutz vor Speicherüberlauf).</item>
/// </list>
/// Fehler kommen als Ausnahme mit deutscher, anzeigbarer Meldung: <see cref="FileNotFoundException"/>,
/// <see cref="UnauthorizedAccessException"/>, <see cref="InvalidDataException"/> (unbekanntes Format, beschädigt, leer)
/// oder <see cref="IOException"/>; Abbruch als <see cref="OperationCanceledException"/>.
/// </summary>
public static class ImageFile
{
    /// <summary>Größte geladene Bildfläche (40 Megapixel ≈ 160 MB im Speicher); größere Bilder werden verkleinert.</summary>
    public const int MaxPixels = 40_000_000;

    public static Task<PixelImage> LoadAsync(string path, CancellationToken ct = default) => LoadAsync(path, MaxPixels, ct);

    /// <summary>
    /// Lädt ein Bild aus einem Datenstrom – z. B. die PNG-Daten, die Browser und Snipping Tool in die Zwischenablage legen.
    /// Gleiche Behandlung und Fehler wie beim Laden einer Datei (Drehung, Transparenz auf Weiß, Größenschutz, deutsche
    /// Meldungen). Gelesen wird ab der aktuellen Position; der Datenstrom bleibt offen und gehört weiter dem Aufrufer.
    /// Nicht durchsuchbare Ströme (Netz, Pipe) werden erst in den Speicher gelesen.
    /// </summary>
    public static Task<PixelImage> LoadAsync(Stream stream, CancellationToken ct = default) => LoadAsync(stream, null, ct);

    /// <summary>Wie <see cref="LoadAsync(Stream, CancellationToken)"/>; <paramref name="name"/> erscheint in Fehlermeldungen (null = „Bild“).</summary>
    public static async Task<PixelImage> LoadAsync(Stream stream, string? name, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead) throw new ArgumentException("Der Datenstrom ist nicht lesbar.", nameof(stream));
        string display = string.IsNullOrWhiteSpace(name) ? Strings.Core_Image_DefaultName : name;
        if (stream.CanSeek)
        {
            if (stream.Length - stream.Position <= 0) throw new InvalidDataException(string.Format(Strings.Core_Image_Empty, display));
            return await LoadAsync(new KeepOpen(stream, stream.Position), display, MaxPixels, ct).ConfigureAwait(false);
        }

        using var copy = new MemoryStream();
        var buffer = new byte[1 << 16];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (copy.Length + read > MaxStreamBytes) throw new InvalidDataException(string.Format(Strings.Core_Image_TooLarge, display));
            copy.Write(buffer, 0, read);
        }
        if (copy.Length == 0) throw new InvalidDataException(string.Format(Strings.Core_Image_Empty, display));
        copy.Position = 0;
        return await LoadAsync(copy, display, MaxPixels, ct).ConfigureAwait(false);
    }

    /// <summary>Obergrenze für nicht durchsuchbare Datenströme, die erst in den Speicher gelesen werden.</summary>
    internal const long MaxStreamBytes = 512L * 1024 * 1024;

    /// <summary>Dateiendungen, die im Öffnen-Dialog angeboten werden (z. B. ".png"), kleingeschrieben.</summary>
    public static IReadOnlyList<string> Extensions => LupePlatform.Current.Images.Extensions;

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
            throw new FileNotFoundException(string.Format(Strings.Core_Image_FileNotFound, name), path, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException(string.Format(Strings.Core_Image_AccessDenied, name), ex);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new FileNotFoundException(string.Format(Strings.Core_Image_InvalidPath, path), path, ex);
        }
        catch (IOException ex) when (ex.HResult is ErrorSharingViolation or ErrorLockViolation)
        {
            throw new IOException(string.Format(Strings.Core_Image_InUse, name), ex);
        }
        catch (IOException ex)
        {
            throw new IOException(string.Format(Strings.Core_Image_OpenError, name, ex.Message), ex);
        }

        await using (stream)
        {
            // Geräte und Pipes (CON, \\.\pipe\…) sind keine Bilddateien – und Length würde dort scheitern
            if (!stream.CanSeek) throw new InvalidDataException(string.Format(Strings.Core_Image_NotAnImageFile, name));
            if (stream.Length == 0) throw new InvalidDataException(string.Format(Strings.Core_Image_FileEmpty, name));
            return await LoadAsync(stream, name, maxPixels, ct).ConfigureAwait(false);
        }
    }

    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorLockViolation = unchecked((int)0x80070021);

    /// <summary>Lädt ein Bild aus einem Datenstrom (z. B. PNG aus der Zwischenablage); <paramref name="name"/> nur für Meldungen.</summary>
    internal static async Task<PixelImage> LoadAsync(Stream stream, string name, int maxPixels, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPixels);
        ct.ThrowIfCancellationRequested();
        var decoder = LupePlatform.Current.Images;
        try
        {
            // Abgeschnittene PNG/JPEG/GIF lesen die Windows-Decoder klaglos (Rest grau oder weiß) – vorher selbst prüfen
            if (await Task.Run(() => ImageIntegrity.Truncation(stream), ct).ConfigureAwait(false) is { } reason)
                throw new InvalidDataException(string.Format(Strings.Core_Image_Incomplete, name, reason));
            return await decoder.DecodeAsync(stream, name, maxPixels, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not (InvalidDataException or OperationCanceledException))
        {
            throw decoder.Translate(ex, name);
        }
    }

    /// <summary>
    /// Verrechnet Transparenz (nicht vormultipliziert) auf weißem Grund und setzt Alpha auf 255. Ist der Alphakanal
    /// überall 0 (kommt bei manchen 32-Bit-BMPs vor), gilt das Bild als undurchsichtig.
    /// </summary>
    public static void FlattenOnWhite(byte[] bgra)
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

    /// <summary>
    /// Zeigt den Datenstrom des Aufrufers ab <paramref name="origin"/> (seine Position beim Aufruf) – und schließt ihn nie:
    /// Manche Decoder-Adapter (Windows.Graphics.Imaging) schließen beim Freigeben sonst auch den darunterliegenden Strom.
    /// </summary>
    private sealed class KeepOpen(Stream inner, long origin) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => inner.Length - origin;
        public override long Position { get => inner.Position - origin; set => inner.Position = origin + value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin seekOrigin) => seekOrigin switch
        {
            SeekOrigin.Begin => inner.Seek(origin + offset, SeekOrigin.Begin) - origin,
            _ => inner.Seek(offset, seekOrigin) - origin,
        };
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
}
