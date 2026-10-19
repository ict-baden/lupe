// SPDX-License-Identifier: MIT
using System.Numerics;
using System.Runtime.InteropServices;
using Lupe.Core.Camera;
using Lupe.Core.Model;
using Lupe.Core.Resources;
using WinRT;
using Windows.Graphics.Imaging;

namespace Lupe.Platform.Windows.Camera;

/// <summary>Kamerabild (<see cref="SoftwareBitmap"/>) → <see cref="PixelImage"/>: BGRA8, dicht gepackt, Alpha 255.</summary>
internal static class FrameConverter
{
    /// <summary>
    /// Kopiert das Bild in einen eigenen Puffer (das Kamerabild selbst wird danach freigegeben). Andere Pixelformate
    /// (NV12, YUY2, Gray8, RGBA …) wandelt Windows vorher nach BGRA8 um. Kamerabilder sind undurchsichtig; was immer im
    /// Alphakanal steht (oft 0), wird auf 255 gesetzt, damit Vorschau und Erkennung ein normales Bild sehen.
    /// </summary>
    public static PixelImage ToPixelImage(SoftwareBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8) return CopyBgra(bitmap);
        using var converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
        return CopyBgra(converted);
    }

    private static unsafe PixelImage CopyBgra(SoftwareBitmap bitmap)
    {
        using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        var plane = buffer.GetPlaneDescription(0);
        int width = Math.Min(plane.Width, bitmap.PixelWidth);
        int height = Math.Min(plane.Height, bitmap.PixelHeight);
        if (width <= 0 || height <= 0) throw new InvalidOperationException(Strings.Win_Camera_FrameEmpty);

        using var reference = buffer.CreateReference();
        var access = reference.As<Native.IMemoryBufferByteAccess>();
        try
        {
            access.GetBuffer(out byte* data, out uint capacity);
            if (data == null) throw new InvalidOperationException(Strings.Win_Camera_FrameMemoryUnreadable);

            int rowBytes = width * 4;
            var pixels = GC.AllocateUninitializedArray<byte>(rowBytes * height);
            for (int y = 0; y < height; y++)
            {
                long offset = plane.StartIndex + (long)y * plane.Stride;
                if (offset < 0 || offset + rowBytes > capacity)
                    throw new InvalidOperationException(Strings.Win_Camera_FrameUnexpectedLayout);
                new ReadOnlySpan<byte>(data + offset, rowBytes).CopyTo(pixels.AsSpan(y * rowBytes, rowBytes));
            }
            MakeOpaque(pixels);
            return new PixelImage(width, height, pixels);
        }
        finally
        {
            // Der COM-Wrapper hielte die Puffer-Referenz sonst bis zur nächsten Garbage Collection – bei 30 Bildern/s viele
            if (Marshal.IsComObject(access)) Marshal.ReleaseComObject(access);
        }
    }

    /// <summary>Setzt den Alphakanal jedes BGRA-Pixels auf 255 (vektorisiert).</summary>
    internal static void MakeOpaque(Span<byte> bgra)
    {
        var px = MemoryMarshal.Cast<byte, uint>(bgra);
        // BGRA im Speicher = 0xAARRGGBB als Little-Endian-uint
        const uint alpha = 0xFF000000u;
        int i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var mask = new Vector<uint>(alpha);
            for (; i <= px.Length - Vector<uint>.Count; i += Vector<uint>.Count)
                (new Vector<uint>(px[i..]) | mask).CopyTo(px[i..]);
        }
        for (; i < px.Length; i++) px[i] |= alpha;
    }
}
