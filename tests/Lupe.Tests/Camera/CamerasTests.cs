using System.Collections.Concurrent;
using Lupe.Core.Camera;
using Windows.Devices.Enumeration;
using static Lupe.Tests.Camera.CameraTestSupport;

namespace Lupe.Tests.Camera;

/// <summary>
/// <see cref="Cameras"/> gegen das echte Windows (Media Foundation), aber ohne eine Kamera zu belegen: Auflisten und
/// Fehlerwege beim Öffnen.
/// </summary>
public sealed class CamerasTests
{
    [Fact]
    public async Task Auflisten_stürzt_nicht_ab_und_passt_zu_Windows()
    {
        var cameras = await Cameras.ListAsync();

        Assert.NotNull(cameras);
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        if (devices.Count == 0)
        {
            // Keine Kamera → leere Liste, kein Fehler
            Assert.Empty(cameras);
        }
        else
        {
            Assert.InRange(cameras.Count, 0, devices.Count);
        }
        Assert.All(cameras, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Id));
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
        });
        Assert.Equal(cameras.Count, cameras.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(cameras.Count, cameras.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public async Task Auflisten_mehrfach_und_parallel()
    {
        var lists = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => Cameras.ListAsync())));
        Assert.All(lists, l => Assert.Equal(lists[0], l));
    }

    [Fact]
    public async Task Auflisten_abgebrochen_wirft_OperationCanceledException()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Cameras.ListAsync(new CancellationToken(canceled: true)));
    }

    [Fact]
    public void Öffnen_belegt_noch_nichts_und_prüft_das_Argument()
    {
        Assert.Throws<ArgumentNullException>(() => Cameras.Open(null!));
        var source = Cameras.Open(new CameraInfo("irgendwas", "  Meine   Kamera "));
        Assert.Equal("Meine Kamera", source.Name);
        Assert.Equal("Kamera", Cameras.Open(new CameraInfo("x", "")).Name);
    }

    [Fact]
    public async Task Unbekannte_Kamera_meldet_sich_über_Failed_statt_mit_Ausnahme()
    {
        await using var source = Cameras.Open(new CameraInfo("gibt-es-nicht", "Phantom-Kamera"));
        var failures = new ConcurrentQueue<string>();
        int frames = 0;
        source.Failed += (_, msg) => failures.Enqueue(msg);
        source.FrameArrived += (_, _) => Interlocked.Increment(ref frames);

        await WithTimeout(source.StartAsync(), 30_000, "Start der unbekannten Kamera");

        var message = Assert.Single(failures);
        Assert.Contains("„Phantom-Kamera“", message);
        Assert.Contains("nicht gefunden", message);
        Assert.Equal(0, frames);
        Assert.Null(((MediaCaptureFrameSource)source).ActiveFormat);

        // wiederholbar, Stopp und Freigabe schaden nicht
        await source.StartAsync();
        Assert.Equal(2, failures.Count);
        await source.StopAsync();
        await source.StopAsync();
    }

    [Fact]
    public async Task Unbekannte_Geräte_Kennung_in_der_Form_eines_echten_Geräts()
    {
        const string id = @"\\?\USB#VID_0000&PID_0000#0#{e5323777-f976-4f5b-9b55-b94699c46e44}\GLOBAL";
        await using var source = Cameras.Open(new CameraInfo(id, "USB-Kamera"));
        var failures = new ConcurrentQueue<string>();
        source.Failed += (_, msg) => failures.Enqueue(msg);

        await WithTimeout(source.StartAsync(), 30_000);

        Assert.Contains("„USB-Kamera“", Assert.Single(failures));
    }

    [Fact]
    public async Task ScanLoop_mit_unbekannter_Kamera_meldet_den_Fehler_weiter()
    {
        await using var source = Cameras.Open(new CameraInfo("gibt-es-nicht", "Phantom-Kamera"));
        await using var loop = new ScanLoop(source, _ => []);
        var failures = new ConcurrentQueue<string>();
        loop.Failed += (_, msg) => failures.Enqueue(msg);

        await WithTimeout(loop.StartAsync(), 30_000);

        Assert.Contains("nicht gefunden", Assert.Single(failures));
        Assert.False(loop.IsRunning);
    }

    [Fact]
    public async Task ScanLoop_mit_unbekannter_Kamera_synchroner_Neustart_aus_Failed_verklemmt_nicht()
    {
        await using var source = Cameras.Open(new CameraInfo("gibt-es-nicht", "Phantom-Kamera"));
        await using var loop = new ScanLoop(source, _ => []);
        var failures = new ConcurrentQueue<string>();
        loop.Failed += (_, msg) =>
        {
            failures.Enqueue(msg);
            if (failures.Count == 1) loop.StartAsync().Wait(); // „gleich noch einmal versuchen“ – synchron
        };

        await WithTimeout(Task.Run(() => loop.StartAsync()), 30_000, "Start mit synchronem Neustart im Failed-Ereignis");

        Assert.Equal(2, failures.Count);
        Assert.All(failures, msg => Assert.Contains("nicht gefunden", msg));
        Assert.False(loop.IsRunning);
    }

    [Fact]
    public async Task Abbruch_beim_Start_wirft_OperationCanceledException()
    {
        await using var source = Cameras.Open(new CameraInfo("gibt-es-nicht", "Phantom"));
        var failures = new ConcurrentQueue<string>();
        source.Failed += (_, msg) => failures.Enqueue(msg);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.StartAsync(new CancellationToken(canceled: true)));
        Assert.Empty(failures);
    }

    [Fact]
    public async Task Nach_Freigabe_kein_Start()
    {
        var source = Cameras.Open(new CameraInfo("gibt-es-nicht", "Phantom"));
        await source.DisposeAsync();
        await source.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => source.StartAsync());
    }
}
