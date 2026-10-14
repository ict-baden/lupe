using Lupe.Core.Model;

namespace Lupe.Tests.Settings;

/// <summary>Eigener, leerer Ordner unter %TEMP% je Test – nie die echten Einstellungen des Benutzers.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Lupe.Tests", "einstellungen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public string[] FileNames() => Directory.GetFiles(Path).Select(System.IO.Path.GetFileName).Order().ToArray()!;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal static class SettingsAssert
{
    /// <summary>Vergleicht alle Felder; Listen nach Inhalt (Record-Gleichheit vergleicht sie nur als Verweis).</summary>
    public static void Equal(LupeSettings expected, LupeSettings actual) => Assert.Equivalent(expected, actual, strict: true);

    /// <summary>Einstellungen, in denen jedes Feld vom Standard abweicht.</summary>
    public static LupeSettings AllDifferent() => new()
    {
        Actions = ResultActions.Kopieren | ResultActions.LinkOeffnen,
        ScreenActions = ResultActions.Eintippen,
        TypeSuffix = "Tab",
        LinkRules =
        [
            new(@"^\d{13}$", "https://example.com/artikel?ean="),
            new(@"(?i)^www\.", "https://", Enabled: false),
            new(@"^https?://\S+$"),
        ],
        Formats = ["QR_CODE", "DATA_MATRIX", "EAN_13"],
        ScreenHotkey = new Hotkey(Ctrl: true, Alt: false, Shift: true, Win: false, Key: 0x78), // Strg+Umschalt+F9
        Sound = false,
        AimHelp = false,
        RepeatSuppressMs = 1500,
        Camera = @"\\?\USB#VID_046D&PID_0825#Grüße",
        KeepHistory = false,
        HistoryLimit = 250,
        KeepRunningInTray = false,
        TrayHintShown = true,
    };
}
