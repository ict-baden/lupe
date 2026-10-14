using System.Diagnostics;
using Lupe.Core.Camera;
using Lupe.Core.Model;
using ZXing;
using ZXing.Common;

namespace Lupe.Tests.Camera;

/// <summary>Bildquelle zum Fernsteuern: Bilder und Ausfälle kommen genau dann, wenn der Test es will (synchron).</summary>
internal sealed class ManualFrameSource : IFrameSource
{
    private int _startCount, _stopCount, _disposeCount;

    public string Name { get; set; } = "Testkamera";
    public int StartCount => Volatile.Read(ref _startCount);
    public int StopCount => Volatile.Read(ref _stopCount);
    public int DisposeCount => Volatile.Read(ref _disposeCount);
    public volatile bool Running;

    /// <summary>Wirft StartAsync diese Ausnahme (nach dem Zählen)?</summary>
    public Exception? StartException { get; set; }
    /// <summary>Wird am Ende von StartAsync aufgerufen (z. B. um einen Ausfall beim Start vorzuspielen).</summary>
    public Action<ManualFrameSource>? DuringStart { get; set; }
    public TimeSpan StartDelay { get; set; }

    public event EventHandler<PixelImage>? FrameArrived;
    public event EventHandler<string>? Failed;

    public int FrameSubscribers => FrameArrived?.GetInvocationList().Length ?? 0;
    public int FailedSubscribers => Failed?.GetInvocationList().Length ?? 0;

    public async Task StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _startCount);
        if (StartDelay > TimeSpan.Zero) await Task.Delay(StartDelay, ct);
        if (StartException is not null) throw StartException;
        Running = true;
        DuringStart?.Invoke(this);
    }

    public Task StopAsync()
    {
        Interlocked.Increment(ref _stopCount);
        Running = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCount);
        Running = false;
        return ValueTask.CompletedTask;
    }

    public void Push(PixelImage image) => FrameArrived?.Invoke(this, image);

    public void Fail(string message) => Failed?.Invoke(this, message);
}

/// <summary>Zeitquelle zum Weiterstellen; ein Zeitstempel = 1 ms.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _now = 1_000_000;

    public override long TimestampFrequency => 1000;

    public override long GetTimestamp() => Interlocked.Read(ref _now);

    public void Set(long milliseconds) => Interlocked.Exchange(ref _now, 1_000_000 + milliseconds);

    public void Advance(long milliseconds) => Interlocked.Add(ref _now, milliseconds);
}

internal static class CameraTestSupport
{
    /// <summary>Kleines einfarbiges Bild, Blau-Kanal = Kennzahl (zum Wiedererkennen nach dem Kopieren).</summary>
    public static PixelImage Marker(byte id, int width = 8, int height = 6)
    {
        var px = new byte[width * height * 4];
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i] = id;
            px[i + 1] = 10;
            px[i + 2] = 20;
            px[i + 3] = 255;
        }
        return new PixelImage(width, height, px);
    }

    public static byte MarkerOf(PixelImage image) => image.Bgra[0];

    public static DecodedCode Code(string text, string format = "QR_CODE") => new() { Text = text, Format = format };

    /// <summary>Wartet, bis die Bedingung gilt; false nach Ablauf.</summary>
    public static async Task<bool> WaitUntil(Func<bool> condition, int timeoutMs = 10_000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) return false;
            await Task.Delay(10);
        }
        return true;
    }

    /// <summary>Wartet auf eine Aufgabe mit Zeitlimit (gegen hängende Tests bei Verklemmungen).</summary>
    public static async Task WithTimeout(Task task, int timeoutMs = 10_000, string what = "Vorgang")
    {
        var done = await Task.WhenAny(task, Task.Delay(timeoutMs));
        Assert.True(done == task, $"{what} hängt (> {timeoutMs} ms) – Verklemmung?");
        await task;
    }

    /// <summary>
    /// Erkennung direkt mit ZXing (unabhängig vom Baustein „decoding“): alle QR-Codes im Bild, auch mehrere.
    /// </summary>
    public static IReadOnlyList<DecodedCode> ZxingDecode(PixelImage image)
    {
        byte[] bytes = image.Bgra;
        if (image.Stride != image.Width * 4)
        {
            bytes = new byte[image.Width * image.Height * 4];
            for (int y = 0; y < image.Height; y++)
                Buffer.BlockCopy(image.Bgra, y * image.Stride, bytes, y * image.Width * 4, image.Width * 4);
        }
        var source = new RGBLuminanceSource(bytes, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = false,
            Options = new DecodingOptions { TryHarder = true, PossibleFormats = [BarcodeFormat.QR_CODE] },
        };
        var results = reader.DecodeMultiple(source);
        if (results is null) return [];
        return results.Select(r => new DecodedCode
        {
            Text = r.Text,
            Format = r.BarcodeFormat.ToString(),
            Points = r.ResultPoints.Select(p => (p.X, p.Y)).ToList(),
            RawBytes = r.RawBytes,
        }).ToList();
    }
}
