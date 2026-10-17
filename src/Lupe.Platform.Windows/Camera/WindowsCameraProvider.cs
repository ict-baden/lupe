using System.Diagnostics;
using Lupe.Core.Camera;
using Lupe.Core.Platform;
using Windows.Media.Capture.Frames;
using Windows.Media.Capture;

namespace Lupe.Platform.Windows.Camera;

/// <summary>Webcams über Windows.Media.Capture (MediaFrameReader).</summary>
/// <remarks>
/// <para><see cref="CameraInfo.Id"/> ist die Kennung der <see cref="MediaFrameSourceGroup"/>; eine Geräte-Kennung
/// (z. B. aus <c>DeviceInformation</c>) funktioniert beim Öffnen ebenfalls. Leere Kennung = erste Kamera.</para>
/// <para>Die geöffnete Quelle startet erst mit <see cref="IFrameSource.StartAsync"/>. Erwartbare Probleme (keine Kamera,
/// belegt, Datenschutz, abgezogen) werfen keine Ausnahme, sondern melden sich über <see cref="IFrameSource.Failed"/> –
/// auch schon während <c>StartAsync</c>. Nur ein Abbruch über das Token wirft <see cref="OperationCanceledException"/>.</para>
/// </remarks>
public sealed class WindowsCameraProvider : ICameraProvider
{
    /// <summary>
    /// Alle Kameras mit Farbbild (Vorschau- oder Aufnahme-Datenstrom), Namen eindeutig (gleiche Modelle mit „(1)“, „(2)“).
    /// Keine Kamera oder Windows ohne Medienfunktionen → leere Liste, kein Fehler.
    /// </summary>
    public async Task<IReadOnlyList<CameraInfo>> ListAsync(CancellationToken ct = default)
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

    /// <summary>Öffnet die Kamera als Bildquelle (ohne sie schon zu starten). Bevorzugt ~1280×720.</summary>
    public IFrameSource Open(CameraInfo camera)
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
                    info.MediaStreamType is MediaStreamType.VideoPreview or MediaStreamType.VideoRecord,
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
