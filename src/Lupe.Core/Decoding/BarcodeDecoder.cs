using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Lupe.Core.Model;
using ZXing;
using ZXing.QrCode.Internal;

namespace Lupe.Core.Decoding;

/// <summary>
/// Findet alle QR- und Barcodes in einem Bild (ZXing.Net), auch kleine, gedrehte und invertierte.
/// <para>
/// Ablauf: Das Bild wird einmal in Graustufen umgerechnet (Transparenz auf Weiß) und dann in Stufen durchsucht; die
/// Aufgaben einer Stufe (Bildvariante × normal/invertiert × Lesergruppe) laufen parallel.
/// Stufe 1: das ganze Bild mit weißem Rand (Codes bis an den Bildrand); große Bilder (z. B. 4K-Bildschirmfotos)
/// stattdessen verkleinert als Übersicht plus überlappende Kacheln in voller Auflösung, damit auch ein 60-Pixel-QR-Code
/// gefunden wird. Invertiert (heller Code auf dunklem Grund) nur mit den 2D-Lesern.
/// Data Matrix und Aztec werden zusätzlich abseits der Bildmitte gesucht (<see cref="WindowSearch"/>), 1D-Codes erst
/// gemeldet, wenn sie auf zwei Zeilen gleich gelesen wurden.
/// Stufe 2 nur, wenn bis dahin nichts (außer vielleicht einem schwachen 1D-Bruchstück) gefunden wurde: 1D invertiert,
/// kleine Bilder hochskaliert (1–2 Pixel je Modul), mittlere halbiert (glättet Rauschen und Unschärfe) und gedreht –
/// in 10°-Schritten bis ±40° für Aztec, PDF417 und Data Matrix, ±30° für Strichcodes.
/// </para>
/// <para>
/// Für den Kamerastrom gibt es <see cref="LiveBarcodeDecoder"/> (nur Stufe 1, viel weniger Rechenzeit je Bild).
/// </para>
/// <para>Threadsicher; eine Instanz kann von mehreren Threads gleichzeitig benutzt werden.</para>
/// </summary>
public sealed class BarcodeDecoder
{
    /// <summary>Ab dieser Pixelzahl wird in Übersicht und Kacheln zerlegt (4K: ja, 1440p: nein).</summary>
    internal const int LargeImagePixels = 4_200_000;
    /// <summary>Zielgröße der verkleinerten Übersicht großer Bilder.</summary>
    internal const int OverviewPixels = 2_100_000;
    internal const int TileSize = 1024;
    /// <summary>Größere Bilder werden vor dem Kacheln auf etwa diese Pixelzahl verkleinert (4K bleibt in voller Auflösung).</summary>
    internal const int TileWorkPixels = 12_000_000;
    /// <summary>Höchstzahl der Kacheln (4K: 15, 12 MP: 24).</summary>
    internal const int MaxTiles = 48;
    /// <summary>Überlappung der Kacheln – Codes bis zu dieser Größe liegen sicher ganz in einer Kachel.</summary>
    internal const int TileOverlap = 320;
    /// <summary>Bis zu dieser Kantenlänge wird in Stufe 2 hochskaliert.</summary>
    internal const int UpscaleMaxSide = 1000;
    /// <summary>Bis zu dieser Kantenlänge zusätzlich dreifach.</summary>
    internal const int TripleUpscaleMaxSide = 400;
    /// <summary>
    /// Drehwinkel der Stufe 2. Aztec verträgt in ZXing etwa ±8°, PDF417 etwa ±6°, daher 10°-Schritte bis ±40° (über ±45°
    /// hinaus übernimmt deren eigene 90°-Prüfung).
    /// </summary>
    internal static readonly double[] SkewAngles = [-40, -30, -20, -10, 10, 20, 30, 40];
    /// <summary>Größte Fläche eines gedrehten Bildes (gemessen bei 45°); größere werden vorher verkleinert.</summary>
    internal const int SkewRotatedPixels = 2_400_000;
    /// <summary>1D-Leser vertragen viel mehr Schräglage; ±30° schließt zusammen mit ihrer 90°-Prüfung die Lücken.</summary>
    internal const double OneDSkewAngle = 30;

    /// <summary>2D-Formate, die von gedrehten Varianten profitieren.</summary>
    internal static readonly BarcodeFormat[] SkewTwoDGroups = [BarcodeFormat.DATA_MATRIX | BarcodeFormat.AZTEC, BarcodeFormat.PDF_417];

