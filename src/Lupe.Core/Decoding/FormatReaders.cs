using ZXing;
using ZXing.Aztec;
using ZXing.Common;
using ZXing.Datamatrix;
using ZXing.IMB;
using ZXing.Maxicode;
using ZXing.OneD;
using ZXing.OneD.RSS;
using ZXing.OneD.RSS.Expanded;
using ZXing.PDF417;
using ZXing.QrCode;

namespace Lupe.Core.Decoding;

/// <summary>
/// Welche Formate gelesen werden und mit welchen ZXing-Lesern. Eigene Zusammenstellung statt MultiFormatReader, damit
/// die Formatliste genau gilt (MultiFormatReader fällt bei manchen Listen still auf „alle“ zurück).
/// <para>
/// Bewusst nicht dabei: MSI und Pharmacode – deren Leser „erkennen“ in fast jedem Bildschirmfoto, Kamerabild und
/// Rauschen Phantomcodes (z. B. „000“) und machen die Suche bis zu 30-mal langsamer; Plessey kann ZXing.Net nur
/// schreiben, nicht lesen.
/// </para>
/// </summary>
internal static class FormatCatalog
{
    /// <summary>Alle Formate, für die ZXing.Net einen Leser hat – 2D zuerst, dann 1D (gängige vor seltenen).</summary>
    public static readonly BarcodeFormat[] All =
    [
        BarcodeFormat.QR_CODE, BarcodeFormat.DATA_MATRIX, BarcodeFormat.AZTEC, BarcodeFormat.PDF_417, BarcodeFormat.MAXICODE,
        BarcodeFormat.CODE_128, BarcodeFormat.CODE_39, BarcodeFormat.CODE_93, BarcodeFormat.CODABAR, BarcodeFormat.ITF,
        BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.UPC_A, BarcodeFormat.UPC_E,
        BarcodeFormat.RSS_14, BarcodeFormat.RSS_EXPANDED, BarcodeFormat.IMB,
    ];

    public static readonly IReadOnlyList<string> AllNames = Array.AsReadOnly(All.Select(f => f.ToString()).ToArray());

    /// <summary>Alle Formate als Bitmaske (die ZXing-Werte sind Zweierpotenzen).</summary>
    public static readonly BarcodeFormat AllMask = All.Aggregate((BarcodeFormat)0, (m, f) => m | f);

    /// <summary>
    /// Formatnamen → Maske. Groß/Klein und Leerzeichen egal, „-“ und „ “ gelten als „_“. Unbekannte Namen werden
    /// übergangen; bleibt nichts Bekanntes übrig, gelten alle Formate (lieber zu viel als stillschweigend nichts erkennen).
    /// </summary>
    public static BarcodeFormat Parse(IReadOnlyList<string>? names)
    {
        if (names is null || names.Count == 0) return AllMask;
        BarcodeFormat mask = 0;
        foreach (var raw in names)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var name = raw.Trim().Replace('-', '_').Replace(' ', '_');
            foreach (var f in All)
                if (string.Equals(f.ToString(), name, StringComparison.OrdinalIgnoreCase)) mask |= f;
        }
        return mask == 0 ? AllMask : mask;
    }

    public static bool Has(BarcodeFormat mask, BarcodeFormat format) => (mask & format) == format;

    public const BarcodeFormat OneDMask =
        BarcodeFormat.CODE_128 | BarcodeFormat.CODE_39 | BarcodeFormat.CODE_93 | BarcodeFormat.CODABAR | BarcodeFormat.ITF |
        BarcodeFormat.EAN_13 | BarcodeFormat.EAN_8 | BarcodeFormat.UPC_A | BarcodeFormat.UPC_E |
        BarcodeFormat.RSS_14 | BarcodeFormat.RSS_EXPANDED;

    /// <summary>Hinweise für ZXing: gründlich suchen, nur die erlaubten Formate.</summary>
    public static Dictionary<DecodeHintType, object> Hints(BarcodeFormat mask) => new()
    {
        [DecodeHintType.TRY_HARDER] = true,
        [DecodeHintType.POSSIBLE_FORMATS] = All.Where(f => Has(mask, f)).ToList(),
    };
}

