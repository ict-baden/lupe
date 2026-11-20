// SPDX-License-Identifier: MIT
using System.Reflection;
using Lupe.Core.Resources;

namespace Lupe.Services;

/// <summary>
/// Lizenztexte: THIRD-PARTY-NOTICES.txt (alle mitgelieferten Bibliotheken, siehe dort) und Lupes eigene LICENSE.txt – die
/// Datei neben dem Programm (ZIP, MSI, Linux- und macOS-Pakete), sonst die in Lupe eingebettete Fassung: Die einzelne
/// Lupe.exe zum Herunterladen hat keine Dateien daneben.
/// </summary>
internal static class ThirdPartyNotices
{
    public const string FileName = "THIRD-PARTY-NOTICES.txt";
    public const string LicenseFileName = "LICENSE.txt";

    /// <summary>Der Text (eingebettet), null, wenn er fehlt.</summary>
    public static string? Text(string fileName = FileName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(fileName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Ordner für die eingebetteten Texte: einer je Prozess, neu angelegt mit zufälligem Namen (unter Linux und macOS nur
    /// für den Benutzer, 0700) – nicht unter festem Namen im gemeinsamen /tmp, wo ein anderer Benutzer vorher einen
    /// Symlink anlegen könnte, dessen Ziel Lupe dann mit dem Lizenztext überschriebe. Wird beim Beenden entfernt.
    /// </summary>
    private static readonly Lazy<DirectoryInfo> s_folder = new(() =>
    {
        var folder = Directory.CreateTempSubdirectory("lupe-");
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { folder.Delete(recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        };
        return folder;
    });

    /// <summary>Ordner, in den <see cref="Open"/> eingebettete Texte schreibt (Tests).</summary>
    internal static string TempFolder => s_folder.Value.FullName;

    /// <summary>Öffnet einen der Lizenztexte im Standard-Texteditor. Fehlermeldung oder null.</summary>
    public static string? Open(string fileName = FileName)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (!File.Exists(path))
            {
                var text = Text(fileName);
                if (text is null) return Strings.App_Notices_Missing;
                path = Path.Combine(s_folder.Value.FullName, fileName);
                if (!File.Exists(path))
                {
                    using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    using var writer = new StreamWriter(stream);
                    writer.Write(text);
                }
            }
            Shell.OpenFile(path);
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return string.Format(Strings.App_Notices_OpenFailed, ex.Message);
        }
    }
}
