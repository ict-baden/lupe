using Lupe.Core.Model;

namespace Lupe.Core.Decoding;

/// <summary>Lädt Bilddateien (PNG, JPEG, BMP, GIF, TIFF und was Windows sonst lesen kann) als <see cref="PixelImage"/>.</summary>
public static class ImageFile
{
    public static Task<PixelImage> LoadAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();

    /// <summary>Dateiendungen, die im Öffnen-Dialog angeboten werden (z. B. ".png").</summary>
    public static IReadOnlyList<string> Extensions => throw new NotImplementedException();
}
