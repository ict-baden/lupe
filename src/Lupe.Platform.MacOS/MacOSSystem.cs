using System.Diagnostics;
using System.Security;
using System.Text;
using Lupe.Core.Platform;

namespace Lupe.Platform.MacOS;

/// <summary>Gemeinsames für die macOS-Bausteine.</summary>
internal static class MacOS
{
    /// <summary>Meldung, wenn die macOS-Anbindung auf einem anderen System benutzt wird (z. B. in Tests unter Windows).</summary>
    public static string NotHere(string what) => $"{what} ist hier nicht verfügbar (nur unter macOS).";
}

/// <summary>
/// Wo Lupe unter macOS seine Dateien ablegt: <c>~/Library/Application Support/Lupe</c>. Einstellungen und Verlauf
/// (<c>SettingsStore.DefaultPath</c>, <c>HistoryStore.DefaultPath</c>) landen schon dort, weil .NET ab Version 8 unter
/// macOS <c>SpecialFolder.LocalApplicationData</c> auf <c>~/Library/Application Support</c> abbildet.
/// </summary>
public static class MacOSPaths
{
    public static string Home =>
        Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } home ? home
        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);

    /// <summary><c>~/Library/Application Support/Lupe</c>.</summary>
    public static string ApplicationSupport => Path.Combine(Home, "Library", "Application Support", "Lupe");

    /// <summary><c>~/Library/LaunchAgents</c>.</summary>
    public static string LaunchAgents => Path.Combine(Home, "Library", "LaunchAgents");
}

/// <summary>
/// „Mit dem System starten“ über einen LaunchAgent: <c>~/Library/LaunchAgents/de.ict-baden.lupe.plist</c> mit
/// <c>RunAtLoad</c>; startet Lupe bei der Anmeldung mit <c>--hintergrund</c>. launchd liest die Datei bei der nächsten
/// Anmeldung – Lupe ruft <c>launchctl</c> bewusst nicht auf (sonst startete es sofort ein zweites Mal).
/// </summary>
public sealed class MacOSAutostart : IAutostart
{
    public const string Label = "de.ict-baden.lupe";

    private readonly string _agentsDir;
    private readonly Func<string?> _executable;
    private readonly bool _supported;

    public MacOSAutostart() : this(null, null, OperatingSystem.IsMacOS()) { }

    /// <param name="agentsDir">Ordner der LaunchAgents (Tests); null = <see cref="MacOSPaths.LaunchAgents"/>.</param>
    /// <param name="executable">Programmdatei (Tests); null = <see cref="Environment.ProcessPath"/>.</param>
    internal MacOSAutostart(string? agentsDir, Func<string?>? executable, bool supported)
    {
        _agentsDir = agentsDir ?? MacOSPaths.LaunchAgents;
        _executable = executable ?? (() => Environment.ProcessPath);
        _supported = supported;
    }

    public string PlistPath => Path.Combine(_agentsDir, Label + ".plist");

    public bool IsSupported => _supported;

    public bool IsEnabled => _supported && File.Exists(PlistPath);

    public string? Set(bool enabled)
    {
        if (!_supported) return enabled ? MacOS.NotHere("Autostart") : null;
        try
        {
            if (!enabled)
            {
                if (File.Exists(PlistPath)) File.Delete(PlistPath);
                return null;
            }
            var exe = _executable();
            if (string.IsNullOrWhiteSpace(exe) || IsDotnetHost(exe))
                return "Autostart lässt sich nur für das veröffentlichte Lupe einrichten, nicht beim Start über „dotnet“.";
            Directory.CreateDirectory(_agentsDir);
            var temp = PlistPath + ".tmp";
            File.WriteAllText(temp, BuildPlist(exe), new UTF8Encoding(false));
            File.Move(temp, PlistPath, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Lupe: LaunchAgent nicht schreibbar: {ex}");
            return $"Autostart konnte nicht {(enabled ? "eingerichtet" : "entfernt")} werden: {ex.Message}";
        }
    }

    private static bool IsDotnetHost(string exe) =>
        Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

    /// <summary>Der Inhalt der plist-Datei (XML, wie <c>plutil</c> sie erwartet).</summary>
    internal static string BuildPlist(string executable)
    {
        string Esc(string s) => SecurityElement.Escape(s);
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key>
              <string>{Esc(Label)}</string>
              <key>ProgramArguments</key>
              <array>
                <string>{Esc(executable)}</string>
                <string>--hintergrund</string>
              </array>
              <key>RunAtLoad</key>
              <true/>
              <key>ProcessType</key>
              <string>Interactive</string>
            </dict>
            </plist>

            """;
    }
}