    /// <summary>
    /// Lesergruppen, die parallel laufen (jede mit eigener Binarisierung), so aufgeteilt, dass sie etwa gleich lange
    /// brauchen. 2D-Codes sind drehungsunabhängig, die 1D-Leser prüfen selbst auch 90° und 180°.
    /// </summary>
    internal static readonly BarcodeFormat[] TwoDGroups =
    [
        BarcodeFormat.QR_CODE | BarcodeFormat.DATA_MATRIX | BarcodeFormat.AZTEC | BarcodeFormat.MAXICODE,
        BarcodeFormat.PDF_417,
    ];

    internal static readonly BarcodeFormat[] OneDGroups =
    [
        BarcodeFormat.EAN_13 | BarcodeFormat.EAN_8 | BarcodeFormat.UPC_A | BarcodeFormat.UPC_E,
        BarcodeFormat.CODE_128 | BarcodeFormat.ITF,
        BarcodeFormat.CODE_39,
        BarcodeFormat.CODE_93 | BarcodeFormat.CODABAR,
        BarcodeFormat.RSS_14 | BarcodeFormat.RSS_EXPANDED,
        BarcodeFormat.IMB,
    ];

    private readonly BarcodeFormat _mask;

    /// <param name="formats">
    /// Erlaubte Formate in ZXing-Schreibweise; null oder leer = alle. Groß/Klein ist egal, unbekannte Namen werden
    /// übergangen (bleibt nichts übrig, gelten alle).
    /// </param>
    public BarcodeDecoder(IReadOnlyList<string>? formats = null) => _mask = FormatCatalog.Parse(formats);

    /// <summary>
    /// Nur die schnelle erste Stufe – für den Kamerastrom (<see cref="LiveBarcodeDecoder"/>),
    /// wo das nächste Bild gleich wieder eine Chance ist und Dauerlast auf allen Kernen nicht sein soll.
    /// </summary>
    internal bool Live { get; init; }

    /// <summary>Alle unterstützten Formate in ZXing-Schreibweise.</summary>
    public static IReadOnlyList<string> AllFormats => FormatCatalog.AllNames;

    /// <summary>Die tatsächlich aktiven Formate.</summary>
    internal IReadOnlyList<string> ActiveFormats => [.. FormatCatalog.All.Where(f => FormatCatalog.Has(_mask, f)).Select(f => f.ToString())];

    /// <summary>
    /// Alle Codes im Bild, ohne Doppelte (gleicher Text und gleiches Format), in Lesereihenfolge (oben vor unten,
    /// links vor rechts); leer, wenn keiner gefunden wurde. Punkte in Pixeln des übergebenen Bildes.
    /// Wirft nur bei <c>null</c> und beim Abbruch über <paramref name="ct"/> (<see cref="OperationCanceledException"/>);
    /// interne Fehler einzelner ZXing-Leser kosten nur deren Teilergebnis.
    /// </summary>
    public IReadOnlyList<DecodedCode> Decode(PixelImage image, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ct.ThrowIfCancellationRequested();
        GrayImage gray;
        try
        {
            gray = GrayImage.FromBgra(image, ct);
        }
        catch (AggregateException ex) when (IsCancellation(ex, ct))
        {
            throw new OperationCanceledException(ct);
        }
        return Decode(gray, ct);
    }

    internal IReadOnlyList<DecodedCode> Decode(GrayImage gray, CancellationToken ct)
    {
        var codes = new List<DecodedCode>();
        var keys = new HashSet<(string Format, string Text)>();
        foreach (var stage in Stages(gray, _mask, Live))
        {
            // Ein schwacher 1D-Treffer (vielleicht nur ein Bruchstück) hält die gründlichere Stufe nicht auf
            if (stage.OnlyIfNothingFound && codes.Exists(c => !IsWeak(c.Format))) break;
            foreach (var (found, map) in RunStage(stage.Tasks, ct))
                foreach (var f in found)
                {
                    var code = ToCode(f, map, gray.Width, gray.Height);
                    if (keys.Add((code.Format, code.Text))) codes.Add(code);
                }
        }
        RemoveFragments(codes);
        return InReadingOrder(codes);
    }

