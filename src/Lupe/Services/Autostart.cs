using Microsoft.Win32;

namespace Lupe.Services;

/// <summary>„Mit Windows starten“ – pro Benutzer über HKCU\…\Run, startet Lupe nur im Infobereich.</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Lupe";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return false;
            }
        }
    }

    /// <summary>Ein- oder ausschalten; Fehlermeldung auf Deutsch oder null.</summary>
    public static string? Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                // Über „dotnet Lupe.dll“ gestartet, wäre der Prozess dotnet.exe – das startete beim Anmelden nicht Lupe
                if (string.IsNullOrEmpty(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    return "Der Pfad von Lupe.exe ist nicht bekannt – Autostart nicht eingerichtet.";
                key.SetValue(ValueName, $"\"{exe}\" --hintergrund");
            }
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return $"Autostart ließ sich nicht {(enabled ? "einrichten" : "entfernen")}: {ex.Message}";
        }
    }
}