/// <summary>
/// Ein Leser für die erlaubten Formate: probiert die 2D-Leser und danach die 1D-Leser und liefert den ersten Treffer.
/// Ausnahmen aus ZXing (kommt bei kaputten Daten vereinzelt vor) gelten als „nichts gefunden“.
/// </summary>
internal sealed class FormatReader : Reader
{
    private readonly List<Reader> _readers = [];

    public FormatReader(BarcodeFormat mask, IDictionary<DecodeHintType, object> hints)
    {
        if (FormatCatalog.Has(mask, BarcodeFormat.QR_CODE)) _readers.Add(new QRCodeReader());
        if (FormatCatalog.Has(mask, BarcodeFormat.DATA_MATRIX)) _readers.Add(new DataMatrixReader());
        if (FormatCatalog.Has(mask, BarcodeFormat.AZTEC)) _readers.Add(new AztecReader());
        if (FormatCatalog.Has(mask, BarcodeFormat.PDF_417)) _readers.Add(new UnrotatedPdf417Reader());
        if (FormatCatalog.Has(mask, BarcodeFormat.MAXICODE)) _readers.Add(new MaxiCodeReader());
        if ((mask & FormatCatalog.OneDMask) != 0) _readers.Add(new OneDFormatsReader(mask, hints));
        if (FormatCatalog.Has(mask, BarcodeFormat.IMB)) _readers.Add(new IMBReader());
    }

    public bool IsEmpty => _readers.Count == 0;

    public Result? decode(BinaryBitmap image) => decode(image, null);

    public Result? decode(BinaryBitmap image, IDictionary<DecodeHintType, object>? hints)
    {
        foreach (var reader in _readers)
        {
            reader.reset();
            var result = DecoderSupport.Safe(() => reader.decode(image, hints));
            if (result is { Text: not null }) return result;
        }
        return null;
    }

    public void reset()
    {
        foreach (var reader in _readers) reader.reset();
    }
}

/// <summary>
/// Alle erlaubten 1D-Formate zeilenweise; Drehung um 90° (TRY_HARDER) übernimmt <see cref="OneDReader"/>.
/// <para>
/// Anders als bei ZXing zählt ein Treffer erst, wenn derselbe Inhalt auf mindestens <see cref="MinLineCount"/> Zeilen
/// gelesen wurde (wie „MinLineCount“ in zxing-cpp). Schräg angeschnittene Zeilen ergeben sonst gelegentlich
/// Phantom-Codes mit zufällig passender Prüfziffer (typisch: EAN-8 oder UPC-E mitten in einem anderen Strichcode).
/// </para>
/// </summary>
internal sealed class OneDFormatsReader : OneDReader
{
    internal const int MinLineCount = 2;

    private readonly OneDReader[] _readers;

    public OneDFormatsReader(BarcodeFormat mask, IDictionary<DecodeHintType, object> hints)
    {
        var list = new List<OneDReader>();
        bool Has(BarcodeFormat f) => FormatCatalog.Has(mask, f);
        if (Has(BarcodeFormat.EAN_13) || Has(BarcodeFormat.UPC_A) || Has(BarcodeFormat.EAN_8) || Has(BarcodeFormat.UPC_E))
            list.Add(new MultiFormatUPCEANReader(hints)); // filtert selbst über POSSIBLE_FORMATS
        if (Has(BarcodeFormat.CODE_128)) list.Add(new Code128Reader());
        if (Has(BarcodeFormat.CODE_39)) list.Add(new Code39Reader(usingCheckDigit: false, extendedMode: false));
        if (Has(BarcodeFormat.CODE_93)) list.Add(new Code93Reader());
        if (Has(BarcodeFormat.CODABAR)) list.Add(new CodaBarReader());
        if (Has(BarcodeFormat.ITF)) list.Add(new ITFReader());
        if (Has(BarcodeFormat.RSS_14)) list.Add(new RSS14Reader());
        if (Has(BarcodeFormat.RSS_EXPANDED)) list.Add(new RSSExpandedReader());
        _readers = [.. list];
    }