    /// <summary>
    /// 1D-Formate, deren Leser aus einem schräg oder teilweise gelesenen anderen Strichcode gelegentlich ein gültiges
    /// Bruchstück machen: ITF hat keine Prüfziffer, EAN-8 und UPC-E nur eine einzige (jedes zehnte Zufallsmuster passt).
    /// </summary>
    internal static bool IsWeak(string format) => format is nameof(BarcodeFormat.ITF) or nameof(BarcodeFormat.EAN_8) or nameof(BarcodeFormat.UPC_E);

    /// <summary>
    /// Entfernt Bruchstücke: ein schwacher 1D-Code, dessen Text echter Teil des Texts eines anderen 1D-Codes ist, der an
    /// derselben Stelle liegt (z. B. ein Phantom-EAN-8 „60002914“ aus einem schräg gelesenen UPC-A „036000291452“ oder
    /// ein kurzes ITF aus einem langen).
    /// </summary>
    internal static void RemoveFragments(List<DecodedCode> codes)
    {
        codes.RemoveAll(c => IsWeak(c.Format) && codes.Exists(o =>
            !ReferenceEquals(o, c) && IsOneD(o.Format) && o.Text.Length > c.Text.Length
            && o.Text.Contains(c.Text, StringComparison.Ordinal) && Overlaps(c, o)));

        static bool Overlaps(DecodedCode fragment, DecodedCode whole)
        {
            if (fragment.Points.Count == 0 || whole.Points.Count == 0) return true;
            float mx = fragment.Points.Average(p => p.X), my = fragment.Points.Average(p => p.Y);
            float minX = whole.Points.Min(p => p.X), maxX = whole.Points.Max(p => p.X);
            float minY = whole.Points.Min(p => p.Y), maxY = whole.Points.Max(p => p.Y);
            // Strichcodes liefern nur eine Lesezeile – der Code reicht etwa eine halbe Länge darüber und darunter hinaus
            float reach = Math.Max(maxX - minX, maxY - minY) / 2;
            return mx >= minX - reach && mx <= maxX + reach && my >= minY - reach && my <= maxY + reach;
        }
    }

    internal static bool IsOneD(string format) =>
        Enum.TryParse<BarcodeFormat>(format, out var f) && (f & (FormatCatalog.OneDMask | BarcodeFormat.IMB)) != 0;

    // ---------- Stufen ----------

    /// <summary>
    /// Eine Bildvariante (Original, Kachel, skaliert, gedreht). Das Bild entsteht erst bei Bedarf im Arbeitsthread und
    /// wird von allen Aufgaben der Variante geteilt. <see cref="ToOriginal"/> rechnet Punkte ins Original zurück; mit
    /// <c>pad</c> bekommt das Bild einen weißen Rand (Ruhezone für Codes am Bildrand), der beim Zurückrechnen abgezogen wird.
    /// </summary>
    internal sealed class ScanVariant
    {
        private readonly Lazy<GrayImage> _image;
        private readonly Lazy<int> _pad;
        private readonly Lazy<GrayImage> _normal;
        private readonly Lazy<GrayImage> _inverted;

