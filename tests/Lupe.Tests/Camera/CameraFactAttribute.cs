using Windows.Devices.Enumeration;

using Lupe.Platform.Windows.Camera;

namespace Lupe.Tests.Camera;

/// <summary>
/// Test, der eine echte Kamera braucht (Windows meldet mindestens ein Videoaufnahmegerät). Ohne Kamera wird er
/// übersprungen. Virtuelle Kameras (z. B. DroidCam, OBS) zählen mit – sie laufen über denselben Media-Foundation-Weg.
/// </summary>
public sealed class CameraFactAttribute : FactAttribute
{
    public CameraFactAttribute()
    {
        if (!IsAvailable) Skip = "Keine Kamera angeschlossen (Windows meldet kein Videoaufnahmegerät).";
    }

    public static bool IsAvailable { get; } = Probe();

    private static bool Probe()
    {
        try
        {
            var devices = DeviceInformation.FindAllAsync(DeviceClass.VideoCapture).AsTask().GetAwaiter().GetResult();
            return devices.Count > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>Alle Tests mit echter Kamera nacheinander – zwei exklusive Zugriffe gleichzeitig würden sich verdrängen.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RealCameraCollection
{
    public const string Name = "Echte Kamera";
}
