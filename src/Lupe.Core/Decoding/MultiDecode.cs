using ZXing;
using ZXing.Common;
using ZXing.Multi.QrCode;

namespace Lupe.Core.Decoding;

/// <summary>Ein Treffer samt Versatz des Teilbilds, in dem er gefunden wurde (Punkte bleiben unangetastet).</summary>
internal readonly record struct Found(Result Result, int OffsetX, int OffsetY);

/// <summary>
/// Sucht alle Codes in einem Graustufenbild. Wie ZXings GenericMultipleBarcodeReader: einen Code finden, dann links,
/// rechts, oberhalb und unterhalb davon weitersuchen – aber Doppelte nach Text und Format (nicht nur Text) erkennen,
/// Punkttypen erhalten (für die Code-Ecken) und zwischendurch den Abbruch prüfen. QR-Codes zusätzlich mit dem
/// QR-Mehrfachleser, der auch dicht beieinanderliegende Codes zuverlässig trennt, und Data Matrix/Aztec zusätzlich
/// über <see cref="WindowSearch"/>.
/// </summary>
internal static class MultiDecode
{
    private const int MaxDepth = 4;
    private const int MinDimensionToRecur = 30;

    public static List<Found> Run(GrayImage image, BarcodeFormat mask, CancellationToken ct)
    {
        var found = new List<Found>();
        var source = new GrayLuminanceSource(image, ct);
        var bitmap = new BinaryBitmap(new HybridBinarizer(source));
        var hints = FormatCatalog.Hints(mask);

        if (FormatCatalog.Has(mask, BarcodeFormat.QR_CODE))
        {
            var qr = DecoderSupport.Safe(() => new QRCodeMultiReader().decodeMultiple(bitmap, hints));
            if (qr != null)
                foreach (var r in qr)
                    if (DecoderSupport.HasText(r)) Add(found, new Found(r, 0, 0));
        }

        var reader = new FormatReader(mask, hints);
        if (!reader.IsEmpty) Recurse(bitmap, reader, hints, found, 0, 0, 0, ct);

        // Data Matrix und Aztec abseits der Bildmitte
        WindowSearch.Run(bitmap, mask, hints, found, ct);
        return found;
    }

    private static void Recurse(BinaryBitmap image, FormatReader reader, IDictionary<DecodeHintType, object> hints,
        List<Found> found, int offsetX, int offsetY, int depth, CancellationToken ct)
    {
        if (depth > MaxDepth) return;
        ct.ThrowIfCancellationRequested();
        var result = reader.decode(image, hints);
        if (result == null) return;
        Add(found, new Found(result, offsetX, offsetY));

        var points = result.ResultPoints;
        if (points == null || points.Length == 0) return;
        int width = image.Width, height = image.Height;
        float minX = width, minY = height, maxX = 0, maxY = 0;
        foreach (var p in points)
        {
            if (p == null) continue;
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }
        if (maxX < minX || maxY < minY) return;
        int left = Math.Clamp((int)minX, 0, width), top = Math.Clamp((int)minY, 0, height);
        int right = Math.Clamp((int)maxX, 0, width), bottom = Math.Clamp((int)maxY, 0, height);

        // Links, oberhalb, rechts und unterhalb des gefundenen Codes weitersuchen
        if (left > MinDimensionToRecur)
            Recurse(image.crop(0, 0, left, height), reader, hints, found, offsetX, offsetY, depth + 1, ct);
        if (top > MinDimensionToRecur)
            Recurse(image.crop(0, 0, width, top), reader, hints, found, offsetX, offsetY, depth + 1, ct);
        if (right < width - MinDimensionToRecur)
            Recurse(image.crop(right, 0, width - right, height), reader, hints, found, offsetX + right, offsetY, depth + 1, ct);
        if (bottom < height - MinDimensionToRecur)
            Recurse(image.crop(0, bottom, width, height - bottom), reader, hints, found, offsetX, offsetY + bottom, depth + 1, ct);
    }

    internal static void Add(List<Found> found, Found item)
    {
        foreach (var f in found)
            if (f.Result.BarcodeFormat == item.Result.BarcodeFormat && string.Equals(f.Result.Text, item.Result.Text, StringComparison.Ordinal))
                return;
        found.Add(item);
    }
}
