// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using Lupe.Core.Camera;
using Lupe.Core.Model;
using Xunit.Abstractions;
using static Lupe.Tests.Camera.CameraTestSupport;

using Lupe.Platform.Windows.Camera;

namespace Lupe.Tests.Camera;

/// <summary>
/// Tests mit einer echten Kamera (auch virtuelle wie DroidCam/OBS zählen). Ohne Kamera übersprungen; alle nacheinander,
/// weil sich zwei exklusive Zugriffe gegenseitig verdrängen.
/// </summary>
[Collection(RealCameraCollection.Name)]
public sealed class RealCameraTests(ITestOutputHelper output)
{
    private const int FrameTimeoutMs = 20_000;

    private static async Task<CameraInfo> FirstCamera()
    {
        var cameras = await Cameras.ListAsync();
        Assert.NotEmpty(cameras);
        return cameras[0];
    }

    private sealed class Collector
    {
        public readonly ConcurrentQueue<PixelImage> Frames = new();
        public readonly ConcurrentQueue<string> Failures = new();
        public int Count => Frames.Count;

        public Collector(IFrameSource source, int keep = 5)
        {
            source.FrameArrived += (_, img) =>
            {
                Frames.Enqueue(img);
                while (Frames.Count > keep && Frames.TryDequeue(out PixelImage? _)) { } // nur die letzten behalten (Speicher)
                Interlocked.Increment(ref Total);
            };
            source.Failed += (_, msg) => Failures.Enqueue(msg);
        }

        public int Total;
    }

    [CameraFact]
    public async Task Liste_enthält_die_Kamera()
    {
        var cameras = await Cameras.ListAsync();
        foreach (var c in cameras) output.WriteLine($"{c.Name} – {c.Id}");
        Assert.NotEmpty(cameras);
    }

    [CameraFact]
    public async Task Liefert_undurchsichtige_Bilder_höchstens_720p_Stopp_und_Neustart()
    {
        var camera = await FirstCamera();
        await using var source = Cameras.Open(camera);
        var c = new Collector(source);

        await source.StartAsync();
        Assert.Empty(c.Failures);
        var format = ((MediaCaptureFrameSource)source).ActiveFormat;
        Assert.NotNull(format);
        output.WriteLine($"Format: {format}");
        Assert.True(await WaitUntil(() => Volatile.Read(ref c.Total) >= 5, FrameTimeoutMs), "keine Bilder von der Kamera");

        foreach (var frame in c.Frames)
        {
            Assert.InRange(frame.Width, 2, 1280);
            Assert.InRange(frame.Height, 2, 1280);
            Assert.True(Math.Min(frame.Width, frame.Height) <= 720);
            Assert.Equal(frame.Width * 4, frame.Stride);
            Assert.Equal(format!.OutputWidth, frame.Width);
            Assert.Equal(format.OutputHeight, frame.Height);
            for (int i = 3; i < frame.Bgra.Length; i += 4 * 97) Assert.Equal(255, frame.Bgra[i]);
        }

        await source.StopAsync();
        Assert.Null(((MediaCaptureFrameSource)source).ActiveFormat);
        int after = Volatile.Read(ref c.Total);
        await Task.Delay(300);
        Assert.Equal(after, Volatile.Read(ref c.Total));

        // Neustart: Kamera wurde vollständig freigegeben und lässt sich wieder öffnen
        await source.StartAsync();
        Assert.True(await WaitUntil(() => Volatile.Read(ref c.Total) >= after + 3, FrameTimeoutMs), "nach Neustart keine Bilder");
        await source.StopAsync();
        Assert.Empty(c.Failures);
    }

    [CameraFact]
    public async Task Leere_Kennung_öffnet_die_erste_Kamera()
    {
        await using var source = Cameras.Open(new CameraInfo("", ""));
        var c = new Collector(source);
        await source.StartAsync();
        Assert.Empty(c.Failures);
        Assert.True(await WaitUntil(() => Volatile.Read(ref c.Total) >= 2, FrameTimeoutMs));
    }

    [CameraFact]
    public async Task Zweiter_Zugriff_verdrängt_oder_teilt_mit_verständlicher_Meldung()
    {
        var camera = await FirstCamera();
        await using var first = Cameras.Open(camera);
        await using var second = Cameras.Open(camera);
        var a = new Collector(first);
        var b = new Collector(second);

        await first.StartAsync();
        Assert.True(await WaitUntil(() => Volatile.Read(ref a.Total) >= 2, FrameTimeoutMs));
        await second.StartAsync();
        await Task.Delay(1500);
        int aBefore = Volatile.Read(ref a.Total), bBefore = Volatile.Read(ref b.Total);
        await Task.Delay(1500);

        output.WriteLine($"erste: {a.Total} Bilder, Meldungen: {string.Join(" | ", a.Failures)}");
        output.WriteLine($"zweite: {b.Total} Bilder, Meldungen: {string.Join(" | ", b.Failures)}; Format {((MediaCaptureFrameSource)second).ActiveFormat}");
        // Windows entscheidet: die zweite verdrängt die erste (meist), oder sie teilen sich die Kamera, oder die zweite
        // bekommt sie nicht. In jedem Fall: keine Ausnahme, deutsche Meldung mit Kameranamen, und eine liefert Bilder.
        foreach (var msg in a.Failures.Concat(b.Failures))
        {
            Assert.Contains($"„{camera.Name}“", msg);
            Assert.Matches("anderen Programm|übernommen|beendet|benutzt", msg);
        }
        Assert.True(a.Failures.Count <= 1 && b.Failures.Count <= 1, "jeder Ausfall genau einmal");
        bool firstRuns = Volatile.Read(ref a.Total) > aBefore, secondRuns = Volatile.Read(ref b.Total) > bBefore;
        Assert.True(firstRuns || secondRuns, "keine der beiden liefert Bilder");
        if (!a.Failures.IsEmpty) Assert.False(firstRuns, "nach dem Ausfall keine Bilder mehr");

        // Nach dem Ausfall lässt sich die erste wieder starten (sie verdrängt dann ihrerseits)
        await second.StopAsync();
        await first.StopAsync();
        int again = Volatile.Read(ref a.Total);
        await first.StartAsync();
        Assert.True(await WaitUntil(() => Volatile.Read(ref a.Total) > again, FrameTimeoutMs));
    }

