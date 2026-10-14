using System.Text.Json;
using Microsoft.Win32;

namespace Lupe.Services;

/// <summary>
/// Einstellungen nur der Oberfläche, die (noch) nicht in <see cref="Core.Model.LupeSettings"/> stehen – gespeichert
/// unter %LocalAppData%\Lupe\oberflaeche.json. Tolerant wie der SettingsStore: fehlt die Datei oder ist sie
/// kaputt, gelten die Standardwerte.
/// </summary>
internal sealed record AppPreferences
{
    /// <summary>Beim Schließen des Fensters im Infobereich weiterlaufen (Tastenkürzel bleibt aktiv).</summary>
    public bool KeepRunningInTray { get; init; } = true;

    /// <summary>Der Hinweis „Lupe läuft im Infobereich weiter“ wurde schon einmal gezeigt.</summary>
    public bool TrayHintShown { get; init; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lupe", "oberflaeche.json");

    public static AppPreferences Load(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            if (!File.Exists(file)) return new();
            return JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(file), Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new();
        }
    }

    /// <summary>Atomar speichern (temporäre Datei, dann ersetzen). false bei einem Fehler.</summary>
    public bool Save(string? path = null)
    {
        var file = path ?? DefaultPath;
        var temp = file + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
            File.Move(temp, file, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return false;
        }
    }
}

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
