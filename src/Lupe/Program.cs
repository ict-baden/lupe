using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Lupe.Cli;
using Lupe.Core.Localization;
using Lupe.Core.Platform;
using Lupe.Core.Resources;
using Lupe.Core.Settings;
using Lupe.Services;

namespace Lupe;

/// <summary>
/// Einstieg. Mit Befehl (lesen, --hilfe) läuft Lupe als Befehlszeilenprogramm ganz ohne Oberfläche, sonst startet
/// Avalonia – normal, nur im Infobereich (Autostart), mit Demo-Daten oder (ohne Bildschirm) zum Erzeugen der Screenshots.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Systemfunktionen (Kamera, Bildschirm, Tippen, Tastenkürzel …) über die Anbindung des laufenden Systems
        LupePlatform.Use(CreatePlatform());
        // Sprache vor dem ersten Text: eingestellt oder wie das System (Richtlinie 13)
        UiLanguage.Apply(SettingsStore.Load().Language);
        var options = StartOptions.Parse(args);
        if (options.Mode == AppMode.CommandLine) return CommandLine.Run(args);
        if (options.Mode == AppMode.Screenshots) return RunScreenshots(options);

        // Nur eine Lupe je Sitzung (Tastenkürzel und Infobereich gibt es einmal). Demo und Screenshots laufen daneben.
        SingleInstance? instance = null;
        if (options.Mode is AppMode.Normal or AppMode.Background)
        {
            instance = SingleInstance.TryAcquire();
            if (instance is null)
            {
                // Läuft schon: das vorhandene Fenster nach vorn holen (oder dort scannen) – beim Autostart nicht, da will
                // niemand ein Fenster
                if (options.ScanOnStart) SingleInstance.SignalFirst(scan: true);
                else if (options.Mode == AppMode.Normal) SingleInstance.SignalFirst(scan: false);
                return 0;
            }
        }

        try
        {
            return BuildApp(options, instance).UsePlatformDetect().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex) when (OperatingSystem.IsLinux() && ex.Message.Contains("XOpenDisplay", StringComparison.Ordinal))
        {
            // Avalonia zeichnet unter Linux über X11 – unter Wayland also über XWayland. Ohne X-Server kein Fenster:
            // verständlich melden statt mit einem Stacktrace abzustürzen
            ConsoleSession.Attach();
            Console.Error.WriteLine(NoDisplayMessage(Environment.GetEnvironmentVariable("DISPLAY"), Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")));
            return CommandLine.ExitError;
        }
        finally
        {
            instance?.Dispose();
        }
    }

    /// <summary>Meldung, wenn Linux keinen X-Server erreicht (Wayland ohne XWayland, SSH ohne Weiterleitung).</summary>
    internal static string NoDisplayMessage(string? display, string? waylandDisplay)
    {
        var where = string.IsNullOrEmpty(display) ? Strings.App_NoDisplay_NotSet : string.Format(Strings.App_NoDisplay_Unreachable, display);
        var hint = string.IsNullOrEmpty(waylandDisplay) ? Strings.App_NoDisplay_HintSession : Strings.App_NoDisplay_HintWayland;
        return string.Format(Strings.App_NoDisplay_Message, where, hint);
    }

    /// <summary>Die Anbindung für dieses System; die Windows-Ausgabe bringt Lupe.Platform.Windows mit, die andere Linux und macOS.</summary>
    internal static IPlatform CreatePlatform()
    {
#if LUPE_WINDOWS
        if (OperatingSystem.IsWindows()) return new Lupe.Platform.Windows.WindowsPlatform();
#else
        if (OperatingSystem.IsLinux()) return new Lupe.Platform.Linux.LinuxPlatform();
        if (OperatingSystem.IsMacOS()) return new Lupe.Platform.MacOS.MacOSPlatform();
#endif
        return new UnsupportedPlatform(OperatingSystem.IsWindows() ? Strings.App_Platform_WindowsWithoutSupport : Strings.App_Platform_ThisSystem);
    }

    private static AppBuilder BuildApp(StartOptions options, SingleInstance? instance) =>
        AppBuilder.Configure(() => new App(options, instance))
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// <c>--screenshots</c>: ohne Bildschirm (Avalonia.Headless mit Skia) – geht auch in einer Dienst-Sitzung und unter
    /// Linux ohne X-Server. Gezeichnet wird genauso wie im Fenster.
    /// </summary>
    private static int RunScreenshots(StartOptions options)
    {
        ConsoleSession.Attach();
        BuildApp(options, null)
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        int code = CommandLine.ExitError;
        using var done = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                code = await UI.Screenshots.RunAsync(options.ScreenshotFolder, options.Theme);
            }
            catch (Exception ex)
            {
                // Bewusst nicht verschluckt: Fehler sichtbar melden und mit 3 enden
                Console.Error.WriteLine(string.Format(Strings.App_Screenshots_Failed, ex));
                code = CommandLine.ExitError;
            }
            finally
            {
                done.Cancel();
            }
        });
        Dispatcher.UIThread.MainLoop(done.Token);
        return code;
    }
}

internal enum AppMode { Normal, Background, Demo, Screenshots, CommandLine }

/// <param name="ScanOnStart"><c>--bildschirm</c>: gleich einen Ausschnitt scannen – für ein eigenes Tastenkürzel des Desktops,
/// wo Programme keine globalen Kürzel anmelden dürfen (Wayland ohne GlobalShortcuts-Portal).</param>
internal sealed record StartOptions(AppMode Mode, string ScreenshotFolder = "", string Theme = "beide", bool ScanOnStart = false)
{
    public static StartOptions Parse(string[] args)
    {
        if (args.Length == 0) return new(AppMode.Normal);
        var first = args[0].Trim();
        if (args.Length == 1 && first.Equals("--demo", StringComparison.OrdinalIgnoreCase)) return new(AppMode.Demo);
        if (args.Length == 1 && first.Equals("--hintergrund", StringComparison.OrdinalIgnoreCase)) return new(AppMode.Background);
        if (args.Length == 1 && first.Equals("--bildschirm", StringComparison.OrdinalIgnoreCase)) return new(AppMode.Background, ScanOnStart: true);

        if (first.Equals("--screenshots", StringComparison.OrdinalIgnoreCase))
        {
            // Falsche Angaben erklärt die Befehlszeile (sie hat eine Konsole für die Meldung)
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal)) return new(AppMode.CommandLine);
            var theme = "beide";
            if (args.Length == 4 && args[2].Equals("--theme", StringComparison.OrdinalIgnoreCase)) theme = args[3].Trim().ToLowerInvariant();
            else if (args.Length != 2) return new(AppMode.CommandLine);
            if (theme is not ("hell" or "dunkel" or "beide")) return new(AppMode.CommandLine);
            try { return new(AppMode.Screenshots, Path.GetFullPath(args[1]), theme); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return new(AppMode.CommandLine); }
        }
        return new(AppMode.CommandLine);
    }
}
