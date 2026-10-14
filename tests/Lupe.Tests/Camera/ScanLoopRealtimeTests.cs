using System.Collections.Concurrent;
using System.Diagnostics;
using Lupe.Core.Camera;
using Lupe.Core.Decoding;
using Lupe.Core.Model;
using Lupe.Tests.TestSupport;
using static Lupe.Tests.Camera.CameraTestSupport;

namespace Lupe.Tests.Camera;

/// <summary>
/// Demo-Kamera mit echten Codes (<see cref="TestCodes"/>) + echte Erkennung in Echtzeit: so, wie die Kamera-Seite
/// der App die Bausteine zusammensteckt.
/// </summary>
public sealed class ScanLoopRealtimeTests
{
    /// <summary>Kamerabild 480×360 mit grauem Hintergrund, mit einem QR-Code oder (null) ganz ohne.</summary>
    private static PixelImage Scene(string? text, int x = 120, int y = 60, int width = 480, int height = 360)
    {
        if (text is not null) return TestCodes.Embed(TestCodes.Render(text, width: 200, height: 200), width, height, x, y);
        var px = new byte[width * height * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = px[i + 1] = px[i + 2] = 200; px[i + 3] = 255; }
        return new PixelImage(width, height, px);
    }

    private sealed class Recorder
    {
        public readonly ConcurrentQueue<(long Ms, DecodedCode Code)> Detections = new();
        public readonly ConcurrentQueue<string> Failures = new();
        public int Frames;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public void Attach(ScanLoop loop)
        {
            loop.Frame += (_, _) => Interlocked.Increment(ref Frames);
            loop.Detected += (_, d) =>
            {
                foreach (var code in d.Codes) Detections.Enqueue((_clock.ElapsedMilliseconds, code));
            };
            loop.Failed += (_, msg) => Failures.Enqueue(msg);
        }
    }

    [Fact]
    public async Task Code_dauerhaft_im_Bild_wird_innerhalb_der_Sperre_genau_einmal_gemeldet()
    {
        await using var source = new FakeFrameSource([Scene("Lupe-1")], framesPerSecond: 30);
        int decodes = 0;
        await using var loop = new ScanLoop(source, img => { Interlocked.Increment(ref decodes); return ZxingDecode(img); }, repeatSuppressMs: 500);
        var rec = new Recorder();
        rec.Attach(loop);

        await loop.StartAsync();
        await Task.Delay(1500); // dreimal so lang wie die Sperre – aber der Code verschwindet nie
        await loop.StopAsync();

        var (_, code) = Assert.Single(rec.Detections);
        Assert.Equal("Lupe-1", code.Text);
        Assert.Equal("QR_CODE", code.Format);
        Assert.NotEmpty(code.Points);
        Assert.True(Volatile.Read(ref decodes) >= 5, $"nur {decodes} Erkennungen");
        Assert.True(rec.Frames >= 15, $"nur {rec.Frames} Bilder");
        Assert.Empty(rec.Failures);
    }

    [Fact]
    public async Task Verschwindet_der_Code_länger_als_die_Sperre_wird_er_erneut_gemeldet()
    {
        // Je Durchgang: 6 Bilder mit Code (~150 ms), dann 16 ohne (~400 ms) – Sperre 200 ms
        var frames = Enumerable.Repeat(Scene("Wiederkehr"), 6).Concat(Enumerable.Repeat(Scene(null), 16)).ToList();
        await using var source = new FakeFrameSource(frames, framesPerSecond: 40);
        await using var loop = new ScanLoop(source, ZxingDecode, repeatSuppressMs: 200);
        var rec = new Recorder();
        rec.Attach(loop);

        await loop.StartAsync();
        Assert.True(await WaitUntil(() => rec.Detections.Count >= 3, 15_000), $"nur {rec.Detections.Count} Meldungen");
        await loop.StopAsync();

        var times = rec.Detections.Select(d => d.Ms).ToArray();
        Assert.All(rec.Detections, d => Assert.Equal("Wiederkehr", d.Code.Text));
        // Nie zweimal in derselben Sichtungsphase
        for (int i = 1; i < times.Length; i++) Assert.True(times[i] - times[i - 1] >= 200, $"Abstand {times[i] - times[i - 1]} ms");
    }

    [Fact]
    public async Task Mehrere_Codes_in_einem_Bild_ein_Treffer_mit_allen()
    {
        var scene = TestCodes.Embed(TestCodes.Render("Links", width: 180, height: 180), 640, 360, 40, 90);
        var right = TestCodes.Render("Rechts", width: 180, height: 180);
        for (int row = 0; row < right.Height; row++)
            Buffer.BlockCopy(right.Bgra, row * right.Stride, scene.Bgra, ((90 + row) * 640 + 420) * 4, right.Width * 4);
        await using var source = new FakeFrameSource([scene], framesPerSecond: 20);
        await using var loop = new ScanLoop(source, ZxingDecode, repeatSuppressMs: 5000);
        var detected = new ConcurrentQueue<IReadOnlyList<DecodedCode>>();
        loop.Detected += (_, d) => detected.Enqueue(d.Codes);

        await loop.StartAsync();
        Assert.True(await WaitUntil(() => !detected.IsEmpty));
        await Task.Delay(500);
        await loop.StopAsync();

        var codes = Assert.Single(detected);
        Assert.Equal(["Links", "Rechts"], codes.Select(c => c.Text).Order());
    }

