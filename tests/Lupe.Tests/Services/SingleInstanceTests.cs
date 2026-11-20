// SPDX-License-Identifier: MIT
using Lupe.Services;
using Lupe.Tests.TestSupport;

namespace Lupe.Tests.Services;

/// <summary>
/// Nur eine Lupe je Sitzung: Sperre und Signal an die erste Instanz – unter Windows benannter Mutex und Ereignisse,
/// unter Linux und macOS Sperrdatei und Socket im Laufzeitordner des Benutzers. Jeder Test nimmt einen eigenen
/// Namenszusatz: So stört er weder eine laufende Lupe noch die Prozess-Tests (App/), die echte Lupen starten.
/// </summary>
public sealed class SingleInstanceTests : IDisposable
{
    // xUnit legt je Test eine neue Instanz an – also ein Zusatz je Test
    private readonly string _suffix = "-test-" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>Unix: Sperrdatei und Socket dieses Tests wieder entfernen.</summary>
    public void Dispose()
    {
        if (OperatingSystem.IsWindows()) return; // benannte Objekte verschwinden mit dem letzten Handle
        var folder = Path.GetDirectoryName(SingleInstance.UnixSocketPath)!;
        foreach (var file in Directory.EnumerateFiles(folder, $"lupe-*{_suffix}.*"))
        {
            try { File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void Sperre_gilt_bis_zum_Freigeben()
    {
        var suffix = _suffix;
        var first = SingleInstance.TryAcquire(suffix);
        Assert.NotNull(first);
        Assert.Null(SingleInstance.TryAcquire(suffix));
        first.Dispose();

        using var again = SingleInstance.TryAcquire(suffix);
        Assert.NotNull(again);
    }

    [Fact]
    public async Task Zweiter_Start_erreicht_die_erste_Instanz()
    {
        var suffix = _suffix;
        using var first = SingleInstance.TryAcquire(suffix);
        Assert.NotNull(first);

        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Listen(onShow: () => shown.TrySetResult(), onScan: () => scanned.TrySetResult());

        // Der Server braucht einen Moment, bis er lauscht – das Signal wird wiederholt
        await SignalUntil(scanned.Task, scan: true, suffix);
        Assert.True(scanned.Task.IsCompleted, "Signal zum Scannen kam nicht an");
        Assert.False(shown.Task.IsCompleted, "Scan-Signal hat das Fenster gezeigt");

        await SignalUntil(shown.Task, scan: false, suffix);
        Assert.True(shown.Task.IsCompleted, "Signal zum Zeigen kam nicht an");
    }

    [Fact]
    public async Task Ein_Fehler_bei_einem_Signal_beendet_das_Lauschen_nicht()
    {
        var suffix = _suffix;
        using var first = SingleInstance.TryAcquire(suffix);
        Assert.NotNull(first);

        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Listen(
            onShow: () =>
            {
                shown.TrySetResult();
                throw new InvalidOperationException("Fehler beim Zeigen des Fensters");
            },
            onScan: () => scanned.TrySetResult());
        await SignalUntil(shown.Task, scan: false, suffix);
        Assert.True(shown.Task.IsCompleted, "Signal kam nicht an");

        // Danach lauscht die erste Instanz weiter (Unix: nach einer kurzen Pause)
        await SignalUntil(scanned.Task, scan: true, suffix, attempts: 80);
        Assert.True(scanned.Task.IsCompleted, "Nach dem Fehler kam kein Signal mehr an");
    }

    [Fact]
    public void Signal_ohne_erste_Instanz_wirft_nicht() => SingleInstance.SignalFirst(scan: true, _suffix);

    [NotWindowsFact]
    public void Socket_liegt_nicht_unter_festem_Namen_im_gemeinsamen_Temp_Ordner()
    {
        var path = SingleInstance.UnixSocketPath;
        Assert.True(Path.IsPathRooted(path));
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime && Directory.Exists(runtime))
            Assert.StartsWith(runtime, path);
    }

    private static async Task SignalUntil(Task received, bool scan, string suffix, int attempts = 50)
    {
        for (int i = 0; i < attempts && !received.IsCompleted; i++)
        {
            SingleInstance.SignalFirst(scan, suffix);
            await Task.WhenAny(received, Task.Delay(100));
        }
    }
}
