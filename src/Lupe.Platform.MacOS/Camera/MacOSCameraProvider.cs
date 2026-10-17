using Lupe.Core.Camera;
using Lupe.Core.Platform;

namespace Lupe.Platform.MacOS.Camera;

/// <summary>Kameras unter macOS – noch nicht umgesetzt: keine Kamera.</summary>
public sealed class MacOSCameraProvider : ICameraProvider
{
    public Task<IReadOnlyList<CameraInfo>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<CameraInfo>>([]);
    }

    public IFrameSource Open(CameraInfo camera) =>
        throw new NotSupportedException("Kameras sind unter macOS noch nicht verfügbar.");
}
