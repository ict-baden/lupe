// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using Lupe.Core.Camera;
using Lupe.Core.Model;
using Lupe.Tests.TestSupport;
using static Lupe.Tests.Camera.CameraTestSupport;

namespace Lupe.Tests.Camera;

/// <summary>
/// Synchrones Starten/Anhalten aus den Ereignissen der Schleife heraus (der schlimmste Fall für Verklemmungen), mit
/// fernsteuerbarer Quelle und mit der Demo-Kamera. Vorher verklemmten sich ein synchroner Neustart aus Failed während
/// des Starts und einer aus dem Frame-Ereignis (Aufräumer hielt die Sperre und wartete auf genau dieses Ereignis).
/// Die Demo-Kamera taktet über den Thread-Pool, daher in der Sammlung „Echtzeit“ (siehe dort). Dort laufen die Tests
/// allein, deshalb wirkt das Zeitlimit von xUnit: Eine Verklemmung – auch beim Aufräumen am Ende (<c>await using</c>) –
/// lässt den Test scheitern, statt den Testlauf anzuhalten.
/// </summary>
[Collection(EchtzeitCollection.Name)]
public sealed class ScanLoopReentrancyTests
{
    private const int TimeoutMs = 30_000;

    [Fact(Timeout = TimeoutMs)]
    public async Task Synchroner_Neustart_aus_Failed_während_des_Starts_verklemmt_nicht()
    {
        var source = new ManualFrameSource { DuringStart = s => s.Fail("Kamera belegt") };
        await using var loop = new ScanLoop(source, _ => []);
        var failed = new ConcurrentQueue<string>();
        int restarts = 0;
        loop.Failed += (_, msg) =>
        {
            failed.Enqueue(msg);
            if (Interlocked.Increment(ref restarts) > 1) return;
            source.DuringStart = null;
            loop.StartAsync().Wait(); // „gleich noch einmal versuchen“ – synchron
        };

        await WithTimeout(Task.Run(() => loop.StartAsync()), 5000, "Start mit synchronem Neustart im Failed-Ereignis");

        Assert.Equal(["Kamera belegt"], failed);
        Assert.True(loop.IsRunning);
        Assert.Equal(2, source.StartCount);
        Assert.Equal(1, source.FrameSubscribers);
    }

    [Fact(Timeout = TimeoutMs)]
    public async Task Synchroner_Neustart_aus_Detected_während_StartAsync_noch_läuft_verklemmt_nicht()
    {
        // So hing der Demo-Kamera-Test unten einmal in der Windows-CI (beide Testziele, je 4 min): Der erste Treffer kam,
        // bevor StartAsync fertig war, und sein Ereignis hielt die Schleife an und startete sie synchron neu. StartAsync
        // räumte danach auf und wartete dabei unter der Sperre auf die Erkennung – deren Ereignis wartete auf die Sperre.
        using var stopped = new ManualResetEventSlim();
        bool stoppedDuringStart = false;
        var source = new ManualFrameSource();
        source.DuringStart = s =>
        {
            s.DuringStart = null;
            s.Push(Marker(1));
            // bis das Ereignis angehalten hat: StartAsync ist dann noch nicht zurück
            stoppedDuringStart = stopped.Wait(5000);
        };
        await using var loop = new ScanLoop(source, _ => [Code("A")], repeatSuppressMs: 0);
        int detected = 0;
        loop.Detected += (_, _) =>
        {
            if (Interlocked.Increment(ref detected) != 1) return;
            loop.StopAsync().Wait();
            stopped.Set();
            loop.StartAsync().Wait();
        };

        // Eigener Thread: Die Barriere in DuringStart belegt ihn (CLAUDE.md, TestSupport/OwnThreads)
        await WithTimeout(OwnThreads.Start(() => loop.StartAsync().Wait()), 5000, "Start, während der erste Treffer synchron neu startet");
        Assert.True(stoppedDuringStart, "Der Treffer kam nicht während des Starts – der Test prüft dann nicht den Fall");
        Assert.True(await WaitUntil(() => loop.IsRunning && source.StartCount == 2, 5000), "kein Neustart");
        // Die erste Erkennung endet erst, wenn ihr Ereignis zurück ist – bis dahin werden Bilder nur angezeigt
        Assert.True(await WaitUntil(() =>
        {
            source.Push(Marker(2));
            return Volatile.Read(ref detected) >= 2;
        }, 5000), "nach dem Neustart keine Treffer");
        Assert.Equal(1, source.FrameSubscribers);
        await WithTimeout(loop.StopAsync(), 5000, "Stopp");
    }

    [Fact(Timeout = TimeoutMs)]
    public async Task StopAsync_wartet_auch_nach_Ausfall_beim_Start_auf_ein_laufendes_Detected()
    {
        // StartAsync räumt nach dem Ausfall ohne Warten auf; das Warten auf den Treffer, der schon unterwegs war, holt
        // StopAsync nach – danach kommt keiner mehr
        using var inDetected = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        bool detectedDuringStart = false;
        var source = new ManualFrameSource();
        source.DuringStart = s =>
        {
            s.DuringStart = null;
            s.Push(Marker(1));
            detectedDuringStart = inDetected.Wait(5000);
            s.Fail("Kamera weg");
        };
        await using var loop = new ScanLoop(source, _ => [Code("A")], repeatSuppressMs: 0);
        loop.Detected += (_, _) =>
        {
            inDetected.Set();
            release.Wait(5000);
        };
        var failed = new ConcurrentQueue<string>();
        loop.Failed += (_, msg) => failed.Enqueue(msg);

        await WithTimeout(OwnThreads.Start(() => loop.StartAsync().Wait()), 5000, "Start mit Ausfall");
        Assert.True(detectedDuringStart, "Der Treffer kam nicht während des Starts – der Test prüft dann nicht den Fall");
        Assert.Equal(["Kamera weg"], failed);
        Assert.False(loop.IsRunning);

        var stop = loop.StopAsync();
        await Task.Delay(200);
        Assert.False(stop.IsCompleted, "StopAsync kehrte zurück, während Detected noch lief");
        release.Set();
        await WithTimeout(stop, 5000, "Stopp");
    }

