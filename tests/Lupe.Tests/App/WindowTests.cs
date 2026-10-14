using System.Diagnostics;

namespace Lupe.Tests.App;

/// <summary>
/// Die Oberfläche als echter Prozess: <c>Lupe.exe --demo</c> (schreibt nichts auf die Platte, keine Einzelinstanz, kein
/// Infobereich) – schließen beendet sie. Läuft auch auf einem unsichtbaren Desktop (Dienst-Sitzung): Fenster werden
/// über ihre Nachrichten bedient, nicht über Mausklicks.
/// </summary>
public sealed class WindowTests
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    [AppCoreFact]
    public void Schliessen_beendet_die_Demo()
    {
        using var process = StartDemo();
        try
        {
            var window = WaitForWindow(process, "Lupe – Demo");
            Assert.True(Native.HasVisibleStyle(window), "Das Demo-Fenster sollte sichtbar sein.");

            Assert.True(Native.PostMessage(window, Native.WM_CLOSE, 0, 0));
            Assert.True(process.WaitForExit(TimeSpan.FromSeconds(15)),
                "Lupe lief nach dem Schließen des Fensters weiter (ohne Infobereich muss Schließen beenden).");
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            Kill(process);
        }
    }

    [AppCoreFact]
    public void Zweimal_schliessen_laesst_keinen_unsichtbaren_Prozess_zurueck()
    {
        // Früher: Das erste Schließen scheiterte still (Close() während Closing), das zweite schloss nur das Fenster –
        // Lupe lief unsichtbar weiter. Hier kommen beide Nachrichten direkt hintereinander.
        using var process = StartDemo();
        try
        {
            var window = WaitForWindow(process, "Lupe – Demo");
            Native.PostMessage(window, Native.WM_CLOSE, 0, 0);
            Native.PostMessage(window, Native.WM_CLOSE, 0, 0);
            Assert.True(process.WaitForExit(TimeSpan.FromSeconds(15)), "Lupe lief nach dem Schließen unsichtbar weiter.");
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            Kill(process);
        }
    }

    private static Process StartDemo()
    {
        var exe = AppExe.Path ?? throw new InvalidOperationException("Lupe.exe nicht gefunden.");
        var info = new ProcessStartInfo(exe) { UseShellExecute = false };
        info.ArgumentList.Add("--demo");
        return Process.Start(info)!;
    }

    private static nint WaitForWindow(Process process, string title)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < StartTimeout)
        {
            if (process.HasExited) throw new InvalidOperationException($"Lupe.exe --demo endete beim Start mit {process.ExitCode}.");
            var hwnd = Native.FindWindow(process.Id, title);
            if (hwnd != 0 && Native.HasVisibleStyle(hwnd)) return hwnd;
            Thread.Sleep(100);
        }
        throw new TimeoutException($"Kein Fenster „{title}“ nach {StartTimeout.TotalSeconds:0} s.");
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
