// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using Lupe.Core.Model;
using Zx = ZXingCpp;

namespace Lupe.Tests.TestSupport;

/// <summary>
/// QR-Codes direkt mit zxing-cpp lesen – unabhängig vom Baustein „decoding“ (keine Stufen, keine Formatnamen von Lupe).
/// Für Kamera-, Bildschirm- und Schleifentests, die prüfen wollen, was im Bild steht, nicht wie Lupe es findet.
/// </summary>
public static class ReferenceReader
{
    /// <summary>Alle QR-Codes im Bild (Format QR_CODE, vier Ecken als Punkte).</summary>
    public static IReadOnlyList<DecodedCode> ReadQr(PixelImage image)
    {
        using var options = new Zx.ReaderOptions
        {
            Formats = new Zx.BarcodeFormats(Zx.BarcodeFormat.QRCodeModel1, Zx.BarcodeFormat.QRCodeModel2),
            TryHarder = true,
            TextMode = Zx.TextMode.Plain,
        };
        Zx.Barcode[] barcodes;
        var handle = GCHandle.Alloc(image.Bgra, GCHandleType.Pinned);
        try
        {
            var view = new Zx.ImageView(handle.AddrOfPinnedObject(), image.Width, image.Height, Zx.ImageFormat.BGRA, image.Stride);
            barcodes = Zx.BarcodeReader.Read(view, options);
            GC.KeepAlive(view);
        }
        finally
        {
            handle.Free();
        }

        var result = new List<DecodedCode>();
        foreach (var b in barcodes)
        {
            using (b)
            {
                var p = b.Position;
                result.Add(new DecodedCode
                {
                    Text = b.Text,
                    Format = "QR_CODE",
                    Points = [(p.TopLeft.X, p.TopLeft.Y), (p.TopRight.X, p.TopRight.Y), (p.BottomRight.X, p.BottomRight.Y), (p.BottomLeft.X, p.BottomLeft.Y)],
                    RawBytes = b.Bytes,
                });
            }
        }
        return result;
    }
}
