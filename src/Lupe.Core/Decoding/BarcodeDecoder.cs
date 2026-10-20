// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using System.Diagnostics;
using Lupe.Core.Model;

namespace Lupe.Core.Decoding;

/// <summary>
/// Findet alle QR- und Barcodes in einem Bild (zxing-cpp), auch kleine, gedrehte, schräge und invertierte.
/// <para>
/// zxing-cpp findet von sich aus mehrere Codes je Bild, Codes in jeder 90°-Lage, helle QR-/Data-Matrix-/Aztec-Codes
/// auf dunklem Grund und große Codes in verkleinerten Fassungen. Lupe ergänzt nur, was die Messungen (Tests und ein
/// Benchmark mit 1 345 Bildern und 1 357 Codes) als Gewinn zeigen. Jede Bildvariante wird je Lesegruppe
/// (<see cref="CodeFormat.Group"/>) einmal gelesen; alle Aufgaben einer Stufe laufen parallel:
/// </para>
/// <list type="number">
/// <item>Das ganze Bild in voller Auflösung, alle Formate.</item>
/// <item>Das ganze Bild invertiert für die Formate, die zxing-cpp nicht selbst invertiert: Strichcodes, PDF417, MaxiCode
/// (heller Strichcode auf dunklem Grund, z. B. im Dunkelmodus).</item>
/// <item>Schräglage: Strichcodes und PDF417 vertragen in zxing-cpp nur wenige Grad (PDF417 etwa ±6°, ein langer
/// Code 128 etwa ±14°). Gedreht wird eine auf höchstens <see cref="SkewRotatedPixels"/> verkleinerte Fassung – um ±20°
/// und ±40° für Strichcodes und PDF417, zusätzlich um ±10° und ±30° nur für PDF417. QR, Data Matrix und Aztec liest
/// zxing-cpp in jeder Lage.</item>
/// <item>Sehr große Bilder (über <see cref="LargeImagePixels"/>, etwa Fotos mit 12 bis 40 Megapixeln) statt am Stück als
/// verkleinerte Übersicht plus überlappende Kacheln in voller Auflösung – so bleibt jeder einzelne Lesevorgang kurz und
/// der Abbruch schnell.</item>
/// <item>Nur wenn bis dahin nichts Sicheres gefunden wurde (<see cref="Hit.Solid"/>): kleine Bilder (bis
/// <see cref="UpscaleMaxSide"/> Pixel Kantenlänge, typisch ein Bildschirmausschnitt) doppelt vergrößert – für Codes mit
/// 1–2 Pixeln je Modul.</item>
/// </list>
/// <para>
/// Danach: Doppelte (gleiches Format, gleicher Text) zusammengeführt, widersprüchliche Treffer an derselben Stelle
/// entschieden (<see cref="Add"/>), Bruchstücke von Strichcodes entfernt (<see cref="RemoveFragments"/>),
/// Lesereihenfolge (oben vor unten, links vor rechts). Für den Kamerastrom gibt es <see cref="LiveBarcodeDecoder"/>
/// (nur das ganze Bild).
/// </para>
/// <para>Threadsicher; eine Instanz kann von mehreren Threads gleichzeitig benutzt werden.</para>
/// </summary>
public sealed class BarcodeDecoder
{
    /// <summary>Ab dieser Pixelzahl wird in Übersicht und Kacheln zerlegt (4K mit 8,3 MP: nein; 12-MP-Foto: ja).</summary>
    internal const int LargeImagePixels = 9_000_000;
    /// <summary>Zielgröße der verkleinerten Übersicht großer Bilder.</summary>
    internal const int OverviewPixels = 4_000_000;
    internal const int TileSize = 2048;
    /// <summary>Überlappung der Kacheln – Codes bis zu dieser Größe liegen sicher ganz in einer Kachel.</summary>
    internal const int TileOverlap = 512;
    /// <summary>Höchstzahl der Kacheln (40 MP: 24).</summary>
    internal const int MaxTiles = 32;
    /// <summary>Bis zu dieser Kantenlänge wird in Stufe 2 vergrößert.</summary>
    internal const int UpscaleMaxSide = 1000;
    /// <summary>Winkel der Schräglagen-Varianten für Strichcodes und PDF417.</summary>
    internal static readonly double[] SkewAngles = [-40, -20, 20, 40];
    /// <summary>Zusätzliche Winkel nur für PDF417 (verträgt in zxing-cpp nur etwa ±6°).</summary>
    internal static readonly double[] Pdf417SkewAngles = [-30, -10, 10, 30];
    /// <summary>Größte Fläche eines gedrehten Bildes (gemessen bei 45°); größere werden vorher verkleinert.</summary>
    internal const int SkewRotatedPixels = 2_400_000;

    private readonly CodeFormat[] _formats;

