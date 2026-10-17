using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Lupe.Core.Model;

namespace Lupe.UI;

/// <summary>Eine Markierung im Bild: Umriss in Bildpixeln und Etikett (Format, ggf. mit Nummer).</summary>
public sealed record CodeMarker(IReadOnlyList<Point> Outline, string Label)
{
    /// <summary>
    /// Umriss aus den Punkten, die die Erkennung liefert: 2D-Codes melden ihre vier Ecken – daraus wird das Viereck,
    /// knapp erweitert; 1D-Codes melden zwei Punkte auf der Leselinie – daraus wird ein Band. Mit <paramref name="image"/>
    /// reicht das Band so weit, wie die Striche im Bild wirklich gehen.
    /// </summary>
    public static CodeMarker? From(DecodedCode code, string label, PixelImage? image = null)
    {
        var p = code.Points.Select(t => new Point(t.X, t.Y)).Where(pt => double.IsFinite(pt.X) && double.IsFinite(pt.Y)).ToList();
        if (p.Count == 0) return null;
        if (p.Count == 1)
        {
            const double r = 14;
            return new CodeMarker([new(p[0].X - r, p[0].Y - r), new(p[0].X + r, p[0].Y - r), new(p[0].X + r, p[0].Y + r), new(p[0].X - r, p[0].Y + r)], label);
        }
        if (p.Count == 2) return new CodeMarker(image is null ? Band(p[0], p[1]) : BarBand(p[0], p[1], image) ?? Band(p[0], p[1]), label);
        // Auch QR-Codes kommen mit ihren Außenecken (früher rechnete Lupe sie aus den Suchmustern selbst aus) – ein
        // Aufschlag wie für Suchmuster-Mitten machte den Rahmen ein Achtel zu groß
        return new CodeMarker(Expand(ConvexHull(p), 1.04), label);
    }

    private static List<Point> Band(Point a, Point b)
    {
        Vector d = b - a;
        double length = Len(d);
        if (length < 1) return [new(a.X - 10, a.Y - 10), new(a.X + 10, a.Y - 10), new(a.X + 10, a.Y + 10), new(a.X - 10, a.Y + 10)];
        d /= length;
        var n = new Vector(-d.Y, d.X);
        double half = Math.Max(12, length * 0.22);
        var ext = d * Math.Max(4, length * 0.04);
        return [a - ext - n * half, b + ext - n * half, b + ext + n * half, a - ext + n * half];
    }

    /// <summary>
    /// Band um die Striche eines 1D-Codes: Von der Leselinie aus wird quer dazu Zeile für Zeile verglichen, ob das
    /// Hell-Dunkel-Muster noch dasselbe ist – so weit reichen die Striche. null, wenn das Bild dafür nichts hergibt.
    /// </summary>
    internal static List<Point>? BarBand(Point a, Point b, PixelImage image)
    {
        Vector d = b - a;
        double length = Len(d);
        if (length < 8) return null;
        d /= length;
        var n = new Vector(-d.Y, d.X);
        int samples = (int)Math.Clamp(length, 24, 400);

        // Muster auf der Leselinie (ohne die äußersten Prozent – dort beginnt die Ruhezone)
        var reference = new bool[samples];
        var luma = new double[samples];
        double min = 255, max = 0;
        for (int i = 0; i < samples; i++)
        {
            var at = a + d * (length * (0.02 + 0.96 * i / (samples - 1)));
            if (Luma(image, at) is not { } value) return null;
            luma[i] = value;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }
        if (max - min < 40) return null; // kein Kontrast – kein Strichcode zu sehen
        double threshold = (min + max) / 2;
        for (int i = 0; i < samples; i++) reference[i] = luma[i] < threshold;

        int maxReach = (int)Math.Min(length * 1.5, 2000);
        double up = Reach(-1), down = Reach(+1);
        // Etwas Luft um die Striche; mindestens so hoch wie das einfache Band, damit der Rahmen sichtbar bleibt
        double margin = Math.Max(4, length * 0.03);
        up = Math.Max(up + margin, 8);
        down = Math.Max(down + margin, 8);
        var ext = d * Math.Max(4, length * 0.04);
        return [a - ext - n * up, b + ext - n * up, b + ext + n * down, a - ext + n * down];

        int Reach(int sign)
        {
            int last = 0, misses = 0;
            for (int t = 1; t <= maxReach; t++)
            {
                var shift = n * (sign * t);
                int same = 0, seen = 0;
                for (int i = 0; i < samples; i++)
                {
                    var at = a + shift + d * (length * (0.02 + 0.96 * i / (samples - 1)));
                    if (Luma(image, at) is not { } value) return last;
                    seen++;
                    if ((value < threshold) == reference[i]) same++;
                }
                if (same >= seen * 0.85)
                {
                    last = t;
                    misses = 0;
                }
                else if (++misses > 2) break; // einzelne gestörte Zeilen (Kratzer, Text am Rand) überspringen
            }
            return last;
        }
    }

