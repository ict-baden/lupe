// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using Lupe.Core.Camera;
using Lupe.Core.Model;
using Lupe.Tests.TestSupport;
using static Lupe.Tests.Camera.CameraTestSupport;

namespace Lupe.Tests.Camera;

/// <summary>
/// <see cref="ScanLoop"/> mit fernsteuerbarer Quelle und Zeit: jedes Bild und jede Millisekunde kommt dann, wenn der
/// Test es will – Sperre, Drosselung, Fehler und Lebenszyklus genau prüfbar. Die Erkennung und das Warten der Tests laufen
/// trotzdem über den Thread-Pool, daher in der Sammlung „Echtzeit“ (siehe dort).
/// </summary>
[Collection(EchtzeitCollection.Name)]
public sealed class ScanLoopTests
{
    private readonly ManualFrameSource _source = new();
    private readonly ManualTimeProvider _time = new();
    private readonly ConcurrentQueue<PixelImage> _frames = new();
    private readonly ConcurrentQueue<(IReadOnlyList<DecodedCode> Codes, PixelImage Frame)> _detected = new();
    private readonly ConcurrentQueue<string> _failed = new();
    private int _decodeCalls;

    /// <summary>Was die Erkennung beim nächsten Aufruf liefert.</summary>
    private volatile IReadOnlyList<DecodedCode> _nextResult = [];

    private ScanLoop CreateLoop(int repeatSuppressMs = 3000, Func<PixelImage, IReadOnlyList<DecodedCode>>? decode = null)
    {
        var loop = new ScanLoop(_source, decode ?? (img =>
        {
            Interlocked.Increment(ref _decodeCalls);
            return _nextResult;
        }), repeatSuppressMs, _time);
        loop.Frame += (_, img) => _frames.Enqueue(img);
        loop.Detected += (_, d) => _detected.Enqueue(d);
        loop.Failed += (_, msg) => _failed.Enqueue(msg);
        return loop;
    }

    /// <summary>Bild schicken und die dadurch ausgelöste Erkennung abwarten.</summary>
    private async Task PushAndWait(ScanLoop loop, PixelImage image, params DecodedCode[] result)
    {
        _nextResult = result;
        _source.Push(image);
        await WithTimeout(loop.CurrentDecode, what: "Erkennung");
    }

    private string[][] DetectedTexts() => _detected.Select(d => d.Codes.Select(c => c.Text).ToArray()).ToArray();

    // ---------- Wiederholungssperre ----------

    [Fact]
    public async Task Treffer_genau_einmal_in_der_Sperre_ab_letzter_Sichtung_danach_erneut()
    {
        await using var loop = CreateLoop(3000);
        await loop.StartAsync();

        _time.Set(0);
        await PushAndWait(loop, Marker(1), Code("A"));
        _time.Set(1000);
        await PushAndWait(loop, Marker(1), Code("A"));
        _time.Set(3500); // 3500 nach der ersten, aber nur 2500 nach der letzten Sichtung
        await PushAndWait(loop, Marker(1), Code("A"));
        Assert.Single(_detected);

        _time.Set(6600); // 3100 ms ohne Sichtung
        await PushAndWait(loop, Marker(1), Code("A"));
        Assert.Equal([["A"], ["A"]], DetectedTexts());
    }

    [Fact]
    public async Task Bilder_ohne_Code_verlängern_die_Sperre_nicht()
    {
        await using var loop = CreateLoop(1000);
        await loop.StartAsync();
        _time.Set(0);
        await PushAndWait(loop, Marker(1), Code("A"));
        for (int t = 100; t <= 1000; t += 100)
        {
            _time.Set(t);
            await PushAndWait(loop, Marker(2)); // nichts erkannt
        }
        _time.Set(1001);
        await PushAndWait(loop, Marker(1), Code("A"));
        Assert.Equal(2, _detected.Count);
    }

    [Fact]
    public async Task Mehrere_Codes_nur_die_neuen_werden_gemeldet()
    {
        await using var loop = CreateLoop(3000);
        await loop.StartAsync();
        _time.Set(0);
        await PushAndWait(loop, Marker(1), Code("A"), Code("B"));
        _time.Set(500);
        await PushAndWait(loop, Marker(1), Code("A"), Code("C"), Code("B"));
        _time.Set(900);
        await PushAndWait(loop, Marker(1), Code("A", "QR_CODE"), Code("A", "DATA_MATRIX"));
        Assert.Equal([["A", "B"], ["C"], ["A"]], DetectedTexts());
        Assert.Equal("DATA_MATRIX", _detected.Last().Codes[0].Format);
    }