    /// <param name="formats">
    /// Erlaubte Formate in ZXing-Schreibweise; null oder leer = alle. Groß/Klein ist egal, unbekannte Namen werden
    /// übergangen (bleibt nichts übrig, gelten alle).
    /// </param>
    public BarcodeDecoder(IReadOnlyList<string>? formats = null) => _formats = FormatCatalog.Parse(formats);

    /// <summary>
    /// Nur das ganze Bild (2D-Codes und Strichcodes, invertiert nur PDF417 und MaxiCode) – für den Kamerastrom
    /// (<see cref="LiveBarcodeDecoder"/>), wo das nächste Bild gleich wieder eine Chance ist und Dauerlast auf allen Kernen
    /// nicht sein soll.
    /// </summary>
    internal bool Live { get; init; }

    /// <summary>Höchstzahl gleichzeitig laufender Aufgaben (-1 = so viele, wie der Thread-Pool hergibt).</summary>
    internal int MaxParallelism { get; init; } = -1;

    /// <summary>
    /// Null, wenn die Erkennung auf diesem System verfügbar ist; sonst die Meldung mit Fehlercode, warum die native
    /// zxing-cpp-Bibliothek nicht lädt (für einen Hinweis gleich beim Start). Prüft beim ersten Aufruf.
    /// </summary>
    public static string? UnavailableReason => ZXingLibrary.Problem;

    /// <summary>Alle unterstützten Formate in ZXing-Schreibweise.</summary>
    public static IReadOnlyList<string> AllFormats => FormatCatalog.AllNames;

    /// <summary>Die tatsächlich aktiven Formate.</summary>
    internal IReadOnlyList<string> ActiveFormats => [.. _formats.Select(f => f.Name)];

    /// <summary>
    /// Alle Codes im Bild, ohne Doppelte (gleicher Text und gleiches Format), in Lesereihenfolge (oben vor unten,
    /// links vor rechts); leer, wenn keiner gefunden wurde. Punkte in Pixeln des übergebenen Bildes.
    /// Wirft nur bei <c>null</c>, beim Abbruch über <paramref name="ct"/> (<see cref="OperationCanceledException"/>) und wenn
    /// die Erkennung auf diesem System nicht verfügbar ist (<see cref="DecoderUnavailableException"/>: die native
    /// zxing-cpp-Bibliothek lädt nicht); andere Fehler eines einzelnen Lesevorgangs kosten nur dessen Teilergebnis.
    /// Rechnet auf mehreren Kernen und blockiert den aufrufenden Thread bis zum Ergebnis (Full HD typisch 30–100 ms) –
    /// aus der Oberfläche also über <c>Task.Run</c> aufrufen.
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

    /// <summary>
    /// Prüft vor dem ersten Lesevorgang, ob die native Bibliothek lädt (null = ja, sonst die Meldung). Tests setzen hier
    /// einen Ladefehler ein; sonst <see cref="ZXingLibrary.Problem"/>.
    /// </summary>
    internal Func<string?> NativeProblem { get; init; } = static () => ZXingLibrary.Problem;

    internal IReadOnlyList<DecodedCode> Decode(GrayImage gray, CancellationToken ct)
    {
        // Vor jedem Objekt von ZXingCpp: Ein Ladefehler ist kein „nichts gefunden“ (und ein halb angelegtes Objekt
        // brächte den Prozess später im Finalizer zum Absturz, siehe ZXingLibrary)
        if (NativeProblem() is { } problem) throw new DecoderUnavailableException(problem);
        var hits = new List<Hit>();
        int order = 0;
        foreach (var stage in Stages(gray, _formats, Live))
        {
            if (stage.OnlyIfNothingFound && hits.Exists(h => h.Solid)) break;
            foreach (var (found, map) in RunStage(stage.Tasks, MaxParallelism, ct))
            {
                foreach (var f in found) Add(hits, ToHit(f, map, gray.Width, gray.Height, _formats, order));
                order++;
            }
        }
        RemoveFragments(hits);
        return [.. InReadingOrder(hits).Select(h => h.Code)];
    }

    // ---------- Stufen ----------

    /// <summary>
    /// Eine Bildvariante (Original, invertiert, Kachel, skaliert, gedreht). Das Bild entsteht erst bei Bedarf im
    /// Arbeitsthread und wird von allen Aufgaben der Variante geteilt. <see cref="ToOriginal"/> rechnet Punkte ins Original zurück.
    /// </summary>
    internal sealed class ScanVariant(string name, Func<GrayImage> build, PointMap toOriginal)
    {
        private readonly Lazy<GrayImage> _image = new(build, LazyThreadSafetyMode.ExecutionAndPublication);

        public string Name { get; } = name;
        public PointMap ToOriginal { get; } = toOriginal;
        public GrayImage Image => _image.Value;
    }

    /// <summary>Eine Aufgabe: ein Lesevorgang von zxing-cpp auf einer Variante.</summary>
    internal sealed record ScanTask(ScanVariant Variant, ReadSettings Settings);

