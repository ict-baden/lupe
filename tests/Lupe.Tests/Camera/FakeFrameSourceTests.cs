using System.Collections.Concurrent;
using System.Diagnostics;
using Lupe.Core.Camera;
using Lupe.Core.Model;
using Lupe.Tests.TestSupport;
using static Lupe.Tests.Camera.CameraTestSupport;

namespace Lupe.Tests.Camera;

// Zählt Bilder in Echtzeit – allein ausführen, damit parallele Tests die Bildrate nicht verfälschen
[Collection(EchtzeitCollection.Name)]
public sealed class FakeFrameSourceTests
{
    [Fact]
    public void Konstruktor_prüft_Argumente()
    {
        var frame = Marker(1);
        Assert.Throws<ArgumentNullException>(() => new FakeFrameSource(null!));
        Assert.Throws<ArgumentException>(() => new FakeFrameSource([]));
        Assert.Throws<ArgumentException>(() => new FakeFrameSource([frame, null!]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeFrameSource([frame], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeFrameSource([frame], -5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeFrameSource([frame], double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeFrameSource([frame], double.PositiveInfinity));
    }

    [Fact]
    public void Name_und_Takt()
    {
        Assert.Equal("Demo-Kamera", new FakeFrameSource([Marker(1)]).Name);
        Assert.Equal("Demo-Kamera", new FakeFrameSource([Marker(1)], name: "  ").Name);
        Assert.Equal("Testbild", new FakeFrameSource([Marker(1)], name: " Testbild ").Name);
        Assert.Equal(TimeSpan.FromMilliseconds(1000.0 / 15), new FakeFrameSource([Marker(1)]).Period);
        Assert.Equal(TimeSpan.FromMilliseconds(1), new FakeFrameSource([Marker(1)], 1_000_000).Period);
        Assert.Equal(TimeSpan.FromDays(1), new FakeFrameSource([Marker(1)], 1e-9).Period);
    }

    [Fact]
    public async Task Spielt_die_Bilder_der_Reihe_nach_in_einer_Schleife_ab()
    {
        await using var source = new FakeFrameSource([Marker(1), Marker(2), Marker(3)], framesPerSecond: 200);
        var got = new ConcurrentQueue<byte>();
        source.FrameArrived += (_, img) => got.Enqueue(MarkerOf(img));

        await source.StartAsync();
        Assert.True(await WaitUntil(() => got.Count >= 7));
        await source.StopAsync();

        Assert.Equal(new byte[] { 1, 2, 3, 1, 2, 3, 1 }, got.Take(7));
    }

    [Fact]
    public async Task Jedes_Bild_ist_eine_eigene_Kopie()
    {
        var original = Marker(7);
        await using var source = new FakeFrameSource([original], framesPerSecond: 200);
        original.Bgra[0] = 99; // nachträgliche Änderung am Original wirkt nicht
        var got = new ConcurrentQueue<PixelImage>();
        var seen = new ConcurrentQueue<byte>();
        source.FrameArrived += (_, img) =>
        {
            seen.Enqueue(img.Bgra[0]);
            got.Enqueue(img);
            img.Bgra[0] = 42; // Empfänger darf das Bild verändern – das nächste Bild ist wieder unverändert
        };

        await source.StartAsync();
        Assert.True(await WaitUntil(() => got.Count >= 3));
        await source.StopAsync();

        var frames = got.ToArray();
        Assert.Equal(frames.Length, frames.Select(f => f.Bgra).Distinct().Count());
        Assert.All(seen, b => Assert.Equal(7, b));
        Assert.All(frames, f => Assert.Equal(42, f.Bgra[0]));
    }

    [Fact]
    public async Task Erstes_Bild_sofort_dann_ungefähr_in_der_Bildrate_auf_Hintergrund_Thread()
    {
        await using var source = new FakeFrameSource([Marker(1)], framesPerSecond: 20);
        int count = 0;
        var threads = new ConcurrentBag<bool>();
        var first = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sw = Stopwatch.StartNew();
        source.FrameArrived += (_, _) =>
        {
            Interlocked.Increment(ref count);
            first.TrySetResult(sw.ElapsedMilliseconds);
            threads.Add(Thread.CurrentThread.IsThreadPoolThread);
        };

        await source.StartAsync();
        Assert.InRange(await first.Task.WaitAsync(TimeSpan.FromSeconds(5)), 0, 500);
        await Task.Delay(1000);
        await source.StopAsync();

        // 20 B/s über ~1 s: großzügig wegen Timer-Auflösung und Last auf dem Testrechner
        Assert.InRange(Volatile.Read(ref count), 8, 30);
        Assert.All(threads, pool => Assert.True(pool, "Bilder kommen auf einem Hintergrund-Thread (Thread-Pool)"));
    }

    [Fact]
    public async Task Nach_StopAsync_kommen_keine_Bilder_mehr()
    {
        await using var source = new FakeFrameSource([Marker(1)], framesPerSecond: 500);
        int count = 0;
        source.FrameArrived += (_, _) => { Interlocked.Increment(ref count); Thread.Sleep(2); };
        for (int round = 0; round < 5; round++)
        {
            await source.StartAsync();
            Assert.True(await WaitUntil(() => Volatile.Read(ref count) > 0));
            await source.StopAsync();
            int after = Volatile.Read(ref count);
            await Task.Delay(60);
            Assert.Equal(after, Volatile.Read(ref count));
            count = 0;
        }
    }

    [Fact]
    public async Task Doppelter_Start_startet_nur_eine_Schleife()
    {
        await using var source = new FakeFrameSource([Marker(1)], framesPerSecond: 10);
        int count = 0;
        source.FrameArrived += (_, _) => Interlocked.Increment(ref count);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => source.StartAsync()));
        await source.StartAsync();
        await Task.Delay(1050);
        await source.StopAsync();
        // Eine Schleife: ~11 Bilder; acht Schleifen wären ~80
        Assert.InRange(Volatile.Read(ref count), 5, 16);
    }

    [Fact]
    public async Task Langsamer_Empfänger_kein_Stau()
    {
        await using var source = new FakeFrameSource([Marker(1)], framesPerSecond: 200);
        int count = 0;
        source.FrameArrived += (_, _) =>
        {
            Interlocked.Increment(ref count);
            Thread.Sleep(100);
        };
        await source.StartAsync();
        await Task.Delay(1000);
        await source.StopAsync();
        // Bei 200 B/s wären es 200 Bilder; bei 100 ms Verarbeitung höchstens ~10 (verpasste Takte verfallen)
        Assert.InRange(Volatile.Read(ref count), 3, 13);
    }

    [Fact]
    public async Task Neustart_beginnt_wieder_beim_ersten_Bild()
    {
        await using var source = new FakeFrameSource([Marker(1), Marker(2), Marker(3)], framesPerSecond: 100);
        var got = new ConcurrentQueue<byte>();
        source.FrameArrived += (_, img) => got.Enqueue(MarkerOf(img));
        await source.StartAsync();
        Assert.True(await WaitUntil(() => got.Count >= 2));
        await source.StopAsync();
        got.Clear();
        await source.StartAsync();
        Assert.True(await WaitUntil(() => !got.IsEmpty));
        await source.StopAsync();
        Assert.Equal(1, got.First());
    }

    [Fact]
    public async Task Stopp_aus_dem_eigenen_Ereignis_heraus_verklemmt_nicht()
    {
        await using var source = new FakeFrameSource([Marker(1)], framesPerSecond: 100);
        int count = 0;
        source.FrameArrived += (_, _) =>
        {
            if (Interlocked.Increment(ref count) == 3)
                source.StopAsync().Wait(); // synchron warten – der schlimmste Fall
        };
        await source.StartAsync();
        Assert.True(await WaitUntil(() => Volatile.Read(ref count) >= 3));
        await Task.Delay(100);
        Assert.Equal(3, Volatile.Read(ref count));
        // und danach normal wieder startbar
        await WithTimeout(source.StartAsync(), what: "Neustart");
        Assert.True(await WaitUntil(() => Volatile.Read(ref count) > 3));
        await WithTimeout(source.StopAsync(), what: "Stopp");
    }

    [Fact]
    public async Task Synchroner_Neustart_aus_dem_eigenen_Ereignis_verklemmt_nicht()
    {
        await using var source = new FakeFrameSource([Marker(1), Marker(2), Marker(3)], framesPerSecond: 100);
        var got = new ConcurrentQueue<byte>();
        source.FrameArrived += (_, img) =>
        {
            got.Enqueue(MarkerOf(img));
            if (got.Count == 2)
            {
                source.StopAsync().Wait();
                source.StartAsync().Wait();
            }
        };
        await source.StartAsync();
        Assert.True(await WaitUntil(() => got.Count >= 5));
        await WithTimeout(source.StopAsync(), what: "Stopp");
        // nach dem Neustart wieder ab dem ersten Bild
        Assert.Equal(new byte[] { 1, 2, 1, 2, 3 }, got.Take(5));
    }

    [Fact]
    public async Task Fehler_eines_Empfängers_stört_weder_andere_noch_die_Schleife()
    {
        await using var source = new FakeFrameSource([Marker(1)], framesPerSecond: 200);
        int good = 0;
        source.FrameArrived += (_, _) => throw new InvalidOperationException("Empfänger kaputt");
        source.FrameArrived += (_, _) => Interlocked.Increment(ref good);
        await source.StartAsync();
        Assert.True(await WaitUntil(() => Volatile.Read(ref good) >= 5));
        await source.StopAsync();
    }

    [Fact]
    public async Task Ausfall_wird_gemeldet_und_beendet_die_Wiedergabe()
    {
        await using var source = new FakeFrameSource([Marker(1)], framesPerSecond: 200);
        int frames = 0;
        var failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.FrameArrived += (_, _) => Interlocked.Increment(ref frames);
        source.Failed += (_, msg) => failed.TrySetResult(msg);
        await source.StartAsync();
        Assert.True(await WaitUntil(() => Volatile.Read(ref frames) > 0));

        await source.SimulateFailureAsync("Kamera abgezogen");

        Assert.Equal("Kamera abgezogen", await failed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await Task.Delay(50);
        int after = Volatile.Read(ref frames);
        await Task.Delay(100);
        Assert.Equal(after, Volatile.Read(ref frames));
        // Erneuter Start nach dem Ausfall geht
        await source.StartAsync();
        Assert.True(await WaitUntil(() => Volatile.Read(ref frames) > after));
    }

    [Fact]
    public async Task Abbruch_beim_Start()
    {
        await using var source = new FakeFrameSource([Marker(1)]);
        int frames = 0;
        source.FrameArrived += (_, _) => Interlocked.Increment(ref frames);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.StartAsync(new CancellationToken(canceled: true)));
        await Task.Delay(100);
        Assert.Equal(0, Volatile.Read(ref frames));
    }

    [Fact]
    public async Task Freigabe_mehrfach_und_danach_kein_Start()
    {
        var source = new FakeFrameSource([Marker(1)], framesPerSecond: 100);
        int frames = 0;
        source.FrameArrived += (_, _) => Interlocked.Increment(ref frames);
        await source.StartAsync();
        Assert.True(await WaitUntil(() => Volatile.Read(ref frames) > 0));
        await source.DisposeAsync();
        await source.DisposeAsync();
        await source.StopAsync();
        int after = Volatile.Read(ref frames);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => source.StartAsync());
        await Task.Delay(50);
        Assert.Equal(after, Volatile.Read(ref frames));
    }

    [Fact]
    public async Task Start_und_Stopp_gleichzeitig_aus_vielen_Threads()
    {
        await using var source = new FakeFrameSource([Marker(1)], framesPerSecond: 300);
        int frames = 0;
        source.FrameArrived += (_, _) => Interlocked.Increment(ref frames);
        var tasks = Enumerable.Range(0, 200).Select(i => Task.Run(() => i % 2 == 0 ? source.StartAsync() : source.StopAsync()));
        await WithTimeout(Task.WhenAll(tasks), 20_000, "Start/Stopp-Gewitter");
        await source.StopAsync();
        int after = Volatile.Read(ref frames);
        await Task.Delay(50);
        Assert.Equal(after, Volatile.Read(ref frames));
        await source.StartAsync();
        Assert.True(await WaitUntil(() => Volatile.Read(ref frames) > after));
    }

    [Fact]
    public async Task Echte_Codes_als_Demo_Kamera()
    {
        var frames = new[]
        {
            TestCodes.Embed(TestCodes.Render("Demo 1", width: 200, height: 200), 640, 480, 50, 50),
            TestCodes.Embed(TestCodes.Render("Demo 2", width: 200, height: 200), 640, 480, 380, 220),
        };
        await using var source = new FakeFrameSource(frames, framesPerSecond: 30);
        var texts = new ConcurrentQueue<string>();
        source.FrameArrived += (_, img) =>
        {
            Assert.Equal(640, img.Width);
            foreach (var code in ZxingDecode(img)) texts.Enqueue(code.Text);
        };
        await source.StartAsync();
        Assert.True(await WaitUntil(() => texts.Distinct().Count() == 2));
        await source.StopAsync();
        Assert.Equal(["Demo 1", "Demo 2"], texts.Distinct().Order());
    }
}
