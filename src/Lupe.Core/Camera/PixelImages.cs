using Lupe.Core.Model;

namespace Lupe.Core.Camera;

/// <summary>Hilfen rund um <see cref="PixelImage"/> für den Kamera-Baustein.</summary>
internal static class PixelImages
{
    /// <summary>Eigenständige Kopie (gleicher Stride, nur der genutzte Teil des Puffers).</summary>
    public static PixelImage Copy(PixelImage image)
    {
        int length = checked(image.Stride * image.Height);
        var copy = GC.AllocateUninitializedArray<byte>(length);
        Buffer.BlockCopy(image.Bgra, 0, copy, 0, length);
        return new PixelImage(image.Width, image.Height, copy, image.Stride);
    }
}