    /// <summary>Helligkeit (0–255) am Punkt, null außerhalb des Bildes.</summary>
    private static double? Luma(PixelImage image, Point at)
    {
        int x = (int)Math.Round(at.X), y = (int)Math.Round(at.Y);
        if (x < 0 || y < 0 || x >= image.Width || y >= image.Height) return null;
        int i = y * image.Stride + x * 4;
        return 0.114 * image.Bgra[i] + 0.587 * image.Bgra[i + 1] + 0.299 * image.Bgra[i + 2];
    }

    private static double Len(Vector v) => Math.Sqrt(v.X * v.X + v.Y * v.Y);

    private static List<Point> Expand(IReadOnlyList<Point> points, double factor)
    {
        double cx = points.Average(q => q.X), cy = points.Average(q => q.Y);
        return [.. points.Select(q => new Point(cx + (q.X - cx) * factor, cy + (q.Y - cy) * factor))];
    }

    /// <summary>Konvexe Hülle (Andrew), damit auch unsortierte Punkte (PDF417 liefert acht) einen sauberen Umriss geben.</summary>
    private static List<Point> ConvexHull(List<Point> points)
    {
        var sorted = points.Distinct().OrderBy(q => q.X).ThenBy(q => q.Y).ToList();
        if (sorted.Count < 3) return sorted;
        static double Cross(Point o, Point a, Point b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var hull = new List<Point>();
        foreach (var q in sorted)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], q) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(q);
        }
        int lower = hull.Count + 1;
        for (int i = sorted.Count - 2; i >= 0; i--)
        {
            while (hull.Count >= lower && Cross(hull[^2], hull[^1], sorted[i]) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(sorted[i]);
        }
        hull.RemoveAt(hull.Count - 1);
        return hull;
    }
}

