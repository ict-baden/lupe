// SPDX-License-Identifier: MIT
using Lupe.Core.Model;
using Lupe.Core.Platform;

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

/// <summary>Webcams der aktiven Plattform (<see cref="LupePlatform.Current"/>), siehe <see cref="ICameraProvider"/>.</summary>
/// <remarks>
/// <para>Die geöffnete Quelle startet erst mit <see cref="IFrameSource.StartAsync"/>. Erwartbare Probleme (keine Kamera,
/// belegt, Datenschutz, abgezogen) werfen keine Ausnahme, sondern melden sich über <see cref="IFrameSource.Failed"/> –
/// auch schon während <c>StartAsync</c>. Nur ein Abbruch über das Token wirft <see cref="OperationCanceledException"/>.</para>
/// </remarks>
public static class Cameras
{
    /// <summary>Alle Kameras mit Farbbild, Namen eindeutig. Keine Kamera → leere Liste, kein Fehler.</summary>
    public static Task<IReadOnlyList<CameraInfo>> ListAsync(CancellationToken ct = default) =>
        LupePlatform.Current.Cameras.ListAsync(ct);

    /// <summary>
    /// Die Kamera zur gespeicherten Kennung (<see cref="LupeSettings.Camera"/>): erst nach <see cref="CameraInfo.Id"/>, dann
    /// nach <see cref="CameraInfo.Name"/> (Groß/klein und Leerzeichen am Rand egal). Leere Kennung oder die Kamera ist gerade
    /// nicht angeschlossen → die erste der Liste; leere Liste → null.
    /// </summary>
    public static CameraInfo? Find(IReadOnlyList<CameraInfo> cameras, string? key)
    {
        ArgumentNullException.ThrowIfNull(cameras);
        if (cameras.Count == 0) return null;
        var wanted = key?.Trim();
        if (string.IsNullOrEmpty(wanted)) return cameras[0];
        return cameras.FirstOrDefault(c => string.Equals(c.Id, wanted, StringComparison.OrdinalIgnoreCase))
            ?? cameras.FirstOrDefault(c => string.Equals(c.Name?.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            ?? cameras[0];
    }

    /// <summary>Öffnet die Kamera als Bildquelle (ohne sie schon zu starten). Bevorzugt ~1280×720.</summary>
    public static IFrameSource Open(CameraInfo camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        return LupePlatform.Current.Cameras.Open(camera);
    }
}
