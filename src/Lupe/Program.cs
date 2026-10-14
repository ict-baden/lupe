using Lupe.Cli;
using Lupe.Services;

namespace Lupe;

/// <summary>
/// Einstieg. Mit Befehl (lesen, --hilfe) läuft Lupe als Befehlszeilenprogramm ganz ohne WPF, sonst startet die
/// Oberfläche – normal, nur im Infobereich (Autostart), mit Demo-Daten oder zum Erzeugen der Screenshots.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = StartOptions.Parse(args);
        if (options.Mode == AppMode.CommandLine) return CommandLine.Run(args);

        // Nur eine Lupe je Sitzung (Tastenkürzel und Infobereich gibt es einmal). Demo und Screenshots laufen daneben.
        SingleInstance? instance = null;
        if (options.Mode is AppMode.Normal or AppMode.Background)
        {
            instance = SingleInstance.TryAcquire();
            if (instance is null)
            {
                // Läuft schon: das vorhandene Fenster nach vorn holen – beim Autostart nicht, da will niemand ein Fenster
                if (options.Mode == AppMode.Normal) SingleInstance.SignalFirst();
                return 0;
            }
        }

        try
        {
            var app = new App(options, instance);
            app.InitializeComponent();
            return app.Run();
        }
        finally
        {
            instance?.Dispose();
        }
    }
}

internal enum AppMode { Normal, Background, Demo, Screenshots, CommandLine }

internal sealed record StartOptions(AppMode Mode, string ScreenshotFolder = "", string Theme = "beide")
{
    public static StartOptions Parse(string[] args)
    {
        if (args.Length == 0) return new(AppMode.Normal);
        var first = args[0].Trim();
        if (args.Length == 1 && first.Equals("--demo", StringComparison.OrdinalIgnoreCase)) return new(AppMode.Demo);
        if (args.Length == 1 && first.Equals("--hintergrund", StringComparison.OrdinalIgnoreCase)) return new(AppMode.Background);

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