/// <summary>
/// Zeigt ein Bild (Kamera, Datei) einpassend und darüber die Markierungen erkannter Codes in Akzentfarbe samt
/// Format-Etikett, auf Wunsch mit Zielhilfe in der Mitte. Selbst gezeichnet: Linien bleiben unabhängig von der
/// Bildgröße gleich dick.
/// </summary>
public sealed class CodeView : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty = AvaloniaProperty.Register<CodeView, Bitmap?>(nameof(Source));
    public static readonly StyledProperty<IReadOnlyList<CodeMarker>> MarkersProperty =
        AvaloniaProperty.Register<CodeView, IReadOnlyList<CodeMarker>>(nameof(Markers), Array.Empty<CodeMarker>());
    public static readonly StyledProperty<bool> ShowAimProperty = AvaloniaProperty.Register<CodeView, bool>(nameof(ShowAim));
    public static readonly StyledProperty<IBrush> AccentProperty = AvaloniaProperty.Register<CodeView, IBrush>(nameof(Accent), Brushes.DodgerBlue);
    public static readonly StyledProperty<IBrush> AccentTextProperty = AvaloniaProperty.Register<CodeView, IBrush>(nameof(AccentText), Brushes.White);

    private static readonly IPen AimShadow = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)), 5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly IPen AimPen = new Pen(new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), 3, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly IPen Halo = new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)), 5, lineJoin: PenLineJoin.Round);

    static CodeView()
    {
        AffectsRender<CodeView>(SourceProperty, MarkersProperty, ShowAimProperty, AccentProperty, AccentTextProperty);
    }

    public CodeView()
    {
        this.SetResource(AccentProperty, "AccentFillColorDefaultBrush");
        this.SetResource(AccentTextProperty, "TextOnAccentFillColorPrimaryBrush");
    }

    public Bitmap? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public IReadOnlyList<CodeMarker> Markers { get => GetValue(MarkersProperty); set => SetValue(MarkersProperty, value); }
    public bool ShowAim { get => GetValue(ShowAimProperty); set => SetValue(ShowAimProperty, value); }
    public IBrush Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public IBrush AccentText { get => GetValue(AccentTextProperty); set => SetValue(AccentTextProperty, value); }

    /// <summary>Neues Kamerabild in derselben Bitmap: nur neu zeichnen.</summary>
    public void Refresh() => InvalidateVisual();

    /// <summary>Wo das Bild im Element liegt (einpassend, mittig). Leer ohne Bild.</summary>
    public Rect ImageRect
    {
        get
        {
            var size = Bounds.Size;
            if (Source is not { } src || src.PixelSize.Width <= 0 || src.PixelSize.Height <= 0 || size.Width <= 0 || size.Height <= 0) return default;
            double scale = Math.Min(size.Width / src.PixelSize.Width, size.Height / src.PixelSize.Height);
            double w = src.PixelSize.Width * scale, h = src.PixelSize.Height * scale;
            return new Rect((size.Width - w) / 2, (size.Height - h) / 2, w, h);
        }
    }

    public override void Render(DrawingContext dc)
    {
        var size = Bounds.Size;
        // Durchsichtige Fläche, damit Ziehen und Ablegen überall im Element ankommen
        dc.FillRectangle(Brushes.Transparent, new Rect(size));
        var rect = ImageRect;
        if (rect.Width <= 0 || Source is not { } source) return;
        double scale = rect.Width / source.PixelSize.Width;
        using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            dc.DrawImage(source, new Rect(0, 0, source.PixelSize.Width, source.PixelSize.Height), rect);

        if (ShowAim) DrawAim(dc, rect);

        var accent = Accent;
        var accentColor = (accent as ISolidColorBrush)?.Color ?? Colors.DodgerBlue;
        var fill = new SolidColorBrush(accentColor, 0.16);
        var pen = new Pen(accent, 3, lineJoin: PenLineJoin.Round);
        var face = new Typeface(this.TryFindResource("UiFont", out var f) && f is FontFamily family ? family : FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

        foreach (var marker in Markers)
        {
            if (marker.Outline.Count < 2) continue;
            var points = marker.Outline.Select(q => new Point(rect.X + q.X * scale, rect.Y + q.Y * scale)).ToList();
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(points[0], isFilled: true);
                foreach (var p in points.Skip(1)) ctx.LineTo(p);
                ctx.EndFigure(isClosed: true);
            }
            dc.DrawGeometry(null, Halo, geometry);
            dc.DrawGeometry(fill, pen, geometry);

            if (marker.Label.Length == 0) continue;
            var text = new FormattedText(marker.Label, Labels.German, FlowDirection.LeftToRight, face, 12.5, AccentText);
            double left = points.Min(q => q.X), top = points.Min(q => q.Y), bottom = points.Max(q => q.Y);
            double boxW = text.Width + 14, boxH = text.Height + 6;
            double boxX = left, boxY = top - text.Height - 10;
            if (boxY < 2) boxY = Math.Min(bottom + 4, size.Height - boxH - 2);
            boxX = Math.Clamp(boxX, 2, Math.Max(2, size.Width - boxW - 2));
            var box = new Rect(boxX, boxY, boxW, boxH);
            dc.DrawRectangle(accent, null, box, 4, 4);
            dc.DrawText(text, new Point(box.X + 7, box.Y + 3));
        }
    }

    /// <summary>Zielhilfe: vier Winkel um die Bildmitte, hell mit dunklem Rand – auf jedem Hintergrund sichtbar.</summary>
    private static void DrawAim(DrawingContext dc, Rect rect)
    {
        double size = Math.Min(rect.Width, rect.Height) * 0.5;
        double arm = size * 0.16;
        var c = rect.Center;
        double l = c.X - size / 2, r = c.X + size / 2, t = c.Y - size / 2, b = c.Y + size / 2;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            void Corner(Point a, Point m, Point z)
            {
                ctx.BeginFigure(a, false);
                ctx.LineTo(m);
                ctx.LineTo(z);
                ctx.EndFigure(false);
            }
            Corner(new(l, t + arm), new(l, t), new(l + arm, t));
            Corner(new(r - arm, t), new(r, t), new(r, t + arm));
            Corner(new(r, b - arm), new(r, b), new(r - arm, b));
            Corner(new(l + arm, b), new(l, b), new(l, b - arm));
            // kleines Kreuz in der Mitte
            ctx.BeginFigure(new(c.X - 7, c.Y), false);
            ctx.LineTo(new(c.X + 7, c.Y));
            ctx.EndFigure(false);
            ctx.BeginFigure(new(c.X, c.Y - 7), false);
            ctx.LineTo(new(c.X, c.Y + 7));
            ctx.EndFigure(false);
        }
        dc.DrawGeometry(null, AimShadow, geometry);
        dc.DrawGeometry(null, AimPen, geometry);
    }
}