    [Fact(Timeout = TimeoutMs)]
    public async Task Ausfall_auf_anderem_Thread_während_des_Starts_wird_nach_dem_Aufräumen_gemeldet()
    {
        // Die Quelle meldet den Ausfall während StartAsync von einem anderen Thread (wie Media Foundation)
        var source = new ManualFrameSource { DuringStart = s => Task.Run(() => s.Fail("Kamera übernommen")).Wait() };
        await using var loop = new ScanLoop(source, _ => []);
        var failed = new ConcurrentQueue<string>();
        int subscribersAtFailure = -1;
        loop.Failed += (_, msg) =>
        {
            failed.Enqueue(msg);
            subscribersAtFailure = source.FrameSubscribers;
        };

        await WithTimeout(loop.StartAsync(), 5000, "Start");

        Assert.Equal(["Kamera übernommen"], failed);
        Assert.Equal(0, subscribersAtFailure); // beim Melden schon abgemeldet …
        Assert.Equal(1, source.StopCount);     // … und die Quelle angehalten, ohne Hintergrund-Aufräumer
        Assert.False(loop.IsRunning);
    }

    [Fact(Timeout = TimeoutMs)]
    public async Task Synchroner_Neustart_aus_Failed_bei_Ausfall_der_Demo_Kamera()
    {
        await using var source = new FakeFrameSource([Marker(1)], framesPerSecond: 100);
        await using var loop = new ScanLoop(source, _ => []);
        int frames = 0, restarts = 0;
        var failed = new ConcurrentQueue<string>();
        loop.Frame += (_, _) => Interlocked.Increment(ref frames);
        loop.Failed += (_, msg) =>
        {
            failed.Enqueue(msg);
            if (Interlocked.Increment(ref restarts) != 1) return;
            Thread.Sleep(50);
            loop.StartAsync().Wait();
        };

        await loop.StartAsync();
        Assert.True(await WaitUntil(() => Volatile.Read(ref frames) > 0));
        await WithTimeout(source.SimulateFailureAsync("Kamera kurz weg"), 5000, "Ausfall mit synchronem Neustart");

        int before = Volatile.Read(ref frames);
        Assert.True(await WaitUntil(() => Volatile.Read(ref frames) > before + 3, 5000), "nach dem Neustart keine Bilder");
        Assert.True(loop.IsRunning);
        Assert.Equal(["Kamera kurz weg"], failed);
        await WithTimeout(loop.StopAsync(), 5000, "Stopp");
    }

    [Fact(Timeout = TimeoutMs)]
    public async Task Synchroner_Neustart_aus_dem_Frame_Ereignis_mit_Demo_Kamera_verklemmt_nicht()
    {
        for (int round = 0; round < 5; round++)
        {
            await using var source = new FakeFrameSource([Marker(1), Marker(2), Marker(3)], framesPerSecond: 100);
            await using var loop = new ScanLoop(source, _ => []);
            int frames = 0;
            var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            loop.Frame += (_, _) =>
            {
                if (Interlocked.Increment(ref frames) != 3) return;
                loop.StopAsync().Wait();
                Thread.Sleep(50); // dem Aufräumer im Hintergrund Zeit geben, zuerst dran zu sein
                loop.StartAsync().Wait();
                restarted.TrySetResult();
            };

            await loop.StartAsync();
            await WithTimeout(restarted.Task, 5000, "Synchroner Neustart im Frame-Ereignis");
            Assert.True(await WaitUntil(() => Volatile.Read(ref frames) > 5), "nach dem Neustart keine Bilder");
            Assert.True(loop.IsRunning);
            await WithTimeout(loop.StopAsync(), 5000, "Stopp");
        }
    }

    [Fact(Timeout = TimeoutMs)]
    public async Task Synchroner_Neustart_aus_Detected_mit_Demo_Kamera_verklemmt_nicht()
    {
        await using var source = new FakeFrameSource([Marker(1)], framesPerSecond: 100);
        int restarts = 0;
        var detected = new ConcurrentQueue<IReadOnlyList<DecodedCode>>();
        await using var loop = new ScanLoop(source, _ => [Code("A")], repeatSuppressMs: 0);
        loop.Detected += (_, d) =>
        {
            detected.Enqueue(d.Codes);
            if (Interlocked.Increment(ref restarts) != 1) return;
            loop.StopAsync().Wait();
            Thread.Sleep(50);
            loop.StartAsync().Wait();
        };

        await loop.StartAsync();
        Assert.True(await WaitUntil(() => detected.Count >= 3, 5000), "nach dem Neustart keine Treffer");
        Assert.True(loop.IsRunning);
        await WithTimeout(loop.StopAsync(), 5000, "Stopp");
    }
}
