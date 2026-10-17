using Lupe.Core.Model;
using Lupe.Core.Platform;

namespace Lupe.Platform.Linux.Imaging;

/// <summary>Bilddateien unter Linux – noch nicht umgesetzt.</summary>
public sealed class LinuxImageDecoder : IImageDecoder
{
    public IReadOnlyList<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".bmp", ".gif"];

    public Task<PixelImage> DecodeAsync(Stream stream, string name, int maxPixels, CancellationToken ct) =>
        throw new InvalidDataException($"„{name}“ kann nicht gelesen werden: Bilddateien sind unter Linux noch nicht verfügbar.");

    public Exception Translate(Exception ex, string name) =>
        new InvalidDataException($"„{name}“ konnte nicht als Bild gelesen werden ({ex.Message.Trim()}).", ex);
}