    [Fact]
    public async Task Langsame_Erkennung_kein_Stau_Vorschau_läuft_weiter()
    {
        int running = 0, maxRunning = 0, decodes = 0;
        await using var source = new FakeFrameSource([Scene("Langsam")], framesPerSecond: 60);
        await using var loop = new ScanLoop(source, img =>
        {
            int now = Interlocked.Increment(ref running);
            if (now > Volatile.Read(ref maxRunning)) Volatile.Write(ref maxRunning, now);
            Interlocked.Increment(ref decodes);
            Thread.Sleep(150);
            Interlocked.Decrement(ref running);
            return ZxingDecode(img);
        }, repeatSuppressMs: 10_000);
        var rec = new Recorder();
        rec.Attach(loop);

        await loop.StartAsync();
        await Task.Delay(1200);
        int framesWhileRunning = Volatile.Read(ref rec.Frames);
        await loop.StopAsync();
        int decodesAtStop = Volatile.Read(ref decodes);
        await Task.Delay(400);

        Assert.Equal(1, maxRunning);
        Assert.True(framesWhileRunning >= 25, $"Vorschau ruckelt: nur {framesWhileRunning} Bilder in 1,2 s");
        Assert.InRange(decodesAtStop, 2, 10); // 1,2 s / 150 ms ≈ 8
        Assert.Equal(decodesAtStop, Volatile.Read(ref decodes)); // kein Rückstau nach dem Stopp
        Assert.Single(rec.Detections);
    }

    [Fact]
    public async Task Ausfall_der_Demo_Kamera_wird_weitergereicht()
    {
        await using var source = new FakeFrameSource([Scene("x")], framesPerSecond: 30);
        await using var loop = new ScanLoop(source, ZxingDecode);
        var rec = new Recorder();
        rec.Attach(loop);
        await loop.StartAsync();
        Assert.True(await WaitUntil(() => Volatile.Read(ref rec.Frames) > 0));

        await source.SimulateFailureAsync("Die Kamera „Demo-Kamera“ wurde abgezogen.");

        Assert.True(await WaitUntil(() => !rec.Failures.IsEmpty));
        Assert.Equal(["Die Kamera „Demo-Kamera“ wurde abgezogen."], rec.Failures);
        Assert.False(loop.IsRunning);
        // Neustart nach dem Ausfall
        int before = Volatile.Read(ref rec.Frames);
        await loop.StartAsync();
        Assert.True(await WaitUntil(() => Volatile.Read(ref rec.Frames) > before));
    }

    [DecoderFact]
    public async Task Mit_dem_echten_BarcodeDecoder()
    {
        var decoder = new BarcodeDecoder();
        var frames = new[] { Scene("Echt-1"), Scene(null), Scene("Echt-2", x: 250, y: 140) };
        await using var source = new FakeFrameSource(frames, framesPerSecond: 15);
        await using var loop = new ScanLoop(source, img => decoder.Decode(img), repeatSuppressMs: 60_000);
        var rec = new Recorder();
        rec.Attach(loop);

        await loop.StartAsync();
        Assert.True(await WaitUntil(() => rec.Detections.Count >= 2, 15_000));
        await Task.Delay(500);
        await loop.StopAsync();

        Assert.Equal(["Echt-1", "Echt-2"], rec.Detections.Select(d => d.Code.Text).Order());
        Assert.Empty(rec.Failures);
    }
}

/// <summary>
/// Test mit dem echten <see cref="BarcodeDecoder"/>. Solange der Baustein „decoding“ in diesem Zweig nur ein Stub ist,
/// wird er übersprungen – nach dem Zusammenführen läuft er automatisch mit.
/// </summary>
public sealed class DecoderFactAttribute : FactAttribute
{
    public DecoderFactAttribute()
    {
        if (!IsAvailable) Skip = "Baustein „decoding“ ist noch nicht zusammengeführt (Stub wirft NotImplementedException).";
    }

    public static bool IsAvailable { get; } = Probe();

    private static bool Probe()
    {
        try
        {
            _ = new BarcodeDecoder().Decode(TestCodes.Render("probe", width: 100, height: 100));
            return true;
        }
        catch (NotImplementedException)
        {
            return false;
        }
        catch
        {
            // Anderer Fehler: nicht überspringen, der Test soll ihn zeigen
            return true;
        }
    }
}