        public ScanVariant(string name, Func<GrayImage> build, PointMap toOriginal, bool pad)
        {
            Name = name;
            ToOriginal = toOriginal;
            _image = new Lazy<GrayImage>(build, LazyThreadSafetyMode.ExecutionAndPublication);
            _pad = new Lazy<int>(() => pad ? PadFor(_image.Value) : 0, LazyThreadSafetyMode.ExecutionAndPublication);
            _normal = new Lazy<GrayImage>(() => _image.Value.Pad(_pad.Value), LazyThreadSafetyMode.ExecutionAndPublication);
            // Erst invertieren, dann rahmen – der Rand muss auch hier weiß sein
            _inverted = new Lazy<GrayImage>(() => _image.Value.Invert().Pad(_pad.Value), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public string Name { get; }
        public PointMap ToOriginal { get; }

        public GrayImage Image(bool inverted) => inverted ? _inverted.Value : _normal.Value;

        /// <summary>Abbildung von Punkten im (gerahmten) Variantenbild ins Original.</summary>
        public PointMap Map => ToOriginal.After(PointMap.ScaleOffset(1, -_pad.Value, -_pad.Value));
    }

    /// <summary>Eine Aufgabe: eine Variante, normal oder invertiert, mit einer Gruppe von Lesern.</summary>
    internal sealed record ScanTask(ScanVariant Variant, bool Inverted, BarcodeFormat Formats);

    internal sealed record Stage(IReadOnlyList<ScanTask> Tasks, bool OnlyIfNothingFound);

    internal static IReadOnlyList<Stage> Stages(GrayImage gray, BarcodeFormat mask, bool live = false)
    {
        int w = gray.Width, h = gray.Height, maxSide = Math.Max(w, h);
        long pixels = (long)w * h;
        bool large = pixels > LargeImagePixels;
        var allGroups = TwoDGroups.Concat(OneDGroups).ToArray();
        var stages = new List<Stage>();

        void Add(List<ScanTask> tasks, ScanVariant v, bool inverted, IEnumerable<BarcodeFormat> groups)
        {
            foreach (var g in groups)
                if ((g & mask) != 0) tasks.Add(new ScanTask(v, inverted, g & mask));
        }

        // Viele Kacheln sind schon genug Parallelität – dort je Kachel nur eine 2D- und eine 1D-Aufgabe, damit nicht
        // jede Lesergruppe dieselbe Kachel erneut binarisiert und dreht.
        BarcodeFormat[] Coarse(BarcodeFormat[] groups) => [groups.Aggregate((BarcodeFormat)0, (a, g) => a | g)];

        // Stufe 1: ganzes Bild bzw. Übersicht + Kacheln. Invertiert nur 2D – helle QR-Codes auf dunklem Grund sind
        // häufig (Dunkelmodus, Displays), helle Strichcodes selten.
        ScanVariant? primary = null;
        var tiles = new List<ScanVariant>();
        if (!large)
        {
            if (Sane(w, h)) primary = new ScanVariant("Original", () => gray, PointMap.Identity, pad: true);
        }
        else
        {
            int factor = FactorFor(pixels, OverviewPixels, w, h);
            if (Sane(w / factor, h / factor))
                primary = new ScanVariant($"Übersicht 1:{factor}", () => gray.Downscale(factor), PointMap.ScaleOffset(factor, 0, 0), pad: true);
            // Riesige Fotos (bis 40 MP) nicht in voller Auflösung kacheln – das wären Hunderte Aufgaben; extrem lange
            // Streifen, die sich auch verkleinert nicht in wenige Kacheln teilen lassen, bekommen keine
            int tf = FactorFor(pixels, TileWorkPixels, w, h);
            while (tf < Math.Min(w, h) && Tiles(w / tf, h / tf, TileSize, TileOverlap).Count() > MaxTiles) tf++;
            var cells = Tiles(w / tf, h / tf, TileSize, TileOverlap).ToList();
            var tileSource = new Lazy<GrayImage>(() => gray.Downscale(tf), LazyThreadSafetyMode.ExecutionAndPublication);
            if (cells.Count <= MaxTiles)
                foreach (var (x, y, tw, th) in cells)
                    tiles.Add(new ScanVariant($"Kachel {x},{y}", () => tileSource.Value.Crop(x, y, tw, th), PointMap.ScaleOffset(tf, x * tf, y * tf), pad: false));
        }
        var first = new List<ScanTask>();
        if (primary != null)
        {
            Add(first, primary, false, allGroups);
            Add(first, primary, true, TwoDGroups);
        }
        foreach (var tile in tiles)
        {
            Add(first, tile, false, Coarse(TwoDGroups));
            Add(first, tile, false, Coarse(OneDGroups));
            Add(first, tile, true, Coarse(TwoDGroups));
        }
        stages.Add(new Stage(first, OnlyIfNothingFound: false));
        if (live) return stages;

        // Stufe 2 – nur, wenn bis dahin nichts gefunden wurde
        var second = new List<ScanTask>();
        if (primary != null) Add(second, primary, true, OneDGroups);
        if (maxSide <= UpscaleMaxSide)
        {
            var up2 = new ScanVariant("Vergrößert ×2", () => gray.Upscale(2), PointMap.ScaleOffset(0.5, 0, 0), pad: true);
            Add(second, up2, false, allGroups);
            Add(second, up2, true, TwoDGroups);
        }
        if (maxSide <= TripleUpscaleMaxSide)
        {
            var up3 = new ScanVariant("Vergrößert ×3", () => gray.Upscale(3), PointMap.ScaleOffset(1.0 / 3, 0, 0), pad: true);
            Add(second, up3, false, allGroups);
            Add(second, up3, true, TwoDGroups);
        }
        if (!large && Math.Min(w, h) >= 200)
        {
            var half = new ScanVariant("Verkleinert 1:2", () => gray.Downscale(2), PointMap.ScaleOffset(2, 0, 0), pad: true);
            Add(second, half, false, allGroups);
            Add(second, half, true, TwoDGroups);
        }

        // Schräglage: Strichcodes, deren Striche keine Bildzeile ganz kreuzt, sowie Aztec und PDF417 (und Data Matrix
        // jenseits von ~40°), deren ZXing-Detektoren nur wenige Grad vertragen; QR ist drehungsunabhängig. Schräg liegen
        // Codes vor allem auf Fotos, und dort sind sie groß – gedreht wird eine verkleinerte Fassung, deren gedrehte Fläche
        // höchstens etwa 2,4 Megapixel hat. Gedreht um 45° ist die Fläche (w + h)² / 2 – bei langen Streifen viel mehr als
        // w·h; Pixelfäden, die sich nicht weit genug verkleinern lassen, werden nicht gedreht.
        int f = FactorFor(Math.Pow(w + h, 2) / 2, SkewRotatedPixels, w, h);
        int sw = w / f, sh = h / f;
        if (Sane(sw, sh) && Math.Pow(sw + sh, 2) / 2 <= 2.0 * SkewRotatedPixels)
        {
            var skewSource = new Lazy<GrayImage>(() => gray.Downscale(f), LazyThreadSafetyMode.ExecutionAndPublication);
            foreach (var angle in SkewAngles)
            {
                var map = PointMap.ScaleOffset(f, 0, 0).After(GrayImage.RotationMap(sw, sh, angle));
                var rotated = new ScanVariant($"Gedreht {angle:+0;-0}°", () => skewSource.Value.Rotate(angle, out _), map, pad: false);
                Add(second, rotated, false, SkewTwoDGroups);
                if (Math.Abs(angle) == OneDSkewAngle) Add(second, rotated, false, OneDGroups);
            }
        }
        if (second.Count > 0) stages.Add(new Stage(second, OnlyIfNothingFound: true));
        return stages;
    }

    /// <summary>
    /// Ganzzahliger Verkleinerungsfaktor, damit <paramref name="area"/> auf höchstens <paramref name="target"/> Pixel
    /// schrumpft – aber nie so weit, dass eine Bildkante unter ein Pixel fällt (extrem schmale Streifen).
    /// </summary>
    internal static int FactorFor(double area, double target, int width, int height) =>
        Math.Clamp((int)Math.Ceiling(Math.Sqrt(area / target)), 1, Math.Min(width, height));

    /// <summary>Längste Kante einer Bildvariante; absurde Streifen (3 000 000 × 2) werden nicht durchsucht.</summary>
    internal const int MaxVariantSide = 32_768;

    private static bool Sane(int width, int height) => width >= 1 && height >= 1 && Math.Max(width, height) <= MaxVariantSide;

    /// <summary>Randbreite für eine Variante: etwa 5 % der längeren Kante, mindestens 8, höchstens 48 Pixel.</summary>
    internal static int PadFor(GrayImage img) => Math.Clamp(Math.Max(img.Width, img.Height) / 20, 8, 48);

    /// <summary>Überlappende Kacheln, die das Bild vollständig abdecken; die letzte Reihe/Spalte schließt bündig ab.</summary>
    internal static IEnumerable<(int X, int Y, int Width, int Height)> Tiles(int width, int height, int size, int overlap)
    {
        int step = Math.Max(1, size - overlap);
        var xs = Starts(width, size, step);
        foreach (int y in Starts(height, size, step))
            foreach (int x in xs)
                yield return (x, y, Math.Min(size, width - x), Math.Min(size, height - y));

        static List<int> Starts(int length, int size, int step)
        {
            var list = new List<int>();
            if (length <= size) { list.Add(0); return list; }
            for (int s = 0; s + size < length; s += step) list.Add(s);
            list.Add(length - size);
            return list;
        }
    }

    private static (List<Found> Found, PointMap Map)[] RunStage(IReadOnlyList<ScanTask> tasks, CancellationToken ct)
    {
        var results = new (List<Found>, PointMap)[tasks.Count];
        try
        {
            // Einzeln verteilen (keine Blöcke), damit ungleich lange Aufgaben die Kerne gleichmäßig auslasten
            var partitioner = Partitioner.Create(Enumerable.Range(0, tasks.Count), EnumerablePartitionerOptions.NoBuffering);
            Parallel.ForEach(partitioner, new ParallelOptions { CancellationToken = ct }, i => results[i] = RunTask(tasks[i], ct));
        }
        catch (AggregateException ex) when (IsCancellation(ex, ct))
        {
            throw new OperationCanceledException(ct);
        }
        return results;
    }

    private static (List<Found>, PointMap) RunTask(ScanTask task, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var image = task.Variant.Image(task.Inverted);
            return (MultiDecode.Run(image, task.Formats, ct), task.Variant.Map);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unerwartete Fehler kosten nur diese eine Aufgabe
            Debug.WriteLine($"Erkennung „{task.Variant.Name}“ ({task.Formats}) fehlgeschlagen: {ex}");
            return ([], PointMap.Identity);
        }
    }

