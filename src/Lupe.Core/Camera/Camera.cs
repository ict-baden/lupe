using System.Diagnostics;
using Lupe.Core.Model;
using Windows.Media.Capture.Frames;

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
/// <remarks>
/// <para><see cref="CameraInfo.Id"/> ist die Kennung der <see cref="MediaFrameSourceGroup"/>; eine Geräte-Kennung
/// (z. B. aus <c>DeviceInformation</c>) funktioniert beim Öffnen ebenfalls. Leere Kennung = erste Kamera.</para>
/// <para>Die geöffnete Quelle startet erst mit <see cref="IFrameSource.StartAsync"/>. Erwartbare Probleme (keine Kamera,
/// belegt, Datenschutz, abgezogen) werfen keine Ausnahme, sondern melden sich über <see cref="IFrameSource.Failed"/> –
/// auch schon während <c>StartAsync</c>. Nur ein Abbruch über das Token wirft <see cref="OperationCanceledException"/>.</para>
/// </remarks>
public static class Cameras
{
    /// <summary>
    /// Alle Kameras mit Farbbild (Vorschau- oder Aufnahme-Datenstrom), Namen eindeutig (gleiche Modelle mit „(1)“, „(2)“).
    /// Keine Kamera oder Windows ohne Medienfunktionen → leere Liste, kein Fehler.
    /// </summary>
    public static async Task<IReadOnlyList<CameraInfo>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var groups = await MediaFrameSourceGroup.FindAllAsync().AsTask(ct).ConfigureAwait(false);
            var candidates = new List<CameraCandidate>(groups.Count);
            foreach (var group in groups)
            {
                var candidate = Describe(group);
                if (candidate is not null) candidates.Add(candidate);
            }
            return CameraList.Build(candidates);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // z. B. Windows N ohne Media Feature Pack oder ein hängender Kameradienst: dann eben keine Kamera
            Trace.TraceWarning($"Lupe: Kameras ließen sich nicht auflisten: {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}");
            return [];
        }
    }

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
        return new MediaCaptureFrameSource(camera);
    }

    private static CameraCandidate? Describe(MediaFrameSourceGroup group)
    {
        try
        {
            var sources = new List<CameraSourceCandidate>();
            foreach (var info in group.SourceInfos)
            {
                string? deviceId = null;
                try { deviceId = info.DeviceInformation?.Id; }
                catch (Exception) { /* manche Treiber liefern keine Geräteinfo – egal */ }
                sources.Add(new CameraSourceCandidate(
                    info.SourceKind == MediaFrameSourceKind.Color,
                    info.MediaStreamType is Windows.Media.Capture.MediaStreamType.VideoPreview or Windows.Media.Capture.MediaStreamType.VideoRecord,
                    deviceId));
            }
            return new CameraCandidate(group.Id, group.DisplayName, sources);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Lupe: Kameragruppe übersprungen: {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}");
            return null;
        }
    }
}
