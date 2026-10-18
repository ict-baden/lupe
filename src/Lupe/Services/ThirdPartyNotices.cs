using System.Reflection;
using Lupe.Core.Resources;

namespace Lupe.Services;

/// <summary>
/// Lizenztexte der mitgelieferten Bibliotheken (zxing-cpp mit libzint und libzueci, C#/WinRT, .NET): die Datei
/// THIRD-PARTY-NOTICES.txt neben Lupe.exe (ZIP, MSI), sonst die in Lupe eingebettete Fassung – die einzelne Lupe.exe
/// zum Herunterladen hat keine Dateien daneben.
/// </summary>
internal static class ThirdPartyNotices
{
    public const string FileName = "THIRD-PARTY-NOTICES.txt";

    /// <summary>Der Text (eingebettet), null, wenn er fehlt.</summary>
    public static string? Text()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(FileName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Öffnet die Lizenztexte im Standard-Texteditor. Fehlermeldung oder null.</summary>
    public static string? Open()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, FileName);
            if (!File.Exists(path))
            {
                var text = Text();
                if (text is null) return Strings.App_Notices_Missing;
                var folder = Path.Combine(Path.GetTempPath(), "Lupe");
                Directory.CreateDirectory(folder);
                path = Path.Combine(folder, FileName);
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
