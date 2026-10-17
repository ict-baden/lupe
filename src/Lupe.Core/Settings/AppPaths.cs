namespace Lupe.Core.Settings;

/// <summary>
/// Wo Lupe pro Benutzer speichert. Windows: %LocalAppData%\Lupe für alles (wie bisher). Linux: Einstellungen unter
/// $XDG_CONFIG_HOME/lupe (~/.config/lupe), Verlauf unter $XDG_DATA_HOME/lupe (~/.local/share/lupe). macOS:
/// ~/Library/Application Support/Lupe.
/// </summary>
public static class AppPaths
{
    /// <summary>Verzeichnis der Einstellungen.</summary>
    public static string ConfigDirectory => Resolve(config: true, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), Environment.GetEnvironmentVariable);

    /// <summary>Verzeichnis für Daten wie den Verlauf.</summary>
    public static string DataDirectory => Resolve(config: false, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), Environment.GetEnvironmentVariable);

    internal static string Resolve(bool config, bool windows, bool macOS, Func<string, string?> env)
    {
        if (windows)
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "Lupe");
        string home = env("HOME") is { Length: > 0 } h ? h
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
        if (macOS) return Path.Combine(home, "Library", "Application Support", "Lupe");
        // XDG: nur absolute Angaben gelten, sonst der Standard
        string? xdg = env(config ? "XDG_CONFIG_HOME" : "XDG_DATA_HOME");
        string baseDir = !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg) ? xdg
            : config ? Path.Combine(home, ".config") : Path.Combine(home, ".local", "share");
        return Path.Combine(baseDir, "lupe");
    }
}