    internal sealed record Stage(IReadOnlyList<ScanTask> Tasks, bool OnlyIfNothingFound);

    internal static IReadOnlyList<Stage> Stages(GrayImage gray, IReadOnlyList<CodeFormat> formats, bool live = false)
    {
        int w = gray.Width, h = gray.Height;
        long pixels = (long)w * h;
        var stages = new List<Stage>();
        var first = new List<ScanTask>();
        // Live nur PDF417 und MaxiCode invertiert (wie bisher); helle Strichcodes auf dunklem Grund sind vor der Kamera selten,
        // und ein Lesevorgang mehr kostet je Bild spürbar Zeit
        var invertible = formats.Where(f => f.NeedsInvertedPass && (!live || !f.Linear)).ToList();

        // Je Lesegruppe (siehe CodeFormat.Group) ein Lesevorgang. Live nur 2D-Codes und Strichcodes getrennt: Bei
        // Kamerabildern ist jeder Lesevorgang kurz, und auf zwei Kernen kostete jede weitere Binarisierung mehr, als die
        // Aufteilung bringt (unruhiges 720p-Bild: 32 statt 40 ms)
        void Add(List<ScanTask> tasks, ScanVariant v, IReadOnlyList<CodeFormat> fs, bool tryInvert = true, bool tryDownscale = true)
        {
            foreach (var group in fs.GroupBy(f => live ? (f.Linear ? 1 : 0) : f.Group).OrderBy(g => g.Key))
                tasks.Add(new ScanTask(v, new ReadSettings([.. group], tryInvert, tryDownscale)));
        }

        // Stufe 1: ganzes Bild bzw. Übersicht und Kacheln, dazu invertiert für die Formate, die zxing-cpp nicht selbst umkehrt.
        // Die Reihenfolge ist zugleich der Vorrang bei widersprüchlichen Treffern (siehe Add): zuerst das ganze Bild.
        ScanVariant? primary = null;
        var tiles = new List<ScanVariant>();
        if (pixels <= LargeImagePixels)
        {
            if (Sane(w, h)) primary = new ScanVariant("Original", () => gray, PointMap.Identity);
        }
        else
        {
            int factor = FactorFor(pixels, OverviewPixels, w, h);
            if (Sane(w / factor, h / factor))
                primary = new ScanVariant($"Übersicht 1:{factor}", () => gray.Downscale(factor), PointMap.ScaleOffset(factor, 0, 0));
            // Riesige Fotos (bis 40 MP) werden verkleinert gekachelt, wenn es sonst zu viele Kacheln würden; extrem lange
            // Streifen, die sich auch verkleinert nicht in wenige Kacheln teilen lassen, bekommen keine
            int tf = 1;
            while (tf < Math.Min(w, h) && Tiles(w / tf, h / tf, TileSize, TileOverlap).Count() > MaxTiles) tf++;
            var cells = Tiles(w / tf, h / tf, TileSize, TileOverlap).ToList();
            var tileSource = new Lazy<GrayImage>(() => gray.Downscale(tf), LazyThreadSafetyMode.ExecutionAndPublication);
            if (cells.Count <= MaxTiles && Sane(w / tf, h / tf))
                foreach (var (x, y, tw, th) in cells)
                    tiles.Add(new ScanVariant($"Kachel {x},{y}", () => tileSource.Value.Crop(x, y, tw, th), PointMap.ScaleOffset(tf, x * tf, y * tf)));
        }
        if (primary != null)
        {
            // Je Lesegruppe ein Lesevorgang: Sie laufen parallel und liefern dasselbe wie einer am Stück – zxing-cpp lässt
            // seine Leser ohnehin nacheinander über dasselbe Bild laufen (unruhiges 720p-Bild: 2D 27 ms und Strichcodes
            // 22 ms parallel statt 49 ms am Stück)
            Add(first, primary, formats);
            var source = primary;
            Add(first, new ScanVariant(primary.Name + " invertiert", () => source.Image.Invert(), primary.ToOriginal), invertible, tryInvert: false);
        }
        // In den Kacheln die kleinen Codes – die großen findet die Übersicht, deshalb ohne zxing-cpps Verkleinerung
        foreach (var tile in tiles)
            Add(first, tile, formats, tryDownscale: false);
        if (!live) AddSkew(first, gray, formats);
        stages.Add(new Stage(first, OnlyIfNothingFound: false));
        if (live) return stages;

        // Stufe 2 – nur, wenn bis dahin nichts gefunden wurde: kleine Bilder vergrößert (1–2 Pixel je Modul)
        var second = new List<ScanTask>();
        if (Math.Max(w, h) <= UpscaleMaxSide && Sane(w, h))
            Add(second, new ScanVariant("Vergrößert ×2", () => gray.Upscale(2), PointMap.ScaleOffset(0.5, 0, 0)), formats);
        if (second.Count > 0) stages.Add(new Stage(second, OnlyIfNothingFound: true));
        return stages;
    }

