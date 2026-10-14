using System.Numerics;
using ZXing;
using ZXing.Aztec;
using ZXing.Common;
using ZXing.Datamatrix;

namespace Lupe.Core.Decoding;

/// <summary>
/// Sucht Data Matrix und Aztec auch abseits der Bildmitte. ZXings Detektoren für beide Formate gehen von der Bildmitte
/// aus und finden einen Code in einem unruhigen Bild (Bildschirmfoto, Foto einer Packung) sonst nur, wenn er zufällig
/// dort liegt. Hier werden in der Binarisierung Flächen mit dichter Struktur in beiden Richtungen gesucht (viele
/// Hell-Dunkel-Wechsel waagerecht und senkrecht, gezählt je 8×8-Zelle, bewertet in 16- und 32-Pixel-Fenstern),
/// zusammenhängende, annähernd rechteckig gefüllte Flächen als Kandidaten genommen und jeweils ein Fenster mit dem
/// Kandidaten in der Mitte gelesen – aus der vorhandenen Bitmatrix, ohne neu zu binarisieren.
/// </summary>
internal static class WindowSearch
{
    internal const int Cell = 8;
    /// <summary>Höchstzahl der Fenster je Bild – Textblöcke ergeben auch Kandidaten, die bleiben billig.</summary>
    internal const int MaxWindows = 48;
    /// <summary>Mindestzahl Hell-Dunkel-Wechsel je Richtung in einem 16×16-Fenster.</summary>
    private const int MinTransitions = 8;

    public static void Run(BinaryBitmap image, BarcodeFormat mask, IDictionary<DecodeHintType, object> hints, List<Found> found, CancellationToken ct)
    {
        bool dm = FormatCatalog.Has(mask, BarcodeFormat.DATA_MATRIX), aztec = FormatCatalog.Has(mask, BarcodeFormat.AZTEC);
        if (!dm && !aztec) return;
        var matrix = image.BlackMatrix;
        if (matrix == null) return;

        foreach (var (x, y, w, h) in Candidates(matrix, ct))
        {
            ct.ThrowIfCancellationRequested();
            if (Covered(found, x + w / 2f, y + h / 2f)) continue;
            var window = new BinaryBitmap(new MatrixBinarizer(CropBits(matrix, x, y, w, h)));
            Result? result = null;
            if (dm) result = DecoderSupport.Safe(() => new DataMatrixReader().decode(window, hints));
            if (!DecoderSupport.HasText(result) && aztec) result = DecoderSupport.Safe(() => new AztecReader().decode(window, hints));
            if (DecoderSupport.HasText(result)) MultiDecode.Add(found, new Found(result, x, y));
        }
    }

    /// <summary>Ausschnitt einer Bitmatrix (wortweise, ohne neu zu binarisieren).</summary>
    internal static BitMatrix CropBits(BitMatrix matrix, int left, int top, int width, int height)
    {
        var result = new BitMatrix(width, height);
        var source = new BitArray(matrix.Width);
        var target = new BitArray(width);
        int words = (width + 31) / 32, first = left >> 5, shift = left & 31, extra = words * 32 - width;
        for (int y = 0; y < height; y++)
        {
            source = matrix.getRow(top + y, source);
            int[] s = source.Array, d = target.Array;
            for (int k = 0; k < words; k++)
            {
                int i = first + k;
                uint lo = i < s.Length ? (uint)s[i] >> shift : 0;
                uint hi = shift != 0 && i + 1 < s.Length ? (uint)s[i + 1] << (32 - shift) : 0;
                d[k] = (int)(lo | hi);
            }
            if (extra > 0) d[words - 1] &= (int)(uint.MaxValue >> extra);
            result.setRow(y, target);
        }
        return result;
    }