    private static bool IsCancellation(AggregateException ex, CancellationToken ct) =>
        ct.IsCancellationRequested && ex.Flatten().InnerExceptions.All(e => e is OperationCanceledException);

    // ---------- Ergebnis ----------

    internal static DecodedCode ToCode(Found found, PointMap map, int width, int height)
    {
        var r = found.Result;
        var local = MarkerPoints(r);
        var points = new List<(float X, float Y)>(local.Count);
        foreach (var (x, y) in local)
        {
            var (ox, oy) = map.Apply(x + found.OffsetX, y + found.OffsetY);
            points.Add(((float)Math.Clamp(ox, 0, width), (float)Math.Clamp(oy, 0, height)));
        }
        return new DecodedCode
        {
            Text = RepairLatin1(r),
            Format = r.BarcodeFormat.ToString(),
            Points = points,
            RawBytes = ByteSegments(r),
        };
    }

    /// <summary>
    /// Punkte für die Markierung: bei QR die vier Außenecken (aus den Suchmustern errechnet, ZXing liefert nur deren
    /// Mitten), bei PDF417 die vier Außenecken, sonst die Punkte von ZXing; ab drei Punkten im Uhrzeigersinn,
    /// beginnend oben links, damit sie ein Vieleck ergeben.
    /// </summary>
    internal static List<(double X, double Y)> MarkerPoints(Result r)
    {
        var raw = r.ResultPoints ?? [];
        if (r.BarcodeFormat == BarcodeFormat.QR_CODE && raw.Length >= 3
            && raw[0] is FinderPattern bl && raw[1] is FinderPattern tl && raw[2] is FinderPattern tr)
            return Clockwise(QrCorners(bl, tl, tr));

        IEnumerable<ResultPoint?> source = r.BarcodeFormat == BarcodeFormat.PDF_417 ? raw.Take(4) : raw;
        var list = source.Where(p => p != null).Select(p => ((double)p!.X, (double)p.Y)).ToList();
        return list.Count >= 3 ? Clockwise(list) : list;
    }