    /// <summary>
    /// Schräg liegende Strichcodes und PDF417. Schräg liegen Codes vor allem auf Fotos, und dort sind sie groß – gedreht
    /// wird eine verkleinerte Fassung, deren gedrehte Fläche höchstens etwa <see cref="SkewRotatedPixels"/> hat. Gedreht
    /// um 45° ist die Fläche (w + h)² / 2 – bei langen Streifen viel mehr als w·h; Pixelfäden, die sich nicht weit genug
    /// verkleinern lassen, werden nicht gedreht.
    /// </summary>
    private static void AddSkew(List<ScanTask> tasks, GrayImage gray, IReadOnlyList<CodeFormat> formats)
    {
        var linear = formats.Where(f => f.Linear || f.Name is "PDF_417").ToList();
        var pdf = formats.Where(f => f.Name is "PDF_417").ToList();
        if (linear.Count == 0) return;
        int w = gray.Width, h = gray.Height;
        int f = FactorFor(Math.Pow(w + h, 2) / 2, SkewRotatedPixels, w, h);
        int sw = w / f, sh = h / f;
        if (!Sane(sw, sh) || Math.Pow(sw + sh, 2) / 2 > 2.0 * SkewRotatedPixels || Math.Min(sw, sh) < 8) return;
        var source = new Lazy<GrayImage>(() => gray.Downscale(f), LazyThreadSafetyMode.ExecutionAndPublication);
        foreach (var (angles, fs) in new[] { (SkewAngles, linear), (Pdf417SkewAngles, pdf) })
        {
            if (fs.Count == 0) continue;
            foreach (var angle in angles)
            {
                var map = PointMap.ScaleOffset(f, 0, 0).After(GrayImage.RotationMap(sw, sh, angle));
                var rotated = new ScanVariant($"Gedreht {angle:+0;-0}°", () => source.Value.Rotate(angle, out _), map);
                // Ohne zxing-cpps Verkleinerung: die Fassung ist schon klein, und große Codes liest das Original
                tasks.Add(new ScanTask(rotated, new ReadSettings(fs, TryInvert: false, TryDownscale: false)));
            }
        }
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

    private static (List<NativeCode> Found, PointMap Map)[] RunStage(IReadOnlyList<ScanTask> tasks, int maxParallelism, CancellationToken ct)
    {
        var results = new (List<NativeCode>, PointMap)[tasks.Count];
        try
        {
            // Einzeln verteilen (keine Blöcke), damit ungleich lange Aufgaben die Kerne gleichmäßig auslasten
            var partitioner = Partitioner.Create(Enumerable.Range(0, tasks.Count), EnumerablePartitionerOptions.NoBuffering);
            var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = maxParallelism };
            Parallel.ForEach(partitioner, options, i => results[i] = RunTask(tasks[i], ct));
        }
        catch (AggregateException ex) when (IsCancellation(ex, ct))
        {
            throw new OperationCanceledException(ct);
        }
        catch (AggregateException ex) when (ex.Flatten().InnerExceptions.OfType<DecoderUnavailableException>().FirstOrDefault() is { } unavailable)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(unavailable).Throw();
        }
        ct.ThrowIfCancellationRequested();
        return results;
    }

    private static (List<NativeCode>, PointMap) RunTask(ScanTask task, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            return (NativeReader.Read(task.Variant.Image, task.Settings), task.Variant.ToOriginal);
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            // Die Bibliothek lädt nicht oder es fehlen Funktionen – das betrifft jeden Lesevorgang, nicht nur diesen
            throw new DecoderUnavailableException(ZXingLibrary.Message((ex.InnerException ?? ex).Message));
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException or DecoderUnavailableException))
        {
            // Unerwartete Fehler (zxing-cpp meldet etwa zu große Bilder als Ausnahme) kosten nur diese eine Aufgabe
            Debug.WriteLine($"Erkennung „{task.Variant.Name}“ fehlgeschlagen: {ex}");
            return ([], PointMap.Identity);
        }
    }

    /// <summary>Ausnahmen, mit denen .NET eine nicht ladbare native Bibliothek meldet (auch verpackt in einen Typinitialisierer).</summary>
    internal static bool IsLoadFailure(Exception ex) =>
        ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
        || ex is TypeInitializationException { InnerException: DllNotFoundException or EntryPointNotFoundException or BadImageFormatException };

    private static bool IsCancellation(AggregateException ex, CancellationToken ct) =>
        ct.IsCancellationRequested && ex.Flatten().InnerExceptions.All(e => e is OperationCanceledException);

    // ---------- Ergebnis ----------

    /// <summary>
    /// Ein Treffer samt Umriss im Original (vier Ecken; bei Strichcodes Anfang und Ende der ersten und letzten Lesezeile),
    /// ungenutzter Fehlerkorrektur (<see cref="NativeCode.Uec"/>) und Rang der Aufgabe, die ihn fand (0 = ganzes Bild).
    /// </summary>
    internal sealed record Hit(DecodedCode Code, IReadOnlyList<(float X, float Y)> Outline, double? Uec, int Order)
    {
        public bool Linear => IsLinear(Code.Format);

        /// <summary>Strichcodes: wie breit der Streifen der Lesezeilen ist (Pixel im Original) – je breiter, desto sicherer.</summary>
        public double Thickness => Outline.Count == 4 ? LineAxis(Outline).Thickness : 0;

        /// <summary>
        /// Ein Treffer, der die gründlichere zweite Stufe überflüssig macht: jeder 2D-Code, Strichcodes nur, wenn sie auf
        /// einem Streifen von mindestens 3 % ihrer Länge gelesen wurden. Ein Strichcode aus nur zwei, drei Zeilen eines
        /// unscharfen Fotos ist oft falsch gelesen (gemessen: EAN-13 statt des UPC-A, den die Vergrößerung richtig liest).
        /// </summary>
        public bool Solid => !Linear || Outline.Count != 4 || Thickness >= 0.03 * LineAxis(Outline).Length;
    }

    internal static Hit ToHit(NativeCode found, PointMap map, int width, int height, IReadOnlyList<CodeFormat> active, int order)
    {
        var outline = new List<(float X, float Y)>(found.Corners.Length);
        foreach (var (x, y) in found.Corners)
        {
            var (ox, oy) = map.Apply(x, y);
            outline.Add(((float)Math.Clamp(ox, 0, width), (float)Math.Clamp(oy, 0, height)));
        }
        return new Hit(ToCode(found, map, width, height, active), outline, found.Uec, order);
    }

    internal static DecodedCode ToCode(NativeCode found, PointMap map, int width, int height, IReadOnlyList<CodeFormat> active)
    {
        var (format, text) = Presentation(found, active);
        var local = MarkerPoints(found.Corners, found.Format.Linear);
        var points = new List<(float X, float Y)>(local.Count);
        foreach (var (x, y) in local)
        {
            var (ox, oy) = map.Apply(x, y);
            points.Add(((float)Math.Clamp(ox, 0, width), (float)Math.Clamp(oy, 0, height)));
        }
        return new DecodedCode
        {
            Text = text,
            Format = format,
            Points = OrderPoints(points, found.Format.Linear),
            RawBytes = found.Bytes.Length == 0 ? null : found.Bytes,
        };
    }

    /// <summary>
    /// Nimmt einen Treffer auf – außer er ist doppelt (gleiches Format, gleicher Text) oder widerspricht einem Treffer
    /// einer anderen Aufgabe an derselben Stelle. Das passiert, wenn eine gedrehte oder verkleinerte Fassung denselben
    /// Code anders liest: PDF417 schräg angeschnitten mit vertauschten Zeilen, die die Fehlerkorrektur gerade noch
    /// durchgehen lässt (gemessen: ein zweites, verwürfeltes PDF417 neben dem richtigen), oder ein Strichcode, aus dessen
    /// Teil eine andere Fassung einen EAN-8 oder UPC-E macht. Dann gilt der sicherere Treffer: bei 2D-Codes der mit mehr
    /// ungenutzter Fehlerkorrektur, bei Strichcodes der auf mehr Zeilen gelesene, sonst der aus der vorrangigen Aufgabe
    /// (zuerst das ganze Bild). Treffer derselben Aufgabe widersprechen sich nicht – das hat zxing-cpp schon entschieden
    /// (z. B. ein QR-Code in einem QR-Code).
    /// </summary>
    internal static void Add(List<Hit> hits, Hit hit)
    {
        for (int i = 0; i < hits.Count; i++)
        {
            var other = hits[i];
            if (other.Code.Format == hit.Code.Format && other.Code.Text == hit.Code.Text) return;
            if (other.Order == hit.Order || !Competing(other, hit) || !Overlap(other, hit)) continue;
            if (Better(hit, other)) hits[i] = hit;
            return;
        }
        hits.Add(hit);

        static bool Competing(Hit a, Hit b) => a.Linear && b.Linear || a.Code.Format == b.Code.Format;

        // 2D: mehr ungenutzte Fehlerkorrektur; Strichcodes: auf mehr Zeilen gelesen; sonst die vorrangige Aufgabe
        static bool Better(Hit candidate, Hit current)
        {
            if (candidate.Uec is { } u && current.Uec is { } v && u != v) return u > v;
            if (candidate.Linear && current.Linear && Math.Abs(candidate.Thickness - current.Thickness) > 1)
                return candidate.Thickness > current.Thickness;
            return candidate.Order < current.Order;
        }
    }

    /// <summary>Liegt die Mitte des einen Umrisses im (etwas erweiterten) anderen?</summary>
    internal static bool Overlap(Hit a, Hit b) => Contains(a.Outline, Center(b.Outline)) || Contains(b.Outline, Center(a.Outline));

    private static (float X, float Y) Center(IReadOnlyList<(float X, float Y)> outline) =>
        outline.Count == 0 ? (float.NaN, float.NaN) : (outline.Average(p => p.X), outline.Average(p => p.Y));

    /// <summary>
    /// Punkt im konvexen Viereck, um ein Viertel um die Mitte erweitert (Strichcodes: die Lesezeilen reichen nicht ganz
    /// bis an die Enden der Striche). Dazu ein paar Pixel Spielraum, damit auch ein flacher Umriss (zwei Lesezeilen)
    /// etwas trifft.
    /// </summary>
    private static bool Contains(IReadOnlyList<(float X, float Y)> outline, (float X, float Y) p)
    {
        if (outline.Count < 3 || float.IsNaN(p.X)) return false;
        var c = Center(outline);
        var grown = outline.Select(q => (X: c.X + (q.X - c.X) * 1.25f, Y: c.Y + (q.Y - c.Y) * 1.25f)).ToList();
        int sign = 0;
        for (int i = 0; i < grown.Count; i++)
        {
            var (a, b) = (grown[i], grown[(i + 1) % grown.Count]);
            double cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            double length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (Math.Abs(cross) <= 4 * length) continue; // höchstens 4 Pixel neben der Kante: zählt als drin
            int side = Math.Sign(cross);
            if (sign == 0) sign = side;
            else if (side != sign) return false;
        }
        return true;
    }

    /// <summary>
    /// Format und Text so, wie Lupe sie schon mit ZXing.Net ausgab (Verlauf, Link-Regeln, Eintippen und Skripte
    /// verlassen sich darauf):
    /// <list type="bullet">
    /// <item>UPC-A: 12 Ziffern, als UPC_A, wenn UPC_A gesucht wird – zxing-cpp meldet ihn als EAN-13 mit führender 0
    /// (dasselbe Strichmuster). Wird nur EAN_13 gesucht, bleibt es EAN-13 mit 13 Ziffern.</item>
    /// <item>UPC-E: die achtstellige Form statt der 13-stelligen GTIN.</item>
    /// <item>Codabar: ohne Start- und Stoppzeichen (A–D).</item>
    /// <item>GS1 DataBar und DataBar Limited: die 14-stellige GTIN ohne den Datenbezeichner „01“.</item>
    /// <item>GS1 DataBar Expanded: in Klammerschreibweise „(01)…(3202)…“.</item>
    /// </list>
    /// Alles andere bleibt, wie zxing-cpp es liefert: GS1-Trennzeichen (0x1D) im Text, Zeichensatz nach ECI bzw. erkannt.
    /// </summary>
    internal static (string Format, string Text) Presentation(NativeCode found, IReadOnlyList<CodeFormat> active)
    {
        string text = found.Text;
        switch (found.Format.Name)
        {
            case "EAN_13" when text.Length == 13 && text[0] == '0' && active.Any(f => f.Name == "UPC_A"):
            case "UPC_A" when text.Length == 13 && text[0] == '0':
                return ("UPC_A", text[1..]);
            case "UPC_E":
                return ("UPC_E", found.Upce ?? text);
            case "CODABAR" when text.Length >= 2 && IsCodabarGuard(text[0]) && IsCodabarGuard(text[^1]):
                return ("CODABAR", text[1..^1]);
            case "RSS_14" or "RSS_LIMITED" when text.Length == 16 && text.StartsWith("01", StringComparison.Ordinal):
                return (found.Format.Name, text[2..]);
            case "RSS_EXPANDED":
                return ("RSS_EXPANDED", Gs1Text.Bracketed(text) ?? text);
            default:
                return (found.Format.Name, text);
        }

        static bool IsCodabarGuard(char c) => c is >= 'A' and <= 'D' or >= 'a' and <= 'd';
    }

    /// <summary>
    /// Punkte für die Markierung: 2D-Codes die vier Ecken; Strichcodes zwei Punkte – die Enden der Leselinie in der Mitte
    /// der gelesenen Zeilen (zxing-cpp meldet Anfang und Ende der ersten und der letzten Lesezeile).
    /// </summary>
    internal static List<(double X, double Y)> MarkerPoints((double X, double Y)[] corners, bool linear)
    {
        if (!linear || corners.Length != 4) return [.. corners];
        var (tl, tr, br, bl) = (corners[0], corners[1], corners[2], corners[3]);
        return [((tl.X + bl.X) / 2, (tl.Y + bl.Y) / 2), ((tr.X + br.X) / 2, (tr.Y + br.Y) / 2)];
    }

    /// <summary>Strichcodes: der linke (bei gleicher Lage der obere) Punkt zuerst; 2D-Codes im Uhrzeigersinn ab oben links.</summary>
    private static List<(float X, float Y)> OrderPoints(List<(float X, float Y)> points, bool linear)
    {
        if (linear && points.Count == 2)
        {
            var (a, b) = (points[0], points[1]);
            return a.X < b.X || (a.X == b.X && a.Y <= b.Y) ? [a, b] : [b, a];
        }
        return points.Count >= 3 ? Clockwise(points) : points;
    }

    /// <summary>Sortiert Punkte im Uhrzeigersinn (Bildschirm, y nach unten) um ihren Schwerpunkt, Start oben links.</summary>
    internal static List<(float X, float Y)> Clockwise(List<(float X, float Y)> points)
    {
        double cx = points.Average(p => p.X), cy = points.Average(p => p.Y);
        var sorted = points.OrderBy(p => Math.Atan2(p.Y - cy, p.X - cx)).ToList();
        int start = 0;
        for (int i = 1; i < sorted.Count; i++)
            if (sorted[i].X + sorted[i].Y < sorted[start].X + sorted[start].Y) start = i;
        return [.. sorted.Skip(start), .. sorted.Take(start)];
    }

    /// <summary>
    /// Strichcode-Formate, deren Leser aus einem schräg oder teilweise gelesenen anderen Strichcode gelegentlich ein
    /// gültiges Bruchstück machen: ITF hat keine Prüfziffer, EAN-8 und UPC-E nur eine einzige (jedes zehnte Zufallsmuster passt).
    /// </summary>
    internal static bool IsWeak(string format) => format is "ITF" or "EAN_8" or "UPC_E";

    /// <summary>
    /// Entfernt Bruchstücke: ein schwacher Strichcode, der zu einem anderen Strichcode gehört, der kein schwacher ist oder
    /// einen längeren Text hat – weil er mitten in dessen Umriss liegt (z. B. ein Phantom-EAN-8 „60002914“ aus einem schräg
    /// gelesenen UPC-A „036000291452“ oder ein UPC-E aus einem um 40° gedreht gelesenen EAN-13) oder weil sein Text ein
    /// Teil von dessen Text ist und er gleich daneben liegt (ein kurzes ITF aus den unteren Zeilen eines schrägen langen,
    /// die nicht mehr ganz durch den Code reichen). Gilt auch innerhalb derselben Aufgabe.
    /// </summary>
    internal static void RemoveFragments(List<Hit> hits)
    {
        // Erst alle Bruchstücke bestimmen, dann entfernen – während RemoveAll ist die Liste halb umgeräumt
        var fragments = new HashSet<Hit>(ReferenceEqualityComparer.Instance);
        foreach (var c in hits)
            if (IsWeak(c.Code.Format) && hits.Exists(o =>
                    !ReferenceEquals(o, c) && o.Linear && (!IsWeak(o.Code.Format) || o.Code.Text.Length > c.Code.Text.Length)
                    && (Contains(o.Outline, Center(c.Outline))
                        || o.Code.Text.Length > c.Code.Text.Length && o.Code.Text.Contains(c.Code.Text, StringComparison.Ordinal) && Near(c, o))))
                fragments.Add(c);
        foreach (var c in hits)
            if (c.Linear && hits.Exists(o => !ReferenceEquals(o, c) && ThinTwin(c, o)))
                fragments.Add(c);
        if (fragments.Count > 0) hits.RemoveAll(fragments.Contains);

        static bool Near(Hit fragment, Hit whole)
        {
            if (fragment.Outline.Count == 0 || whole.Outline.Count == 0) return true;
            var (mx, my) = Center(fragment.Outline);
            float minX = whole.Outline.Min(p => p.X), maxX = whole.Outline.Max(p => p.X);
            float minY = whole.Outline.Min(p => p.Y), maxY = whole.Outline.Max(p => p.Y);
            // Die Lesezeilen decken die Striche nicht immer ganz ab – der Code reicht etwa eine halbe Länge darüber hinaus
            float reach = Math.Max(maxX - minX, maxY - minY) / 2;
            return mx >= minX - reach && mx <= maxX + reach && my >= minY - reach && my <= maxY + reach;
        }
    }

    /// <summary>
    /// Ein Strichcode, der nur auf wenigen Zeilen am Rand eines gleichartigen, viel öfter gelesenen Strichcodes mit gleich
    /// langem, aber anderem Text liegt – dieselben Striche, schräg oder unscharf angeschnitten und falsch gelesen (gemessen:
    /// ein zweiter EAN-13 auf drei Spalten neben dem echten, ein UPC-E aus zwei Zeilen über dem echten). Gleiche Richtung,
    /// fast gleiche Ausdehnung längs des Codes, höchstens ein Viertel so viele Lesezeilen und nicht weiter weg als ein
    /// Viertel der Codelänge. Zwei echte Codes übereinander (etwa auf einem Etikettenbogen) sind beide ähnlich hoch gelesen.
    /// </summary>
    internal static bool ThinTwin(Hit thin, Hit thick)
    {
        if (!thin.Linear || Family(thin.Code) != Family(thick.Code) || thin.Code.Text == thick.Code.Text
            || thin.Outline.Count != 4 || thick.Outline.Count != 4)
            return false;
        var a = LineAxis(thick.Outline);
        var b = LineAxis(thin.Outline);
        if (a.Length < 1 || b.Thickness * 4 > a.Thickness) return false;
        // Gleiche Richtung (±10°), gleiches Stück längs des Codes (Anfang und Ende je auf ein Zehntel)
        if (Math.Abs(a.Dx * b.Dx + a.Dy * b.Dy) < Math.Cos(Math.PI / 18)) return false;
        double start = (b.Start.X - a.Start.X) * a.Dx + (b.Start.Y - a.Start.Y) * a.Dy;
        double end = (b.End.X - a.Start.X) * a.Dx + (b.End.Y - a.Start.Y) * a.Dy;
        (start, end) = (Math.Min(start, end), Math.Max(start, end));
        if (Math.Abs(start) > a.Length / 10 || Math.Abs(end - a.Length) > a.Length / 10) return false;
        // Quer zum Code: Abstand der Mitten höchstens halbe Dicke plus ein Viertel der Länge
        var (ca, cb) = (Center(thick.Outline), Center(thin.Outline));
        double across = Math.Abs((cb.X - ca.X) * -a.Dy + (cb.Y - ca.Y) * a.Dx);
        return across <= a.Thickness / 2 + a.Length / 4;

        // UPC-A ist ein EAN-13 mit führender 0 – gleiche Striche, also eine Familie; sonst Format und Textlänge
        static string Family(DecodedCode c) => c.Format is "EAN_13" or "UPC_A" ? "EAN_13/UPC_A" : $"{c.Format}/{c.Text.Length}";
    }

    /// <summary>
    /// Achse eines Strichcode-Umrisses (Ecken in der Lage des Codes: oben links, oben rechts, unten rechts, unten links):
    /// Richtung längs des Codes, Länge, Dicke (Breite des Streifens der Lesezeilen), Anfang und Ende der Mittellinie.
    /// </summary>
    internal static (double Dx, double Dy, double Length, double Thickness, (float X, float Y) Start, (float X, float Y) End) LineAxis(
        IReadOnlyList<(float X, float Y)> o)
    {
        var start = ((o[0].X + o[3].X) / 2, (o[0].Y + o[3].Y) / 2);
        var end = ((o[1].X + o[2].X) / 2, (o[1].Y + o[2].Y) / 2);
        double dx = end.Item1 - start.Item1, dy = end.Item2 - start.Item2, length = Math.Sqrt(dx * dx + dy * dy);
        double tx = (o[3].X + o[2].X - o[0].X - o[1].X) / 2.0, ty = (o[3].Y + o[2].Y - o[0].Y - o[1].Y) / 2.0;
        return length < 1e-6 ? (0, 0, 0, Math.Sqrt(tx * tx + ty * ty), start, end) : (dx / length, dy / length, length, Math.Sqrt(tx * tx + ty * ty), start, end);
    }

    internal static bool IsLinear(string format) => FormatCatalog.Find(format) is { Linear: true };

    /// <summary>
    /// Lesereihenfolge wie auf einer Seite: Codes, die sich in der Höhe überlappen, bilden eine Zeile und stehen darin
    /// von links nach rechts; Zeilen von oben nach unten. Maßgeblich ist der Umriss – bei Strichcodes reicht er so weit,
    /// wie zxing-cpp Zeilen gelesen hat, nicht nur die eine Leselinie der Markierung. Codes ohne Umriss kommen ans Ende.
    /// </summary>
    internal static List<Hit> InReadingOrder(IEnumerable<Hit> hits)
    {
        var placed = new List<(Hit Hit, float Top, float Bottom, float Left)>();
        var unplaced = new List<Hit>();
        foreach (var h in hits)
        {
            var outline = h.Outline.Count > 0 ? h.Outline : h.Code.Points;
            if (outline.Count == 0) unplaced.Add(h);
            else placed.Add((h, outline.Min(p => p.Y), outline.Max(p => p.Y), outline.Min(p => p.X)));
        }

        var result = new List<Hit>();
        var row = new List<(Hit Hit, float Top, float Bottom, float Left)>();
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
        result.AddRange(unplaced.OrderBy(h => h.Code.Format, StringComparer.Ordinal).ThenBy(h => h.Code.Text, StringComparer.Ordinal));
        return result;

        void Flush()
        {
            result.AddRange(row.OrderBy(r => r.Left).ThenBy(r => r.Top).Select(r => r.Hit));
            row.Clear();
        }
    }
}
