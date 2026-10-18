using System.Text;
using Lupe.Core.Platform;
using Lupe.Core.Resources;
using Lupe.Platform.Linux.Camera;
using Lupe.Platform.Linux.Input;
using Lupe.Platform.Linux.Native;

namespace Lupe.Platform.Linux;

/// <summary>„Mit dem System starten“ über ~/.config/autostart/lupe.desktop (XDG-Autostart, startet nur im Infobereich).</summary>
public sealed class LinuxAutostart : IAutostart
{
    /// <summary>Für Tests: Pfad des Programms (null = der laufende Prozess).</summary>
    internal Func<string?> ExecutablePath { get; init; } = DefaultExecutable;

    internal static string FilePath => Path.Combine(LinuxSession.ConfigHome, "autostart", "lupe.desktop");

    public bool IsSupported => true;

    public bool IsEnabled
    {
        get
        {
            try
            {
                if (!File.Exists(FilePath)) return false;
                // Hidden=true heißt laut Spezifikation „gelöscht“
                return !File.ReadLines(FilePath).Any(l => l.Trim().Equals("Hidden=true", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public string? Set(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                return null;
            }
            var command = ExecutablePath();
            if (string.IsNullOrEmpty(command)) return Strings.Linux_Autostart_PathUnknown;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, DesktopEntry(command), new UTF8Encoding(false));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Format(enabled ? Strings.Linux_Autostart_EnableFailed : Strings.Linux_Autostart_DisableFailed, ex.Message);
        }
    }

    /// <summary>Inhalt der .desktop-Datei; <paramref name="command"/> ist schon zitiert (Programm und ggf. DLL).</summary>
    internal static string DesktopEntry(string command) =>
        "[Desktop Entry]\n" +
        "Type=Application\n" +
        "Name=Lupe\n" +
        $"Comment={Strings.Linux_Autostart_Comment}\n" +
        $"Exec={command} --hintergrund\n" +
        "Icon=lupe\n" +
        "Terminal=false\n" +
        "X-GNOME-Autostart-enabled=true\n";

    /// <summary>Zitiert ein Argument für Exec= (Desktop-Entry-Spezifikation: in "…" mit \ vor " ` $ \).</summary>
    internal static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.All(c => char.IsAsciiLetterOrDigit(c) || "/._-+".Contains(c))) return arg.Replace("%", "%%");
        var sb = new StringBuilder("\"");
        foreach (char c in arg)
        {
            if (c is '"' or '`' or '$' or '\\') sb.Append('\\');
            sb.Append(c == '%' ? "%%" : c.ToString());
        }
        return sb.Append('"').ToString();
    }

    private static string? DefaultExecutable()
    {
        // AppImage: der Pfad der .AppImage-Datei, nicht der des ausgepackten Programms
        if (Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage) return Quote(appImage);
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return null;
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.Ordinal))
        {
            // „dotnet Lupe.dll“: beides eintragen
            var dll = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            return string.IsNullOrEmpty(dll) ? null : $"{Quote(exe)} {Quote(dll)}";
        }
        return Quote(exe);
    }
}

/// <summary>Bekannte Hindernisse unter Linux: keine Sitzung, Wayland ohne Portale, keine Kamerarechte, kein XTest.</summary>
public sealed class LinuxProblems : IPlatformProblems
{
    public IReadOnlyList<PlatformProblem> Check()
    {
        var list = new List<PlatformProblem>();
        CheckCamera(list);
        switch (LinuxSession.Kind)
        {
            case SessionKind.None:
                list.Add(new(PlatformFeature.Screen, Strings.Linux_Problem_NoSession));
                break;
            case SessionKind.X11:
                nint display = X11.TryOpen(out string? error);
                if (display == 0)
                {
                    list.Add(new(PlatformFeature.Screen, string.Format(Strings.Linux_Problem_X11Unavailable, error)));
                    break;
                }
                try
                {
                    if (!X11.HasXTest(display))
                        list.Add(new(PlatformFeature.Typing, Strings.Linux_Problem_NoXTest));
                }
                finally
                {
                    X11.XCloseDisplay(display);
                }
                break;
            case SessionKind.Wayland:
                if (string.IsNullOrWhiteSpace(Tmds.DBus.Protocol.DBusAddress.Session))
                    list.Add(new(PlatformFeature.Screen, Strings.Linux_Problem_NoSessionBus));
                list.Add(new(PlatformFeature.ActiveWindow, Strings.Linux_Problem_WaylandActiveWindow));
                break;
        }
        return list;
    }

    /// <summary>Kameras da, aber keine lesbar: meist fehlt die Gruppe „video“.</summary>
    private static void CheckCamera(List<PlatformProblem> list)
    {
        if (!OperatingSystem.IsLinux()) return;
        try
        {
            var devices = Directory.GetFiles("/dev", "video*");
            if (devices.Length == 0) return;
            bool anyAccess = devices.Any(d => Libc2.access(d, Libc2.R_OK | Libc2.W_OK) == 0);
            if (!anyAccess)
                list.Add(new(PlatformFeature.Camera, Strings.Linux_Problem_NoCameraAccess));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

internal static class Libc2
{
    public const int R_OK = 4, W_OK = 2;
    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    public static extern int access(string path, int mode);
}
