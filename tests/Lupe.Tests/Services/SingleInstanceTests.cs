// SPDX-License-Identifier: MIT
using Lupe.Services;

namespace Lupe.Tests.Services;

/// <summary>Nur eine Lupe je Sitzung: Sperre und Signal an die erste Instanz.</summary>
public sealed class SingleInstanceTests
{
    [Fact]
    public async Task Zweiter_Start_erreicht_die_erste_Instanz()
    {
        // Unter Windows nicht: Die Prozess-Tests (App/) starten dort echte Lupen, die an einer hier gehaltenen Sperre
        // als „zweiter Start“ enden würden. Linux und macOS: der Socket im Laufzeitordner des Benutzers
        if (OperatingSystem.IsWindows()) return;
        using var first = SingleInstance.TryAcquire();
        if (first is null) return; // auf diesem Rechner läuft gerade eine echte Lupe
        Assert.Null(SingleInstance.TryAcquire());

        var scanned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Listen(onShow: () => { }, onScan: () => scanned.TrySetResult());
        // Der Server braucht einen Moment, bis er lauscht – das Signal wird wiederholt
        for (int i = 0; i < 50 && !scanned.Task.IsCompleted; i++)
        {
            SingleInstance.SignalFirst(scan: true);
            await Task.WhenAny(scanned.Task, Task.Delay(100));
        }
        Assert.True(scanned.Task.IsCompleted, "Signal kam nicht an");
    }

    [Fact]
    public async Task Ein_Fehler_bei_einem_Signal_beendet_das_Lauschen_nicht()
    {
        if (OperatingSystem.IsWindows()) return; // siehe oben
        using var first = SingleInstance.TryAcquire();
        if (first is null) return; // auf diesem Rechner läuft gerade eine echte Lupe

        int shows = 0;
        var scanned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Listen(
            onShow: () =>
            {
                Interlocked.Increment(ref shows);
                throw new InvalidOperationException("Fehler beim Zeigen des Fensters");
            },
            onScan: () => scanned.TrySetResult());
        for (int i = 0; i < 50 && Volatile.Read(ref shows) == 0; i++)
        {
            SingleInstance.SignalFirst(scan: false);
            await Task.Delay(100);
        }
        Assert.True(Volatile.Read(ref shows) > 0, "Signal kam nicht an");

        // Danach lauscht die erste Instanz weiter
        for (int i = 0; i < 80 && !scanned.Task.IsCompleted; i++)
        {
            SingleInstance.SignalFirst(scan: true);
            await Task.WhenAny(scanned.Task, Task.Delay(100));
        }
        Assert.True(scanned.Task.IsCompleted, "Nach dem Fehler kam kein Signal mehr an");
    }

    [Fact]
    public void Socket_liegt_nicht_unter_festem_Namen_im_gemeinsamen_Temp_Ordner()
    {
        if (OperatingSystem.IsWindows()) return; // Windows: benannte Ereignisse je Sitzung
        var path = SingleInstance.UnixSocketPath;
        Assert.True(Path.IsPathRooted(path));
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime && Directory.Exists(runtime))
            Assert.StartsWith(runtime, path);
    }
}
