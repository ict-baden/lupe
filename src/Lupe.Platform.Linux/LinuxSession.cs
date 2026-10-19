// SPDX-License-Identifier: MIT
namespace Lupe.Platform.Linux;

/// <summary>Welche Art von Desktop-Sitzung läuft (X11, Wayland, keine)?</summary>
internal enum SessionKind
{
    /// <summary>Weder DISPLAY noch WAYLAND_DISPLAY – z. B. über SSH oder als Dienst.</summary>
    None,
    X11,
    /// <summary>Wayland; DISPLAY kann trotzdem gesetzt sein (XWayland), reicht aber nur bis zu X11-Programmen.</summary>
    Wayland,
}

/// <summary>Die Sitzung und die XDG-Verzeichnisse, aus der Umgebung gelesen.</summary>
internal static class LinuxSession
{
    /// <summary>Für Tests überschreibbar (null = aus der Umgebung).</summary>
    internal static Func<string, string?> Environment { get; set; } = System.Environment.GetEnvironmentVariable;

    public static SessionKind Kind => Detect(Environment);

    internal static SessionKind Detect(Func<string, string?> env)
    {
        string? type = env("XDG_SESSION_TYPE")?.Trim().ToLowerInvariant();
        bool wayland = !string.IsNullOrWhiteSpace(env("WAYLAND_DISPLAY")) || type == "wayland";
        bool x11 = !string.IsNullOrWhiteSpace(env("DISPLAY"));
        // LUPE_BACKEND=x11 erzwingt X11 trotz Wayland (dann nur über XWayland, also nur für X11-Programme)
        string? forced = env("LUPE_BACKEND")?.Trim().ToLowerInvariant();
        if (forced == "x11" && x11) return SessionKind.X11;
        if (forced == "wayland") return SessionKind.Wayland;
        if (wayland) return SessionKind.Wayland;
        return x11 ? SessionKind.X11 : SessionKind.None;
    }

    /// <summary>Name der Desktop-Umgebung für Meldungen (z. B. „GNOME“, „KDE“), sonst null.</summary>
    public static string? DesktopName
    {
        get
        {
            string? d = Environment("XDG_CURRENT_DESKTOP");
            if (string.IsNullOrWhiteSpace(d)) return null;
            return d.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        }
    }

    /// <summary>$XDG_CONFIG_HOME oder ~/.config (relative Angaben gelten laut Spezifikation nicht).</summary>
    public static string ConfigHome => XdgDir("XDG_CONFIG_HOME", ".config");

    /// <summary>$XDG_DATA_HOME oder ~/.local/share.</summary>
    public static string DataHome => XdgDir("XDG_DATA_HOME", Path.Combine(".local", "share"));

    /// <summary>Lupes Verzeichnis für Einstellungen: ~/.config/lupe.</summary>
    public static string LupeConfigDir => Path.Combine(ConfigHome, "lupe");

    private static string XdgDir(string variable, string fallback)
    {
        string? value = Environment(variable);
        if (!string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value)) return value;
        string home = Environment("HOME") is { Length: > 0 } h ? h
            : System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, fallback);
    }
}
