// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Lupe.Core.Model;

namespace Lupe.Services;

/// <summary>Umwandlung zwischen <see cref="PixelImage"/> (Core) und Avalonia-Bildern.</summary>
internal static class Images
{
    /// <summary>
    /// Als Avalonia-Bild. Undurchsichtig – das vierte Byte zählt nicht: Ein <see cref="PixelImage"/> ist undurchsichtig
    /// gemeint, und wo eine Quelle den Alphakanal doch auf 0 lässt (GDI, manche Kameratreiber), bliebe das Bild sonst unsichtbar.
    /// </summary>
    public static WriteableBitmap ToBitmap(PixelImage image)
    {
        var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        Write(bitmap, image);
        return bitmap;
    }

    /// <summary>Schreibt ein Bild gleicher Größe in eine vorhandene Bitmap (Kamera-Vorschau, ohne neue Bitmap je Bild).</summary>
    public static void Write(WriteableBitmap bitmap, PixelImage image)
    {
        using var buffer = bitmap.Lock();
        int rowBytes = image.Width * 4;
        if (buffer.RowBytes == image.Stride && image.Stride == rowBytes)
        {
            Marshal.Copy(image.Bgra, 0, buffer.Address, rowBytes * image.Height);
            return;
        }
        for (int y = 0; y < image.Height; y++)
            Marshal.Copy(image.Bgra, y * image.Stride, buffer.Address + y * buffer.RowBytes, rowBytes);
    }

    /// <summary>
    /// Avalonia-Bild → BGRA. Durchsichtiges wird auf Weiß verrechnet (dunkler Code auf transparentem Grund bleibt
    /// erkennbar). Ist der Alphakanal überall 0 – das liefern manche Programme in der Zwischenablage –, gilt das
    /// Bild als undurchsichtig.
    /// </summary>
    public static PixelImage FromBitmap(Bitmap source)
    {
        int width = source.PixelSize.Width, height = source.PixelSize.Height, stride = width * 4;
        var px = new byte[stride * height];
        var handle = GCHandle.Alloc(px, GCHandleType.Pinned);
        try
        {
            source.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), px.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        // Skia liefert je nach System BGRA oder RGBA, vormultipliziert oder nicht
        bool rgba = source.Format is { } format && format == PixelFormat.Rgba8888;
        bool premultiplied = source.AlphaFormat is null or AlphaFormat.Premul;
        if (rgba)
            for (int i = 0; i < px.Length; i += 4) (px[i], px[i + 2]) = (px[i + 2], px[i]);

        bool anyAlpha = false;
        for (int i = 3; i < px.Length; i += 4)
        {
            if (px[i] != 0) { anyAlpha = true; break; }
        }
        for (int i = 0; i < px.Length; i += 4)
        {
            int a = anyAlpha ? px[i + 3] : 255;
            if (a < 255)
            {
                int white = 255 - a;
                for (int c = 0; c < 3; c++)
                    px[i + c] = (byte)Math.Min(255, (premultiplied ? px[i + c] : px[i + c] * a / 255) + white);
            }
            px[i + 3] = 255;
        }
        return new PixelImage(width, height, px, stride);
    }

    /// <summary>Ausschnitt (wird auf das Bild begrenzt). null, wenn nichts übrig bleibt.</summary>
    public static PixelImage? Crop(PixelImage image, int x, int y, int width, int height)
    {
        int left = Math.Clamp(x, 0, image.Width), top = Math.Clamp(y, 0, image.Height);
        int right = Math.Clamp(x + width, 0, image.Width), bottom = Math.Clamp(y + height, 0, image.Height);
        int w = right - left, h = bottom - top;
        if (w <= 0 || h <= 0) return null;
        var px = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
            Buffer.BlockCopy(image.Bgra, (top + row) * image.Stride + left * 4, px, row * w * 4, w * 4);
        return new PixelImage(w, h, px);
    }
}