    /// <summary>Erster Treffer irgendeines Lesers in dieser Zeile (ZXing-Schnittstelle).</summary>
    public override Result? decodeRow(int rowNumber, BitArray row, IDictionary<DecodeHintType, object> hints)
    {
        foreach (var reader in _readers)
        {
            var result = DecodeRow(reader, rowNumber, row, hints);
            if (result != null) return result;
        }
        return null;
    }

    private static Result? DecodeRow(OneDReader reader, int rowNumber, BitArray row, IDictionary<DecodeHintType, object>? hints)
    {
        try
        {
            var result = reader.decodeRow(rowNumber, row, hints);
            return result is { Text: not null } ? result : null;
        }
        catch (Exception ex) when (DecoderSupport.IsZxingFault(ex))
        {
            // Einzelne Leser stolpern selten über kaputte Zeilen – dann eben der nächste
            return null;
        }
    }

    /// <summary>
    /// Zeilen von der Mitte nach außen wie <see cref="OneDReader"/> (bei TRY_HARDER jede 256stel Bildhöhe), jede Zeile
    /// vorwärts und rückwärts und mit allen Lesern; erst ein bestätigter Treffer wird zurückgegeben – mit der Lesezeile
    /// in der Mitte der Striche (für die Markierung), nicht am zufällig zuerst getroffenen Rand.
    /// </summary>
    protected override Result? doDecode(BinaryBitmap image, IDictionary<DecodeHintType, object> hints)
    {
        int width = image.Width, height = image.Height;
        var row = new BitArray(width);
        bool tryHarder = hints != null && hints.ContainsKey(DecodeHintType.TRY_HARDER);
        int rowStep = Math.Max(1, height >> (tryHarder ? 8 : 5));
        int maxLines = tryHarder ? height : 15;
        int middle = height >> 1;
        Dictionary<(BarcodeFormat, string), (Result First, int Row, int Count)>? seen = null;

        for (int x = 0; x < maxLines; x++)
        {
            int steps = (x + 1) >> 1;
            int rowNumber = middle + rowStep * ((x & 1) == 0 ? steps : -steps);
            if (rowNumber < 0 || rowNumber >= height) break;
            // Gleichförmige Zeilen liefert der Binarisierer als null – den Puffer dabei behalten
            if (image.getBlackRow(rowNumber, row) is not { } fetched) continue;
            row = fetched;

            // Pro Zeile zählt jeder Inhalt höchstens einmal (vorwärts und rückwärts gelesen ist dieselbe Zeile)
            HashSet<(BarcodeFormat, string)>? inRow = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (attempt == 1) row.reverse();
                foreach (var reader in _readers)
                {
                    var result = DecodeRow(reader, rowNumber, row, hints);
                    if (result == null) continue;
                    if (attempt == 1) Mirror(result, width);
                    var key = (result.BarcodeFormat, result.Text);
                    inRow ??= [];
                    if (!inRow.Add(key)) continue;
                    seen ??= [];
                    if (!seen.TryGetValue(key, out var entry))
                    {
                        seen[key] = (result, rowNumber, 1);
                        continue;
                    }
                    if (entry.Count + 1 < MinLineCount)
                    {
                        seen[key] = (entry.First, entry.Row, entry.Count + 1);
                        continue;
                    }
                    // RSS-Leser sammeln Paare über Zeilen hinweg – dort nicht nachtasten
                    if (reader is RSS14Reader or RSSExpandedReader) return entry.First;
                    return Centered(image, reader, key, entry.First, entry.Row, result, rowNumber, hints);
                }
            }
        }
        return null;
    }

    /// <summary>Rückwärts gelesene Zeile: Punkte spiegeln wie in <see cref="OneDReader"/>.</summary>
    private static void Mirror(Result result, int width)
    {
        result.putMetadata(ResultMetadataType.ORIENTATION, 180);
        var p = result.ResultPoints;
        if (p is { Length: >= 2 } && p[0] != null && p[1] != null)
        {
            p[0] = new ResultPoint(width - p[0].X - 1, p[0].Y);
            p[1] = new ResultPoint(width - p[1].X - 1, p[1].Y);
        }
    }

    /// <summary>
    /// Tastet von den beiden bestätigenden Zeilen nach oben und unten, bis der Code zweimal nicht mehr gelesen wird, und
    /// liefert das Ergebnis der Zeile, die der Mitte der Striche am nächsten liegt.
    /// </summary>
    private static Result Centered(BinaryBitmap image, OneDReader reader, (BarcodeFormat, string) key,
        Result first, int firstRow, Result second, int secondRow, IDictionary<DecodeHintType, object>? hints)
    {
        int width = image.Width, height = image.Height;
        int step = Math.Max(1, height / 64);
        var hits = new SortedDictionary<int, Result> { [firstRow] = first, [secondRow] = second };
        var row = new BitArray(width);

        Result? Read(int y)
        {
            if (image.getBlackRow(y, row) is not { } fetched) return null;
            row = fetched;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (attempt == 1) row.reverse();
                var r = DecodeRow(reader, y, row, hints);
                if (r == null || (r.BarcodeFormat, r.Text) != key) continue;
                if (attempt == 1) Mirror(r, width);
                return r;
            }
            return null;
        }

        foreach (int direction in (int[])[-1, 1])
        {
            int edge = direction < 0 ? Math.Min(firstRow, secondRow) : Math.Max(firstRow, secondRow);
            for (int y = edge + direction * step, misses = 0; y >= 0 && y < height && misses < 2; y += direction * step)
            {
                var r = Read(y);
                if (r == null) { misses++; continue; }
                hits[y] = r;
                misses = 0;
            }
        }

        int top = hits.Keys.First(), bottom = hits.Keys.Last(), mid = (top + bottom) / 2;
        if (hits.TryGetValue(mid, out var atMid)) return atMid;
        return Read(mid) ?? hits.MinBy(h => Math.Abs(h.Key - mid)).Value;
    }

    public override void reset()
    {
        foreach (Reader reader in _readers) reader.reset();
    }
}

