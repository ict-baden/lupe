// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using System.Diagnostics;
using Lupe.Core.Camera;
using Xunit.Abstractions;
using static Lupe.Tests.Camera.CameraTestSupport;

using Lupe.Platform.Windows.Camera;

namespace Lupe.Tests.Camera;

/// <summary>
/// Synchroner Neustart aus den Ereignissen heraus mit echter Kamera. Früher wartete die Freigabe der alten Sitzung
/// (unter der Sperre der Quelle) bis zu 5 s auf genau das Bild-Ereignis, das den Neustart auslöste.
/// </summary>
[Collection(RealCameraCollection.Name)]
public sealed class RealCameraReentrancyTests(ITestOutputHelper output)
{
    /// <summary>Ein Neustart darf nicht auf das Zeitlimit der Bild-Abwicklung (5 s) laufen.</summary>
    private const int MaxRestartMs = 4000;

    [CameraFact]
    public async Task Synchroner_Neustart_der_Quelle_aus_dem_Bild_Ereignis()
    {
        var camera = (await Cameras.ListAsync())[0];
        await using var source = Cameras.Open(camera);
        int frames = 0;
        long restartMs = -1;
        var failures = new ConcurrentQueue<string>();
        source.Failed += (_, m) => failures.Enqueue(m);
        source.FrameArrived += (_, _) =>
        {
            if (Interlocked.Increment(ref frames) != 3) return;
            var sw = Stopwatch.StartNew();
            source.StopAsync().Wait();
            source.StartAsync().Wait();
            Interlocked.Exchange(ref restartMs, sw.ElapsedMilliseconds);
        };

        await source.StartAsync();
        Assert.True(await WaitUntil(() => Interlocked.Read(ref restartMs) >= 0, 30_000), "Neustart im Bild-Ereignis hängt");
        output.WriteLine($"Neustart im Ereignis: {restartMs} ms");
        Assert.True(await WaitUntil(() => Volatile.Read(ref frames) > 6, 20_000), "nach dem Neustart keine Bilder");
        await WithTimeout(source.StopAsync(), 20_000, "Stopp");

        Assert.Empty(failures);
        Assert.InRange(Interlocked.Read(ref restartMs), 0, MaxRestartMs);
    }

    [CameraFact]
    public async Task Synchroner_Neustart_der_Schleife_aus_dem_Frame_Ereignis()
    {
        var camera = (await Cameras.ListAsync())[0];
        await using var source = Cameras.Open(camera);
        await using var loop = new ScanLoop(source, _ => []);
        int frames = 0;
        long restartMs = -1;
        var failures = new ConcurrentQueue<string>();
        loop.Failed += (_, m) => failures.Enqueue(m);
        loop.Frame += (_, _) =>
        {
            if (Interlocked.Increment(ref frames) != 3) return;
            var sw = Stopwatch.StartNew();
            loop.StopAsync().Wait();
            Thread.Sleep(50); // dem Aufräumer im Hintergrund den Vortritt lassen
            loop.StartAsync().Wait();
            Interlocked.Exchange(ref restartMs, sw.ElapsedMilliseconds);
        };

        await loop.StartAsync();
        Assert.True(await WaitUntil(() => Interlocked.Read(ref restartMs) >= 0, 30_000), "Neustart im Frame-Ereignis hängt");
        output.WriteLine($"Neustart im Ereignis: {restartMs} ms");
        Assert.True(await WaitUntil(() => Volatile.Read(ref frames) > 6, 20_000), "nach dem Neustart keine Bilder");
        Assert.True(loop.IsRunning);
        await WithTimeout(loop.StopAsync(), 20_000, "Stopp");

        Assert.Empty(failures);
        Assert.InRange(Interlocked.Read(ref restartMs), 0, MaxRestartMs);
        Assert.Null(((MediaCaptureFrameSource)source).ActiveFormat);
    }
}
