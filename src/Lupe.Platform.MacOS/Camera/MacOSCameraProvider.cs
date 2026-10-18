using System.Diagnostics;
using Lupe.Core.Camera;
using Lupe.Core.Platform;

namespace Lupe.Platform.MacOS.Camera;

/// <summary>Webcams unter macOS über libLupeCamera (AVFoundation).</summary>
public sealed class MacOSCameraProvider : ICameraProvider
{
    internal static TimeSpan ListTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public async Task<IReadOnlyList<CameraInfo>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS()) return [];
        // Ohne Erlaubnis gar nicht erst fragen; die Kamera-Seite zeigt den Hinweis aus MacOSPermissions
        if (MacOSPermissions.Camera() is CameraAuthorization.Denied or CameraAuthorization.Restricted) return [];
        List<(string Id, string Name)> cameras;
        try
        {
            // AVFoundation kann beim Auflisten hängen (auf GitHub-Runnern ohne Kamera und ohne Dialog dauerhaft) –
            // dann wie „keine Kamera“, statt die Kamera-Seite ewig suchen zu lassen
            cameras = await Task.Run(Enumerate, ct).WaitAsync(ListTimeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Trace.TraceWarning($"Lupe: Kameras nach {ListTimeout.TotalSeconds:0} s noch nicht aufgelistet – keine Kamera angenommen.");
            return [];
        }
        ct.ThrowIfCancellationRequested();
        var names = CameraList.MakeUnique(cameras.Select(c => c.Name).ToList());
        return cameras.Select((c, i) => new CameraInfo(c.Id, names[i])).ToList();
    }

    public IFrameSource Open(CameraInfo camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        return new AVFoundationFrameSource(camera);
    }

    /// <summary>Alle Kameras; Fehler → leere Liste.</summary>
    private static List<(string Id, string Name)> Enumerate()
    {
        try
        {
            return NativeCamera.List();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Trace.TraceWarning($"Lupe: Kameras nicht auflistbar: {ex.Message}");
            return [];
        }
    }
}