    /// <summary>Außenecken eines QR-Codes: jedes Suchmuster liegt 3,5 Module von seiner Ecke entfernt.</summary>
    private static List<(double X, double Y)> QrCorners(FinderPattern bl, FinderPattern tl, FinderPattern tr)
    {
        double m = (bl.EstimatedModuleSize + tl.EstimatedModuleSize + tr.EstimatedModuleSize) / 3 * 3.5;
        var (ux, uy) = Unit(tr.X - tl.X, tr.Y - tl.Y);
        var (vx, vy) = Unit(bl.X - tl.X, bl.Y - tl.Y);
        var tlc = (X: tl.X - m * (ux + vx), Y: tl.Y - m * (uy + vy));
        var trc = (X: tr.X + m * (ux - vx), Y: tr.Y + m * (uy - vy));
        var blc = (X: bl.X + m * (vx - ux), Y: bl.Y + m * (vy - uy));
        var brc = (X: trc.X + blc.X - tlc.X, Y: trc.Y + blc.Y - tlc.Y);
        return [tlc, trc, brc, blc];

        static (double, double) Unit(double x, double y)
        {
            double len = Math.Sqrt(x * x + y * y);
            return len < 1e-6 ? (0, 0) : (x / len, y / len);
        }
    }

    /// <summary>Sortiert Punkte im Uhrzeigersinn (Bildschirm, y nach unten) um ihren Schwerpunkt, Start oben links.</summary>
    private static List<(double X, double Y)> Clockwise(List<(double X, double Y)> points)
    {
        double cx = points.Average(p => p.X), cy = points.Average(p => p.Y);
        var sorted = points.OrderBy(p => Math.Atan2(p.Y - cy, p.X - cx)).ToList();
        int start = 0;
        for (int i = 1; i < sorted.Count; i++)
            if (sorted[i].X + sorted[i].Y < sorted[start].X + sorted[start].Y) start = i;
        return [.. sorted.Skip(start), .. sorted.Take(start)];
    }