    /// <summary>Liegt der Punkt in einem schon gefundenen Data-Matrix- oder Aztec-Code? Dann lohnt das Fenster nicht.</summary>
    private static bool Covered(List<Found> found, float px, float py)
    {
        foreach (var f in found)
        {
            if (f.Result.BarcodeFormat is not (BarcodeFormat.DATA_MATRIX or BarcodeFormat.AZTEC)) continue;
            var points = f.Result.ResultPoints;
            if (points == null) continue;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in points)
            {
                if (p == null) continue;
                minX = Math.Min(minX, p.X + f.OffsetX); maxX = Math.Max(maxX, p.X + f.OffsetX);
                minY = Math.Min(minY, p.Y + f.OffsetY); maxY = Math.Max(maxY, p.Y + f.OffsetY);
            }
            if (px >= minX && px <= maxX && py >= minY && py <= maxY) return true;
        }
        return false;
    }

    /// <summary>
    /// Fenster (Pixel) um codeverdächtige Flächen, die besten zuerst; jedes symmetrisch um die Fläche. Ausgewertet in
    /// zwei Maßstäben (16- und 32-Pixel-Fenster), damit kleine Codes und solche mit großen Modulen auffallen.
    /// </summary>
    internal static List<(int X, int Y, int Width, int Height)> Candidates(BitMatrix matrix, CancellationToken ct = default)
    {
        int width = matrix.Width, height = matrix.Height;
        int cols = width / Cell, rows = height / Cell;
        var windows = new List<(int X, int Y, int Width, int Height)>();
        if (cols < 2 || rows < 2) return windows;

        var counts = Count(matrix, cols, rows, ct);
        var scored = new List<(double Score, int X, int Y, int Width, int Height)>();
        foreach (int k in Scales)
        {
            ct.ThrowIfCancellationRequested();
            var textured = Textured(counts, cols, rows, k);
            foreach (var (minX, minY, maxX, maxY, count) in Components(textured, cols, rows))
            {
                // Codes sind kompakt und dicht gefüllt, Textzeilen lang und schmal, Textblöcke löchrig
                int bw = maxX - minX + 1, bh = maxY - minY + 1;
                if (bw < 2 || bh < 2) continue;
                double aspect = (double)Math.Min(bw, bh) / Math.Max(bw, bh);
                double fill = (double)count / (bw * bh);
                if (aspect < 0.2 || fill < 0.45) continue;
                // Mittelpunkt und halbe Ausdehnung in Pixeln, plus Rand für die Ruhezone
                double cx = (minX + maxX + 1) * Cell / 2.0, cy = (minY + maxY + 1) * Cell / 2.0;
                double half = Math.Max(bw, bh) * Cell / 2.0;
                double reach = half + Math.Max(16, half * 0.4);
                // Symmetrisch bleiben, damit der Detektor in der Fenstermitte beginnt
                double rx = Math.Min(reach, Math.Min(cx, width - cx)), ry = Math.Min(reach, Math.Min(cy, height - cy));
                int x0 = (int)Math.Floor(cx - rx), y0 = (int)Math.Floor(cy - ry);
                int w = Math.Min(width - x0, (int)Math.Ceiling(2 * rx)), h = Math.Min(height - y0, (int)Math.Ceiling(2 * ry));
                if (w >= 2 * Cell && h >= 2 * Cell) scored.Add((fill * aspect * Math.Sqrt(count), x0, y0, w, h));
            }
        }

        // Beste zuerst; ein Fenster, dessen Mitte schon mitten in einem ähnlich großen liegt, bringt nichts Neues
        foreach (var c in scored.OrderByDescending(s => s.Score))
        {
            double mx = c.X + c.Width / 2.0, my = c.Y + c.Height / 2.0;
            bool duplicate = windows.Exists(o =>
                Math.Abs(o.X + o.Width / 2.0 - mx) < o.Width / 6.0 && Math.Abs(o.Y + o.Height / 2.0 - my) < o.Height / 6.0
                && c.Width < o.Width * 1.5 && c.Width > o.Width / 1.5);
            if (duplicate) continue;
            windows.Add((c.X, c.Y, c.Width, c.Height));
            if (windows.Count == MaxWindows) break;
        }
        return windows;
    }

    /// <summary>Kantenlänge der Bewertungsfenster in Zellen (2 = 16 Pixel, 4 = 32 Pixel).</summary>
    private static readonly int[] Scales = [2, 4];

    /// <summary>Zusammenhängende markierte Zellen (8er-Nachbarschaft) als Rechteck samt Zellenzahl.</summary>
    private static List<(int MinX, int MinY, int MaxX, int MaxY, int Count)> Components(bool[] marked, int cols, int rows)
    {
        var seen = new bool[cols * rows];
        var stack = new Stack<int>();
        var components = new List<(int, int, int, int, int)>();
        for (int start = 0; start < seen.Length; start++)
        {
            if (!marked[start] || seen[start]) continue;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, count = 0;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int c = stack.Pop(), cx = c % cols, cy = c / cols;
                count++;
                minX = Math.Min(minX, cx); maxX = Math.Max(maxX, cx);
                minY = Math.Min(minY, cy); maxY = Math.Max(maxY, cy);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;
                        int n = ny * cols + nx;
                        if (marked[n] && !seen[n]) { seen[n] = true; stack.Push(n); }
                    }
            }
            components.Add((minX, minY, maxX, maxY, count));
        }
        return components;
    }

    /// <summary>Hell-Dunkel-Wechsel waagerecht und senkrecht sowie schwarze Pixel je 8×8-Zelle.</summary>
    private static (int[] Horizontal, int[] Vertical, int[] Black) Count(BitMatrix matrix, int cols, int rows, CancellationToken ct)
    {
        int width = matrix.Width;
        var horizontal = new int[cols * rows];
        var vertical = new int[cols * rows];
        var black = new int[cols * rows];
        var row = new BitArray(width);
        var previous = new int[(width + 31) / 32];
        bool havePrevious = false;

        for (int y = 0; y < rows * Cell; y++)
        {
            if ((y & 63) == 0) ct.ThrowIfCancellationRequested();
            row = matrix.getRow(y, row);
            var bits = row.Array;
            int baseIndex = (y / Cell) * cols;
            for (int cx = 0; cx < cols; cx++)
            {
                int word = cx >> 2, shift = (cx & 3) * 8;
                uint w = (uint)bits[word];
                uint next = word + 1 < bits.Length ? (uint)bits[word + 1] : 0;
                // Wechsel zwischen Bit i und i+1 (bei Bit 31 mit Bit 0 des nächsten Worts)
                uint t = w ^ ((w >> 1) | (next << 31));
                int i = baseIndex + cx;
                horizontal[i] += BitOperations.PopCount((t >> shift) & 0xFF);
                black[i] += BitOperations.PopCount((w >> shift) & 0xFF);
                if (havePrevious)
                    vertical[i] += BitOperations.PopCount(((w ^ (uint)previous[word]) >> shift) & 0xFF);
            }
            Array.Copy(bits, previous, Math.Min(bits.Length, previous.Length));
            havePrevious = true;
        }
        return (horizontal, vertical, black);
    }

    /// <summary>
    /// Strukturiert ist eine Zelle, wenn das k×k-Zellen-Fenster um sie genug Wechsel in beiden Richtungen hat und weder
    /// fast leer noch fast voll ist. Größere Fenster erfassen auch Codes mit großen Modulen.
    /// </summary>
    private static bool[] Textured((int[] Horizontal, int[] Vertical, int[] Black) counts, int cols, int rows, int k)
    {
        var textured = new bool[cols * rows];
        int from = -(k - 1) / 2, to = k / 2, minTransitions = MinTransitions * k / 2;
        for (int cy = 0; cy < rows; cy++)
            for (int cx = 0; cx < cols; cx++)
            {
                int h = 0, v = 0, b = 0, n = 0;
                for (int dy = from; dy <= to; dy++)
                {
                    int y = cy + dy;
                    if (y < 0 || y >= rows) continue;
                    for (int dx = from; dx <= to; dx++)
                    {
                        int x = cx + dx;
                        if (x < 0 || x >= cols) continue;
                        int i = y * cols + x;
                        h += counts.Horizontal[i]; v += counts.Vertical[i]; b += counts.Black[i]; n++;
                    }
                }
                int area = n * Cell * Cell;
                textured[cy * cols + cx] = h >= minTransitions && v >= minTransitions && b >= area / 10 && b <= area * 9 / 10;
            }
        return textured;
    }
}

/// <summary>Binarisierer mit fertiger Bitmatrix – für Fenster, die nicht neu binarisiert werden sollen.</summary>
internal sealed class MatrixBinarizer(BitMatrix matrix) : Binarizer(new SizeOnlySource(matrix.Width, matrix.Height))
{
    public override BitMatrix BlackMatrix => matrix;

    public override BitArray getBlackRow(int y, BitArray row) => matrix.getRow(y, row);

    public override Binarizer createBinarizer(LuminanceSource source) =>
        throw new NotSupportedException("Fertige Bitmatrix lässt sich nicht neu binarisieren.");

    /// <summary>Nur die Größe; Pixel gibt es hier nicht.</summary>
    private sealed class SizeOnlySource(int width, int height) : LuminanceSource(width, height)
    {
        public override byte[] getRow(int y, byte[] row) => throw new NotSupportedException();
        public override byte[] Matrix => throw new NotSupportedException();
    }
}