    [Fact]
    public async Task Sperre_null_meldet_jede_Sichtung()
    {
        await using var loop = CreateLoop(0);
        await loop.StartAsync();
        for (int i = 0; i < 5; i++) await PushAndWait(loop, Marker(1), Code("A"));
        Assert.Equal(5, _detected.Count);
    }

    [Fact]
    public async Task Neustart_beginnt_mit_frischer_Sperre()
    {
        await using var loop = CreateLoop(60_000);
        await loop.StartAsync();
        await PushAndWait(loop, Marker(1), Code("A"));
        await PushAndWait(loop, Marker(1), Code("A"));
        await loop.StopAsync();
        await loop.StartAsync();
        await PushAndWait(loop, Marker(1), Code("A"));
        Assert.Equal(2, _detected.Count);
    }

    [Fact]
    public async Task Leeres_oder_null_Ergebnis_meldet_nichts()
    {
        await using var loop = CreateLoop(decode: _ => { Interlocked.Increment(ref _decodeCalls); return null!; });
        await loop.StartAsync();
        _source.Push(Marker(1));
        await WithTimeout(loop.CurrentDecode);
        Assert.Equal(1, _decodeCalls);
        Assert.Empty(_detected);
        Assert.Empty(_failed);
        Assert.Single(_frames);
    }

    // ---------- Bilder und Drosselung ----------

    [Fact]
    public async Task Jedes_Bild_geht_an_die_Vorschau_die_Erkennung_bekommt_eine_Kopie()
    {
        PixelImage? decoded = null;
        await using var loop = CreateLoop(decode: img =>
        {
            decoded = img;
            return [Code("A")];
        });
        await loop.StartAsync();
        var image = Marker(5);
        _source.Push(image);
        await WithTimeout(loop.CurrentDecode);

        Assert.Same(image, Assert.Single(_frames));
        Assert.NotNull(decoded);
        Assert.NotSame(image, decoded);
        Assert.NotSame(image.Bgra, decoded!.Bgra);
        Assert.Equal(image.Bgra, decoded.Bgra);
        // Detected liefert das Bild, in dem gesucht wurde
        Assert.Same(decoded, Assert.Single(_detected).Frame);
    }

    [Fact]
    public async Task Während_einer_Erkennung_werden_Bilder_angezeigt_aber_nicht_gesammelt()
    {
        using var gate = new ManualResetEventSlim(false);
        var seen = new ConcurrentQueue<byte>();
        await using var loop = CreateLoop(decode: img =>
        {
            seen.Enqueue(MarkerOf(img));
            gate.Wait(TimeSpan.FromSeconds(10));
            return [];
        });
        await loop.StartAsync();

        for (byte i = 1; i <= 10; i++) _source.Push(Marker(i));
        Assert.Equal(10, _frames.Count); // Vorschau bekommt alles, ohne zu warten
        Assert.True(await WaitUntil(() => seen.Count == 1));

        gate.Set();
        await WithTimeout(loop.CurrentDecode);
        await Task.Delay(50);
        // Keine Warteschlange: die 9 Bilder während der Erkennung sind verfallen
        Assert.Equal(new byte[] { 1 }, seen);

        _source.Push(Marker(11));
        await WithTimeout(loop.CurrentDecode);
        Assert.Equal(new byte[] { 1, 11 }, seen);
    }

