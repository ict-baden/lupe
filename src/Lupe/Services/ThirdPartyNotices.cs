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
                var folder = Path.Combine(Path.GetTempPath(), "Lupe");
                Directory.CreateDirectory(folder);
                path = Path.Combine(folder, fileName);
                File.WriteAllText(path, text);
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
