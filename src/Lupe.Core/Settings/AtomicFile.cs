// SPDX-License-Identifier: MIT
using Lupe.Core.Resources;

namespace Lupe.Core.Settings;

/// <summary>
/// Schreibt Dateien atomar: erst vollständig in eine Temp-Datei im selben Ordner, auf die Platte geschrieben,
/// dann per Umbenennen (MoveFileEx mit REPLACE_EXISTING, auf NTFS atomar) an die Zielstelle. Ein Absturz oder
/// voller Datenträger hinterlässt damit nie eine halbe Datei – entweder steht der alte oder der neue Inhalt da.
/// Kurze Sperren durch Virenscanner oder Indexdienst werden mit wenigen Wiederholungen abgefangen.
/// <para>Unter Linux und macOS nur für den eigenen Benutzer lesbar: neue Ordner 0700, Dateien 0600 – Verlauf und
/// Einstellungen können vertrauliche Inhalte enthalten.</para>
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
        var dir = Path.GetDirectoryName(full) ?? throw new IOException(string.Format(Strings.Core_File_NoFolder, full));
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(dir);
        else if (!Directory.Exists(dir)) Directory.CreateDirectory(dir, OwnerOnlyFolder);

        // Eindeutiger Name im selben Ordner (gleiches Volume → Umbenennen statt Kopieren)
        var tmp = Path.Combine(dir, $"{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 64 * 1024 };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = OwnerOnlyFile;
            using (var stream = new FileStream(tmp, options))
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
        if (length > MaxReadBytes) throw new IOException(string.Format(Strings.Core_File_TooLarge, length / (1024 * 1024)));
        var buffer = new byte[length];
        stream.ReadExactly(buffer);
        return buffer;
    });

    /// <summary>Rechte neuer Ordner unter Linux und macOS: nur der Benutzer (0700).</summary>
    internal const UnixFileMode OwnerOnlyFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>Rechte neuer Dateien unter Linux und macOS: nur der Benutzer (0600).</summary>
    internal const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

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

/// <summary>Einordnung und Beschreibung typischer Dateifehler (in der Sprache der Oberfläche).</summary>
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
        FileNotFoundException => Strings.Core_File_NotFound,
        DirectoryNotFoundException => Strings.Core_File_FolderNotFound,
        DriveNotFoundException => Strings.Core_File_DriveNotFound,
        PathTooLongException => Strings.Core_File_PathTooLong,
        UnauthorizedAccessException => Strings.Core_File_AccessDenied,
        IOException io when IsSharingViolation(io) => Strings.Core_File_OpenElsewhere,
        IOException io when io.HResult is ErrorDiskFull or ErrorHandleDiskFull => Strings.Core_File_DiskFull,
        // Typisch beim Export auf USB-Sticks und Netzlaufwerke
        IOException io when io.HResult is ErrorWriteProtect => Strings.Core_File_WriteProtected,
        IOException io when io.HResult is ErrorNotReady => Strings.Core_File_DriveNotReady,
        IOException io when io.HResult is ErrorBadNetPath or ErrorBadNetName => Strings.Core_File_NetworkPathUnreachable,
        IOException io when io.HResult is ErrorInvalidName => Strings.Core_File_InvalidName,
        EndOfStreamException => Strings.Core_File_Truncated,
        NotSupportedException => Strings.Core_File_InvalidPath,
        // Seltene Fälle: die Meldung des Systems (kann englisch sein) ist besser als gar keine
        _ => ex.Message,
    };
}