/// <summary>
/// PDF417 mit Punkten im ungedrehten Bild: ZXing sucht PDF417 auch um 90°/180°/270° gedreht, lässt die Eckpunkte
/// aber in den Koordinaten der gedrehten Bitmatrix. Hier werden sie zurückgerechnet.
/// </summary>
internal sealed class UnrotatedPdf417Reader : Reader
{
    private readonly PDF417Reader _inner = new();

    public Result? decode(BinaryBitmap image) => decode(image, null);

    public Result? decode(BinaryBitmap image, IDictionary<DecodeHintType, object>? hints)
    {
        var result = _inner.decode(image, hints);
        if (result != null) FixPoints(result, image.Width, image.Height);
        return result;
    }

    public void reset() => _inner.reset();

    internal static void FixPoints(Result result, int width, int height)
    {
        int rotation = result.ResultMetadata != null && result.ResultMetadata.TryGetValue(ResultMetadataType.ORIENTATION, out var o) && o is int r ? r : 0;
        var points = result.ResultPoints;
        if (rotation % 360 == 0 || points == null) return;
        for (int i = 0; i < points.Length; i++)
        {
            var p = points[i];
            if (p == null) continue;
            // Umkehrung von BitMatrix.rotate180/rotate90 in der Reihenfolge, in der der PDF417-Detektor sie anwendet
            points[i] = rotation switch
            {
                180 => new ResultPoint(width - 1 - p.X, height - 1 - p.Y),
                270 => new ResultPoint(p.Y, height - 1 - p.X),
                90 => new ResultPoint(width - 1 - p.Y, p.X),
                _ => p,
            };
        }
    }
}

internal static class DecoderSupport
{
    /// <summary>Führt einen ZXing-Aufruf aus; Ausnahmen außer Abbruch gelten als „kein Treffer“.</summary>
    public static T? Safe<T>(Func<T?> action) where T : class
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (IsZxingFault(ex))
        {
            System.Diagnostics.Debug.WriteLine($"ZXing-Fehler ignoriert: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Fehler aus dem Inneren von ZXing (Abbruch und Speichermangel zählen nicht dazu).</summary>
    public static bool IsZxingFault(Exception ex) => ex is not (OperationCanceledException or OutOfMemoryException);
}