    /// <summary>
    /// Behebt einen Fehler in ZXing.Net unter .NET (Core): Ohne Shift_JIS-Codepage setzt ZXing dafür UTF-8 ein, hält
    /// Shift_JIS aber weiter für „verfügbar und Standard“. Byte-Inhalte ohne ECI-Kennung, die zufällig wie Shift_JIS
    /// aussehen – etwa „Groß“, „Käse“ oder „Ä“ in ISO-8859-1 –, werden dann als UTF-8 gelesen und enden als „�“. Ein
    /// solches Stück wird hier als ISO-8859-1 neu gelesen, der Voreinstellung für QR-Codes ohne ECI.
    /// </summary>
    internal static string RepairLatin1(Result r)
    {
        string text = r.Text;
        if (!text.Contains(ReplacementChar) || r.ResultMetadata == null
            || !r.ResultMetadata.TryGetValue(ResultMetadataType.BYTE_SEGMENTS, out var value) || value is not IEnumerable<byte[]> segments)
            return text;
        foreach (var segment in segments)
        {
            if (segment == null) continue;
            var asUtf8 = Encoding.UTF8.GetString(segment);
            if (!asUtf8.Contains(ReplacementChar)) continue;
            int at = text.IndexOf(asUtf8, StringComparison.Ordinal);
            if (at >= 0) text = string.Concat(text.AsSpan(0, at), Encoding.Latin1.GetString(segment), text.AsSpan(at + asUtf8.Length));
        }
        return text;
    }

    /// <summary>Unicode-Ersatzzeichen (U+FFFD), das .NET für ungültige Bytefolgen einsetzt.</summary>
    private const char ReplacementChar = (char)0xFFFD;

    /// <summary>Inhalt der Byte-Segmente (QR, Data Matrix, Aztec, PDF417 im Byte-Modus) am Stück, sonst null.</summary>
    private static byte[]? ByteSegments(Result r)
    {
        if (r.ResultMetadata == null || !r.ResultMetadata.TryGetValue(ResultMetadataType.BYTE_SEGMENTS, out var value)) return null;
        if (value is not IEnumerable<byte[]> segments) return null;
        var bytes = segments.Where(s => s != null).SelectMany(s => s).ToArray();
        return bytes.Length == 0 ? null : bytes;
    }

    /// <summary>
    /// Lesereihenfolge wie auf einer Seite: Codes, die sich in der Höhe überlappen, bilden eine Zeile und stehen darin
    /// von links nach rechts; Zeilen von oben nach unten. Codes ohne Punkte kommen ans Ende.
    /// </summary>
    internal static List<DecodedCode> InReadingOrder(IEnumerable<DecodedCode> codes)
    {
        var placed = new List<(DecodedCode Code, float Top, float Bottom, float Left)>();
        var unplaced = new List<DecodedCode>();
        foreach (var c in codes)
        {
            if (c.Points.Count == 0) unplaced.Add(c);
            else placed.Add((c, c.Points.Min(p => p.Y), c.Points.Max(p => p.Y), c.Points.Min(p => p.X)));
        }

        var result = new List<DecodedCode>();
        var row = new List<(DecodedCode Code, float Top, float Bottom, float Left)>();
        float rowTop = 0, rowBottom = 0;
        foreach (var item in placed.OrderBy(p => p.Top).ThenBy(p => p.Left))
        {
            float center = (item.Top + item.Bottom) / 2, rowCenter = (rowTop + rowBottom) / 2;
            bool sameRow = row.Count > 0 && (center <= rowBottom || (rowCenter >= item.Top && rowCenter <= item.Bottom));
            if (!sameRow)
            {
                Flush();
                rowTop = item.Top;
                rowBottom = item.Bottom;
            }
            row.Add(item);
            rowBottom = Math.Max(rowBottom, item.Bottom);
        }
        Flush();
        result.AddRange(unplaced.OrderBy(c => c.Format, StringComparer.Ordinal).ThenBy(c => c.Text, StringComparer.Ordinal));
        return result;

        void Flush()
        {
            result.AddRange(row.OrderBy(r => r.Left).ThenBy(r => r.Top).Select(r => r.Code));
            row.Clear();
        }
    }
}
