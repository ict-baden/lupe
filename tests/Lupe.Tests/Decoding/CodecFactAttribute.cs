using Lupe.Core.Model;
using Windows.Graphics.Imaging;

namespace Lupe.Tests.Decoding;

/// <summary>
/// Test, der einen optionalen Windows-Bildcodec braucht (WebP, HEIF …). Fehlt der Codec auf dem Rechner, wird der Test
/// mit Begründung übersprungen statt zu scheitern.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CodecFactAttribute : FactAttribute
{
    /// <param name="extension">Dateiendung, für die ein Decoder da sein muss, z. B. ".webp".</param>
    /// <param name="needsEncoder">Der Test schreibt die Datei selbst – dann muss Kodieren und Dekodieren klappen.</param>
    /// <param name="sampleBase64">Beispieldatei des Tests: Sie muss sich wirklich lesen lassen. Auf manchen Systemen
    /// (z. B. GitHub-Runnern) ist ein Decoder registriert, scheitert aber beim Lesen.</param>
    public CodecFactAttribute(string extension, bool needsEncoder = false, string? sampleBase64 = null)
    {
        if (!CodecProbe.CanDecode(extension))
            Skip = $"Kein Windows-Decoder für {extension} installiert.";
        else if (sampleBase64 is not null && !CodecProbe.CanDecodeSample(extension, sampleBase64))
            Skip = $"Der Windows-Decoder für {extension} ist registriert, liest aber keine Dateien (Codec-Erweiterung unvollständig).";
        else if (needsEncoder && !CodecProbe.CanRoundTrip(extension))
            Skip = $"{extension} lässt sich auf diesem Rechner nicht schreiben und wieder lesen (Codec-Erweiterung fehlt).";
    }
}

internal static class CodecProbe
{
    private static readonly Dictionary<string, bool> s_roundTrip = new(StringComparer.OrdinalIgnoreCase);

    public static bool CanDecode(string extension)
    {
        try
        {
            return BitmapDecoder.GetDecoderInformationEnumerator()
                .Any(c => c.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Liest eine mitgelieferte Beispieldatei – der zuverlässigste Beweis, dass der Decoder wirklich arbeitet.</summary>
    public static bool CanDecodeSample(string extension, string sampleBase64)
    {
        try
        {
            using var files = new TestImageFiles();
            var path = files.PathFor("probe" + extension);
            File.WriteAllBytes(path, Convert.FromBase64String(sampleBase64));
            return Lupe.Core.Decoding.ImageFile.LoadAsync(path).GetAwaiter().GetResult().Width > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Probiert ein winziges Bild tatsächlich zu schreiben und zu lesen (HEIF braucht z. B. den HEVC-Codec).</summary>
    public static bool CanRoundTrip(string extension)
    {
        lock (s_roundTrip)
        {
            if (s_roundTrip.TryGetValue(extension, out var known)) return known;
            bool ok;
            try
            {
                var encoder = BitmapEncoder.GetEncoderInformationEnumerator()
                    .FirstOrDefault(c => c.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
                if (encoder == null) ok = false;
                else
                {
                    using var files = new TestImageFiles();
                    var px = new byte[16 * 16 * 4];
                    Array.Fill(px, (byte)255);
                    var path = files.WriteAsync("probe" + extension, encoder.CodecId, null, new PixelImage(16, 16, px)).GetAwaiter().GetResult();
                    ok = Lupe.Core.Decoding.ImageFile.LoadAsync(path).GetAwaiter().GetResult().Width == 16;
                }
            }
            catch (Exception)
            {
                ok = false;
            }
            s_roundTrip[extension] = ok;
            return ok;
        }
    }
}
