namespace Lupe.Core.Settings;

/// <summary>
/// Schreibt Dateien atomar: erst vollständig in eine Temp-Datei im selben Ordner, auf die Platte geschrieben,
/// dann per Umbenennen (MoveFileEx mit REPLACE_EXISTING, auf NTFS atomar) an die Zielstelle. Ein Absturz oder
/// voller Datenträger hinterlässt damit nie eine halbe Datei – entweder steht der alte oder der neue Inhalt da.
/// Kurze Sperren durch Virenscanner oder Indexdienst werden mit wenigen Wiederholungen abgefangen.
/// </summary>
internal static class AtomicFile
{
    /// <summary>Wartezeiten zwischen den Versuchen (ms); insgesamt höchstens eine Viertelsekunde.</summary>
    private static readonly int[] RetryDelaysMs = [15, 30, 60, 120];

    /// <summary>Schreibt <paramref name="content"/> atomar nach <paramref name="path"/> und legt den Ordner bei Bedarf an.</summary>
    public static void Write(string path, byte[] content) => Write(path, stream => stream.Write(content));

    /// <summary>
    /// Wie <see cref="Write(string, byte[])"/>, der Inhalt kommt aus <paramref name="writeContent"/>. Wirft der Rückruf,
    /// bleibt die Zieldatei unverändert und die Temp-Datei wird entfernt.
    /// </summary>
    public static void Write(string path, Action<Stream> writeContent)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full) ?? throw new IOException($"„{full}“ hat keinen Ordner.");
        Directory.CreateDirectory(dir);

        // Eindeutiger Name im selben Ordner (gleiches Volume → Umbenennen statt Kopieren)
        var tmp = Path.Combine(dir, $"{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024))
            {
                writeContent(stream);
                // Erst auf die Platte, dann umbenennen – sonst kann nach einem Stromausfall eine leere Datei übrig bleiben
                stream.Flush(flushToDisk: true);
            }
            WithRetries(() => File.Move(tmp, full, overwrite: true));
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    /// <summary>Liest eine Datei ganz; Leser und Schreiber anderer Prozesse werden nicht ausgesperrt. Fehlt sie, <see cref="FileNotFoundException"/>.</summary>
    public static byte[] ReadAllBytes(string path) => WithRetries(() =>
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        if (length > MaxReadBytes) throw new IOException($"Die Datei ist mit {length / (1024 * 1024)} MB ungewöhnlich groß.");
        var buffer = new byte[length];
        stream.ReadExactly(buffer);
        return buffer;
    });

    /// <summary>Obergrenze beim Lesen – schützt vor einer riesigen (fremden) Datei, die den Speicher füllt.</summary>
    internal const long MaxReadBytes = 256L * 1024 * 1024;

    private static void WithRetries(Action action) => WithRetries(() => { action(); return true; });

    private static T WithRetries<T>(Func<T> action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (attempt < RetryDelaysMs.Length && IsTransient(ex))
            {
                Thread.Sleep(RetryDelaysMs[attempt]);
            }
        }
    }

    /// <summary>Fehler, die nach kurzer Zeit verschwinden können (Sperre durch ein anderes Programm).</summary>
    private static bool IsTransient(Exception ex) => ex switch
    {
        FileNotFoundException or DirectoryNotFoundException or PathTooLongException or DriveNotFoundException => false,
        // Zugriff verweigert kommt beim Ersetzen auch, wenn ein anderes Programm die Zieldatei gerade offen hat
        UnauthorizedAccessException => true,
        IOException io => FileProblems.IsSharingViolation(io),
        _ => false,
    };

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (FileProblems.IsExpected(ex))
        {
            // Eine liegen gebliebene Temp-Datei stört nicht; sie wird nie gelesen
        }
    }
}

/// <summary>Einordnung und deutsche Beschreibung typischer Dateifehler.</summary>
internal static class FileProblems
{
    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorLockViolation = unchecked((int)0x80070021);
    private const int ErrorHandleDiskFull = unchecked((int)0x80070027);
    private const int ErrorDiskFull = unchecked((int)0x80070070);
    private const int ErrorWriteProtect = unchecked((int)0x80070013);
    private const int ErrorNotReady = unchecked((int)0x80070015);
    private const int ErrorBadNetPath = unchecked((int)0x80070035);
    private const int ErrorBadNetName = unchecked((int)0x80070043);
    private const int ErrorInvalidName = unchecked((int)0x8007007B);

    /// <summary>Fehler, mit denen man beim Lesen/Schreiben von Dateien rechnen muss (keine Programmierfehler).</summary>
    public static bool IsExpected(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException;

    public static bool IsSharingViolation(IOException ex) => ex.HResult is ErrorSharingViolation or ErrorLockViolation;

    /// <summary>Kurze deutsche Beschreibung, z. B. „Zugriff verweigert“.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        FileNotFoundException => "Datei nicht gefunden",
        DirectoryNotFoundException => "Ordner nicht gefunden",
        DriveNotFoundException => "Laufwerk nicht gefunden",
        PathTooLongException => "Pfad zu lang",
        UnauthorizedAccessException =>
            "Zugriff verweigert – keine Berechtigung, Datei schreibgeschützt oder in einem anderen Programm geöffnet",
        IOException io when IsSharingViolation(io) => "Die Datei ist in einem anderen Programm geöffnet",
        IOException io when io.HResult is ErrorDiskFull or ErrorHandleDiskFull => "Kein Platz mehr auf dem Datenträger",
        // Typisch beim Export auf USB-Sticks und Netzlaufwerke
        IOException io when io.HResult is ErrorWriteProtect => "Der Datenträger ist schreibgeschützt",
        IOException io when io.HResult is ErrorNotReady => "Das Laufwerk ist nicht bereit (z. B. USB-Stick abgezogen)",
        IOException io when io.HResult is ErrorBadNetPath or ErrorBadNetName => "Netzwerkpfad nicht erreichbar",
        IOException io when io.HResult is ErrorInvalidName => "Ungültiger Datei- oder Ordnername",
        EndOfStreamException => "Die Datei wurde beim Lesen kürzer – vermutlich schreibt gerade ein anderes Programm",
        NotSupportedException => "Ungültiger Pfad",
        // Seltene Fälle: die Meldung des Systems (kann englisch sein) ist besser als gar keine
        _ => ex.Message,
    };
}
