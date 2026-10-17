using Lupe.Core.Camera;
using Lupe.Core.Platform;

namespace Lupe.Platform.Linux.Camera;

/// <summary>Kameras unter Linux – noch nicht umgesetzt: keine Kamera.</summary>
public sealed class LinuxCameraProvider : ICameraProvider
{
    public Task<IReadOnlyList<CameraInfo>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<CameraInfo>>([]);
    }

    public IFrameSource Open(CameraInfo camera) =>
        throw new NotSupportedException("Kameras sind unter Linux noch nicht verfügbar.");
}