    [CameraFact]
    public async Task Stopp_aus_dem_Bild_Ereignis_heraus_verklemmt_nicht_und_gibt_frei()
    {
        var camera = await FirstCamera();
        await using var source = Cameras.Open(camera);
        int frames = 0;
        source.FrameArrived += (_, _) =>
        {
            if (Interlocked.Increment(ref frames) == 3) source.StopAsync().Wait();
        };
        await source.StartAsync();
        Assert.True(await WaitUntil(() => Volatile.Read(ref frames) >= 3, FrameTimeoutMs));
        await Task.Delay(500);
        Assert.Equal(3, Volatile.Read(ref frames));
        Assert.Null(((MediaCaptureFrameSource)source).ActiveFormat);

        await WithTimeout(source.StartAsync(), 20_000, "Neustart");
        Assert.True(await WaitUntil(() => Volatile.Read(ref frames) > 3, FrameTimeoutMs));
        await WithTimeout(source.StopAsync(), 20_000, "Stopp");
    }

    [CameraFact]
    public async Task Start_Stopp_Gewitter_und_Freigabe_mittendrin()
    {
        var camera = await FirstCamera();
        var source = Cameras.Open(camera);
        var c = new Collector(source);
        var tasks = Enumerable.Range(0, 24).Select(i => Task.Run(() => i % 2 == 0 ? source.StartAsync() : source.StopAsync()));
        await WithTimeout(Task.WhenAll(tasks), 60_000, "Start/Stopp-Gewitter");
        Assert.Empty(c.Failures);

        await source.StartAsync();
        Assert.True(await WaitUntil(() => Volatile.Read(ref c.Total) > 0, FrameTimeoutMs));
        var start = source.StartAsync();
        await source.DisposeAsync();
        await start;
        int after = Volatile.Read(ref c.Total);
        await Task.Delay(300);
        Assert.Equal(after, Volatile.Read(ref c.Total));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => source.StartAsync());
    }

    [CameraFact]
    public async Task ScanLoop_mit_echter_Kamera()
    {
        var camera = await FirstCamera();
        await using var source = Cameras.Open(camera);
        int decodes = 0, frames = 0;
        await using var loop = new ScanLoop(source, img =>
        {
            Interlocked.Increment(ref decodes);
            return ZxingDecode(img);
        });
        var failures = new ConcurrentQueue<string>();
        loop.Frame += (_, _) => Interlocked.Increment(ref frames);
        loop.Failed += (_, msg) => failures.Enqueue(msg);

        await loop.StartAsync();
        Assert.True(await WaitUntil(() => Volatile.Read(ref frames) >= 10 && Volatile.Read(ref decodes) >= 3, FrameTimeoutMs));
        await loop.StopAsync();

        Assert.Empty(failures);
        Assert.True(Volatile.Read(ref decodes) <= Volatile.Read(ref frames));
        output.WriteLine($"{frames} Bilder, {decodes} Erkennungen");
    }

    [CameraFact]
    public async Task Start_und_Stopp_vom_Oberflaechen_Thread_wie_in_der_App()
    {
        // Die App startet und stoppt die Kamera vom WPF-Thread (STA) aus, z. B. beim Wechsel auf „Verlauf“. Dabei stürzte
        // Lupe mit 0xC0000005 in WinRT ab (MediaCapture.remove_Failed). Die übrigen Tests starten von Hintergrund-Threads.
        var camera = await FirstCamera();
        using var ui = new Screen.UiThread(perMonitorDpi: false);
        for (int round = 0; round < 3; round++)
        {
            var source = ui.Invoke(() => Cameras.Open(camera));
            int frames = 0;
            source.FrameArrived += (_, _) => Interlocked.Increment(ref frames);
            await WithTimeout(ui.Invoke(() => source.StartAsync()), 20_000, "Start");
            Assert.True(await WaitUntil(() => Volatile.Read(ref frames) >= 3, FrameTimeoutMs), "keine Bilder von der Kamera");
            await WithTimeout(ui.Invoke(() => source.StopAsync()), 20_000, "Stopp");
            await WithTimeout(ui.Invoke(() => source.DisposeAsync().AsTask()), 20_000, "Freigabe");
            output.WriteLine($"Runde {round + 1}: {frames} Bilder");
        }
    }
}
