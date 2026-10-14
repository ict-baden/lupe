using Lupe.Core.Model;

namespace Lupe.Core.Camera;

public sealed record CameraInfo(string Id, string Name);

/// <summary>Eine Bildquelle (Webcam oder Attrappe), die laufend Bilder liefert.</summary>
public interface IFrameSource : IAsyncDisposable
{
    string Name { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    /// <summary>Neues Bild; auf einem Hintergrund-Thread, das Bild gehört danach dem Empfänger.</summary>
    event EventHandler<PixelImage>? FrameArrived;
    /// <summary>Quelle ist ausgefallen (Kamera abgezogen, von anderer App belegt …), Text auf Deutsch.</summary>
    event EventHandler<string>? Failed;
}

/// <summary>Webcams über Windows.Media.Capture (MediaFrameReader).</summary>
public static class Cameras
{
    public static Task<IReadOnlyList<CameraInfo>> ListAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public static IFrameSource Open(CameraInfo camera) => throw new NotImplementedException();
}

/// <summary>Attrappe für Tests, Demo und Screenshots: spielt vorgegebene Bilder in einer Schleife ab.</summary>
public sealed class FakeFrameSource : IFrameSource
{
    public FakeFrameSource(IReadOnlyList<PixelImage> frames, double framesPerSecond = 15, string name = "Demo-Kamera") => throw new NotImplementedException();
    public string Name => throw new NotImplementedException();
    public event EventHandler<PixelImage>? FrameArrived;
    public event EventHandler<string>? Failed;
    public Task StartAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task StopAsync() => throw new NotImplementedException();
    public ValueTask DisposeAsync() => throw new NotImplementedException();
}

/// <summary>
/// Verbindet Bildquelle und Erkennung: drosselt (Bilder, die während einer laufenden Erkennung kommen, werden
/// verworfen), meldet Treffer mit Wiederholungssperre und reicht jedes Bild für die Vorschau weiter.
/// </summary>
public sealed class ScanLoop : IAsyncDisposable
{
    public ScanLoop(IFrameSource source, Func<PixelImage, IReadOnlyList<DecodedCode>> decode, int repeatSuppressMs = 3000) => throw new NotImplementedException();

    /// <summary>Jedes Bild der Quelle, für die Vorschau.</summary>
    public event EventHandler<PixelImage>? Frame;
    /// <summary>Neue Treffer (ohne die gerade gesperrten Wiederholungen) samt dem Bild, in dem sie gefunden wurden.</summary>
    public event EventHandler<(IReadOnlyList<DecodedCode> Codes, PixelImage Frame)>? Detected;
    public event EventHandler<string>? Failed;

    public Task StartAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task StopAsync() => throw new NotImplementedException();
    public ValueTask DisposeAsync() => throw new NotImplementedException();
}