    [Fact]
    public async Task Nie_mehr_als_eine_Erkennung_gleichzeitig()
    {
        int running = 0, maxRunning = 0, calls = 0;
        await using var loop = CreateLoop(decode: _ =>
        {
            int now = Interlocked.Increment(ref running);
            InterlockedMax(ref maxRunning, now);
            Interlocked.Increment(ref calls);
            Thread.Sleep(5);
            Interlocked.Decrement(ref running);
            return [];
        });
        await loop.StartAsync();
        // Bilder aus vielen Threads gleichzeitig (eine echte Kamera liefert auf wechselnden Threads)
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 200; i++) _source.Push(Marker(1));
        })));
        await loop.StopAsync();
        Assert.Equal(1, maxRunning);
        Assert.Equal(1600, _frames.Count);
        Assert.InRange(calls, 1, 1599);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current) { }
    }

    // ---------- Fehler ----------

    [Fact]
    public async Task Ausfall_der_Quelle_wird_weitergereicht_und_hält_an()
    {
        await using var loop = CreateLoop();
        await loop.StartAsync();
        _source.Push(Marker(1));

        _source.Fail("Die Kamera „Testkamera“ ist nicht mehr erreichbar – wurde sie abgezogen?");

        Assert.Equal(["Die Kamera „Testkamera“ ist nicht mehr erreichbar – wurde sie abgezogen?"], _failed);
        Assert.False(loop.IsRunning);
        Assert.True(await WaitUntil(() => _source.StopCount == 1 && _source.FrameSubscribers == 0 && _source.FailedSubscribers == 0));
        // ein zweiter Ausfall derselben Sitzung wird nicht doppelt gemeldet
        _source.Fail("noch einmal");
        Assert.Single(_failed);
    }

    [Fact]
    public async Task Ausfall_ohne_Text_bekommt_eine_deutsche_Meldung()
    {
        _source.Name = "Logitech C920";
        await using var loop = CreateLoop();
        await loop.StartAsync();
        _source.Fail("  ");
        Assert.Contains("„Logitech C920“", Assert.Single(_failed));
    }

    [Fact]
    public async Task Fehler_der_Erkennung_wird_gemeldet_und_hält_an()
    {
        await using var loop = CreateLoop(decode: _ => throw new InvalidOperationException("Leser kaputt"));
        await loop.StartAsync();
        _source.Push(Marker(1));
        await WithTimeout(loop.CurrentDecode);

        var message = Assert.Single(_failed);
        Assert.Contains("Erkennung", message);
        Assert.Contains("Leser kaputt", message);
        Assert.False(loop.IsRunning);
        Assert.True(await WaitUntil(() => _source.StopCount == 1 && _source.FrameSubscribers == 0));
        Assert.Empty(_detected);
    }

    [Fact]
    public async Task Nicht_ladbare_Erkennung_meldet_Fehlercode_statt_Neustart_Hinweis()
    {
        const string message = "LUP-0001: Lupe kann auf diesem System keine Codes lesen …";
        await using var loop = CreateLoop(decode: _ => throw new Lupe.Core.Decoding.DecoderUnavailableException(message));
        await loop.StartAsync();
        _source.Push(Marker(1));
        await WithTimeout(loop.CurrentDecode);

        // Unverändert: kein „Kamera neu starten“ (hilft nicht) und kein technischer Typname davor
        Assert.Equal(message, Assert.Single(_failed));
        Assert.False(loop.IsRunning);
        Assert.Empty(_detected);
    }

    [Fact]
    public async Task Abbruch_in_der_Erkennung_ist_kein_Fehler()
    {
        int calls = 0;
        await using var loop = CreateLoop(decode: _ =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new OperationCanceledException();
            return [Code("A")];
        });
        await loop.StartAsync();
        _source.Push(Marker(1));
        await WithTimeout(loop.CurrentDecode);
        _source.Push(Marker(1));
        await WithTimeout(loop.CurrentDecode);
        Assert.Empty(_failed);
        Assert.Single(_detected);
        Assert.True(loop.IsRunning);
    }

    [Fact]
    public async Task Quelle_wirft_beim_Start_dann_Meldung_statt_Ausnahme()
    {
        _source.StartException = new InvalidOperationException("Treiber antwortet nicht");
        await using var loop = CreateLoop();

        await loop.StartAsync();

        var message = Assert.Single(_failed);
        Assert.Contains("„Testkamera“", message);
        Assert.Contains("Treiber antwortet nicht", message);
        Assert.False(loop.IsRunning);
        Assert.Equal(0, _source.FrameSubscribers);
        Assert.Equal(1, _source.StopCount);

        // Problem behoben → erneuter Start klappt
        _source.StartException = null;
        await loop.StartAsync();
        Assert.True(loop.IsRunning);
        _source.Push(Marker(1));
        Assert.Single(_frames);
    }

    [Fact]
    public async Task Quelle_fällt_schon_während_des_Starts_aus()
    {
        _source.DuringStart = s => s.Fail("Kamera belegt");
        await using var loop = CreateLoop();

        await loop.StartAsync();

        Assert.Equal(["Kamera belegt"], _failed);
        Assert.False(loop.IsRunning);
        Assert.True(await WaitUntil(() => _source.FrameSubscribers == 0 && _source.StopCount == 1));
        _source.DuringStart = null;
        await loop.StartAsync();
        Assert.True(loop.IsRunning);
        Assert.Equal(1, _source.FrameSubscribers);
    }

    [Fact]
    public async Task Quelle_meldet_Ausfall_und_wirft_dann_trotzdem_nur_eine_Meldung()
    {
        _source.DuringStart = s =>
        {
            s.Fail("Kamera belegt");
            throw new InvalidOperationException("und dann noch das");
        };
        await using var loop = CreateLoop();

        await loop.StartAsync();

        Assert.Equal(["Kamera belegt"], _failed);
        Assert.False(loop.IsRunning);
        Assert.True(await WaitUntil(() => _source.FrameSubscribers == 0));
    }

    [Fact]
    public async Task Abbruch_beim_Start_wirft_und_hinterlässt_nichts()
    {
        _source.StartDelay = TimeSpan.FromSeconds(30);
        await using var loop = CreateLoop();
        using var cts = new CancellationTokenSource(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop.StartAsync(cts.Token));

        Assert.False(loop.IsRunning);
        Assert.Equal(0, _source.FrameSubscribers);
        Assert.Empty(_failed);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop.StartAsync(new CancellationToken(true)));
    }

    // ---------- Lebenszyklus ----------

    [Fact]
    public void Konstruktor_prüft_Argumente()
    {
        Assert.Throws<ArgumentNullException>(() => new ScanLoop(null!, _ => []));
        Assert.Throws<ArgumentNullException>(() => new ScanLoop(_source, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScanLoop(_source, _ => [], -1));
        _ = new ScanLoop(_source, _ => [], 0);
    }

    [Fact]
    public async Task Doppelter_Start_startet_die_Quelle_einmal_doppelter_Stopp_schadet_nicht()
    {
        await using var loop = CreateLoop();
        await loop.StopAsync(); // ohne Start: nichts zu tun
        Assert.Equal(0, _source.StopCount);

        await loop.StartAsync();
        await loop.StartAsync();
        Assert.Equal(1, _source.StartCount);
        Assert.Equal(1, _source.FrameSubscribers);

        await loop.StopAsync();
        await loop.StopAsync();
        Assert.Equal(1, _source.StopCount);
        Assert.Equal(0, _source.FrameSubscribers);
        Assert.Equal(0, _source.DisposeCount); // die Quelle gehört dem Aufrufer
    }

    [Fact]
    public async Task Nach_StopAsync_keine_Ereignisse_mehr()
    {
        await using var loop = CreateLoop();
        await loop.StartAsync();
        _source.Push(Marker(1));
        await WithTimeout(loop.CurrentDecode);
        await loop.StopAsync();
        int frames = _frames.Count;
        _nextResult = [Code("A")];
        _source.Push(Marker(1)); // Quelle sendet (fehlerhaft) weiter – kommt nicht mehr an
        await Task.Delay(50);
        Assert.Equal(frames, _frames.Count);
        Assert.Empty(_detected);
    }

    [Fact]
    public async Task Stopp_wartet_eine_laufende_Erkennung_ab_und_verwirft_ihr_Ergebnis()
    {
        using var entered = new ManualResetEventSlim(false);
        using var gate = new ManualResetEventSlim(false);
        // Die Erkennung endet erst mit gate.Set() (kein eigenes Zeitlimit): Kommt Task.Delay auf einem ausgelasteten
        // Runner spät dran, dauert der Test nur länger, statt dass die Erkennung inzwischen von selbst endet
        await using var loop = CreateLoop(decode: _ =>
        {
            entered.Set();
            gate.Wait();
            return [Code("zu spät")];
        });
        await loop.StartAsync();
        Task stop;
        try
        {
            _source.Push(Marker(1));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

            stop = loop.StopAsync();
            await Task.Delay(100);
            Assert.False(stop.IsCompleted, "StopAsync muss die laufende Erkennung abwarten");
        }
        finally
        {
            gate.Set();
        }
        await WithTimeout(stop, what: "Stopp");

        Assert.Empty(_detected);
    }

    [Fact]
    public async Task Stopp_aus_Frame_und_Detected_heraus_verklemmt_nicht()
    {
        await using var loop = CreateLoop(0);
        loop.Frame += (_, _) =>
        {
            if (_frames.Count == 3) loop.StopAsync().Wait(); // synchron – der schlimmste Fall
        };
        await loop.StartAsync();
        for (int i = 0; i < 5; i++) _source.Push(Marker(1));
        Assert.Equal(3, _frames.Count);
        Assert.False(loop.IsRunning);
        Assert.True(await WaitUntil(() => _source.FrameSubscribers == 0 && _source.StopCount == 1));

        // dasselbe aus Detected (läuft auf dem Erkennungs-Thread)
        loop.Detected += (_, _) => loop.StopAsync().Wait();
        await loop.StartAsync();
        _nextResult = [Code("A")];
        _source.Push(Marker(9));
        await WithTimeout(loop.CurrentDecode, what: "Erkennung mit Stopp im Detected-Ereignis");
        Assert.False(loop.IsRunning);
        Assert.True(await WaitUntil(() => _source.FrameSubscribers == 0 && _source.StopCount == 2));
    }

    [Fact]
    public async Task Freigabe_aus_dem_Failed_Ereignis_heraus_verklemmt_nicht()
    {
        var loop = CreateLoop();
        loop.Failed += (_, _) => loop.DisposeAsync().AsTask().Wait();
        await loop.StartAsync();
        _source.Fail("weg");
        Assert.True(await WaitUntil(() => _source.FrameSubscribers == 0));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => loop.StartAsync());
    }

    [Fact]
    public async Task Neustart_aus_dem_Failed_Ereignis_heraus()
    {
        await using var loop = CreateLoop();
        int restarts = 0;
        loop.Failed += async (_, _) =>
        {
            if (Interlocked.Increment(ref restarts) == 1) await loop.StartAsync();
        };
        await loop.StartAsync();
        _source.Fail("kurz weg");
        Assert.True(await WaitUntil(() => loop.IsRunning && _source.StartCount == 2));
        await Task.Delay(100); // der Aufräumer im Hintergrund darf den neuen Lauf nicht anhalten
        Assert.True(loop.IsRunning);
        Assert.Equal(1, _source.FrameSubscribers);
        _source.Push(Marker(1));
        Assert.Single(_frames);
    }

    [Fact]
    public async Task Synchroner_Neustart_aus_dem_Detected_Ereignis_verklemmt_nicht()
    {
        await using var loop = CreateLoop(0);
        int restarts = 0;
        loop.Detected += (_, _) =>
        {
            if (Interlocked.Increment(ref restarts) > 1) return;
            loop.StopAsync().Wait();
            loop.StartAsync().Wait(); // synchron auf dem Erkennungs-Thread – der schlimmste Fall
        };
        await loop.StartAsync();
        _nextResult = [Code("A")];
        _source.Push(Marker(1));
        await WithTimeout(loop.CurrentDecode, what: "Neustart im Detected-Ereignis");
        Assert.True(await WaitUntil(() => _source.StartCount == 2));
        await Task.Delay(100); // der Aufräumer im Hintergrund darf den neuen Lauf nicht anhalten
        Assert.True(loop.IsRunning);
        Assert.Equal(1, _source.FrameSubscribers);
        await PushAndWait(loop, Marker(2), Code("B"));
        Assert.Equal("B", _detected.Last().Codes[0].Text);
    }

    [Fact]
    public async Task Freigabe_mehrfach_danach_kein_Start()
    {
        var loop = CreateLoop();
        await loop.StartAsync();
        await loop.DisposeAsync();
        await loop.DisposeAsync();
        await loop.StopAsync();
        Assert.Equal(0, _source.FrameSubscribers);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => loop.StartAsync());
    }

    [Fact]
    public async Task Start_Stopp_Gewitter_aus_vielen_Threads_bleibt_konsistent()
    {
        await using var loop = CreateLoop(0);
        var pushing = new CancellationTokenSource();
        var pusher = Task.Run(() =>
        {
            while (!pushing.IsCancellationRequested) _source.Push(Marker(1));
        });
        _nextResult = [Code("A")];
        var tasks = Enumerable.Range(0, 300).Select(i => Task.Run(() => (i % 3) switch
        {
            0 => loop.StartAsync(),
            1 => loop.StopAsync(),
            _ => loop.StartAsync(),
        }));
        await WithTimeout(Task.WhenAll(tasks), 30_000, "Start/Stopp-Gewitter");
        pushing.Cancel();
        await pusher;

        await loop.StopAsync();
        Assert.Equal(0, _source.FrameSubscribers);
        Assert.Equal(0, _source.FailedSubscribers);
        Assert.Equal(_source.StartCount, _source.StopCount);
        Assert.Empty(_failed);

        await loop.StartAsync();
        Assert.Equal(1, _source.FrameSubscribers); // nie doppelt angemeldet
        Assert.Equal(1, _source.FailedSubscribers);
    }

    [Fact]
    public async Task Fehler_eines_Empfängers_stört_die_Schleife_nicht()
    {
        await using var loop = CreateLoop(0);
        loop.Frame += (_, _) => throw new InvalidOperationException("Vorschau kaputt");
        loop.Detected += (_, _) => throw new InvalidOperationException("Verlauf kaputt");
        await loop.StartAsync();
        await PushAndWait(loop, Marker(1), Code("A"));
        await PushAndWait(loop, Marker(1), Code("A"));
        Assert.Equal(2, _frames.Count);
        Assert.Equal(2, _detected.Count);
        Assert.Empty(_failed);
        Assert.True(loop.IsRunning);
    }
}
