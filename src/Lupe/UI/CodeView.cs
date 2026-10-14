using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lupe.Core.Model;

namespace Lupe.UI;

/// <summary>Eine Markierung im Bild: Umriss in Bildpixeln und Etikett (Format, ggf. mit Nummer).</summary>
public sealed record CodeMarker(IReadOnlyList<Point> Outline, string Label)
{
    /// <summary>
    /// Umriss aus den Punkten, die die Erkennung liefert: QR-Codes melden die Mitten der drei Suchmuster (plus
    /// manchmal das Ausrichtungsmuster) – daraus wird das Viereck ergänzt und auf die Codegröße erweitert;
    /// 1D-Codes melden zwei Punkte auf der Leselinie – daraus wird ein Band.
    /// </summary>
    public static CodeMarker? From(DecodedCode code, string label)
    {
        var p = code.Points.Select(t => new Point(t.X, t.Y)).Where(pt => double.IsFinite(pt.X) && double.IsFinite(pt.Y)).ToList();
        if (p.Count == 0) return null;
        if (p.Count == 1)
        {
            const double r = 14;
            return new CodeMarker([new(p[0].X - r, p[0].Y - r), new(p[0].X + r, p[0].Y - r), new(p[0].X + r, p[0].Y + r), new(p[0].X - r, p[0].Y + r)], label);
        }
        if (p.Count == 2) return new CodeMarker(Band(p[0], p[1]), label);

        if (code.Format.Equals("QR_CODE", StringComparison.OrdinalIgnoreCase))
        {
            // unten links, oben links, oben rechts → vierte Ecke als Parallelogramm, dann von den Suchmuster-Mitten
            // auf den Rand erweitern (bei kleinen Versionen 1,5×, bei großen weniger – 1,25 passt meist gut)
            var fourth = new Point(p[0].X + p[2].X - p[1].X, p[0].Y + p[2].Y - p[1].Y);
            return new CodeMarker(Expand([p[1], p[2], fourth, p[0]], 1.25), label);
        }
        return new CodeMarker(Expand(ConvexHull(p), 1.04), label);
    }

    private static List<Point> Band(Point a, Point b)
    {
        var d = b - a;
        double length = d.Length;
        if (length < 1) return [new(a.X - 10, a.Y - 10), new(a.X + 10, a.Y - 10), new(a.X + 10, a.Y + 10), new(a.X - 10, a.Y + 10)];
        d /= length;
        var n = new Vector(-d.Y, d.X);
        double half = Math.Max(12, length * 0.22);
        var ext = d * Math.Max(4, length * 0.04);
        return [a - ext - n * half, b + ext - n * half, b + ext + n * half, a - ext + n * half];
    }

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
/// Format-Etikett, auf Wunsch mit Zielhilfe in der Mitte. Gezeichnet in OnRender: Linien bleiben unabhängig von
/// der Bildgröße gleich dick.
/// </summary>
public sealed class CodeView : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(BitmapSource), typeof(CodeView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MarkersProperty = DependencyProperty.Register(nameof(Markers), typeof(IReadOnlyList<CodeMarker>), typeof(CodeView),
        new FrameworkPropertyMetadata(Array.Empty<CodeMarker>(), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShowAimProperty = DependencyProperty.Register(nameof(ShowAim), typeof(bool), typeof(CodeView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(CodeView),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentTextProperty = DependencyProperty.Register(nameof(AccentText), typeof(Brush), typeof(CodeView),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Pen AimShadow = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)), 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });
    private static readonly Pen AimPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });

    public CodeView()
    {
        SetResourceReference(AccentProperty, "AccentFillColorDefaultBrush");
        SetResourceReference(AccentTextProperty, "TextOnAccentFillColorPrimaryBrush");
        SnapsToDevicePixels = true;
    }

    public BitmapSource? Source { get => (BitmapSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public IReadOnlyList<CodeMarker> Markers { get => (IReadOnlyList<CodeMarker>)GetValue(MarkersProperty); set => SetValue(MarkersProperty, value); }
    public bool ShowAim { get => (bool)GetValue(ShowAimProperty); set => SetValue(ShowAimProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public Brush AccentText { get => (Brush)GetValue(AccentTextProperty); set => SetValue(AccentTextProperty, value); }

    /// <summary>Wo das Bild im Element liegt (einpassend, mittig). Leer ohne Bild.</summary>
    public Rect ImageRect
    {
        get
        {
            if (Source is not { PixelWidth: > 0, PixelHeight: > 0 } src || ActualWidth <= 0 || ActualHeight <= 0) return Rect.Empty;
            double scale = Math.Min(ActualWidth / src.PixelWidth, ActualHeight / src.PixelHeight);
            double w = src.PixelWidth * scale, h = src.PixelHeight * scale;
            return new Rect((ActualWidth - w) / 2, (ActualHeight - h) / 2, w, h);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Durchsichtige Fläche, damit Ziehen und Ablegen überall im Element ankommen
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var rect = ImageRect;
        if (rect.IsEmpty || Source is null) return;
        double scale = rect.Width / Source.PixelWidth;
        dc.DrawImage(Source, rect);

        if (ShowAim) DrawAim(dc, rect);

        var accent = Accent;
        var fill = accent.CloneCurrentValue();
        fill.Opacity = 0.16;
        var pen = new Pen(accent, 3) { LineJoin = PenLineJoin.Round };
        var halo = new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)), 5) { LineJoin = PenLineJoin.Round };
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        foreach (var marker in Markers)
        {
            if (marker.Outline.Count < 2) continue;
            var points = marker.Outline.Select(q => new Point(rect.X + q.X * scale, rect.Y + q.Y * scale)).ToList();
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(points[0], isFilled: true, isClosed: true);
                ctx.PolyLineTo(points.Skip(1).ToList(), isStroked: true, isSmoothJoin: false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, halo, geometry);
            dc.DrawGeometry(fill, pen, geometry);

            if (marker.Label.Length == 0) continue;
            var text = new FormattedText(marker.Label, Labels.German, FlowDirection.LeftToRight, LabelFace, 12.5, AccentText, dpi);
            double left = points.Min(q => q.X), top = points.Min(q => q.Y), bottom = points.Max(q => q.Y);
            var box = new Rect(left, top - text.Height - 10, text.Width + 14, text.Height + 6);
            if (box.Top < 2) box.Y = Math.Min(bottom + 4, ActualHeight - box.Height - 2);
            box.X = Math.Clamp(box.X, 2, Math.Max(2, ActualWidth - box.Width - 2));
            dc.DrawRoundedRectangle(accent, null, box, 4, 4);
            dc.DrawText(text, new Point(box.X + 7, box.Y + 3));
        }
    }

    /// <summary>Zielhilfe: vier Winkel um die Bildmitte, hell mit dunklem Rand – auf jedem Hintergrund sichtbar.</summary>
    private static void DrawAim(DrawingContext dc, Rect rect)
    {
        double size = Math.Min(rect.Width, rect.Height) * 0.5;
        double arm = size * 0.16;
        var c = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        double l = c.X - size / 2, r = c.X + size / 2, t = c.Y - size / 2, b = c.Y + size / 2;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            void Corner(Point a, Point m, Point z)
            {
                ctx.BeginFigure(a, false, false);
                ctx.LineTo(m, true, true);
                ctx.LineTo(z, true, true);
            }
            Corner(new(l, t + arm), new(l, t), new(l + arm, t));
            Corner(new(r - arm, t), new(r, t), new(r, t + arm));
            Corner(new(r, b - arm), new(r, b), new(r - arm, b));
            Corner(new(l + arm, b), new(l, b), new(l, b - arm));
            // kleines Kreuz in der Mitte
            ctx.BeginFigure(new(c.X - 7, c.Y), false, false);
            ctx.LineTo(new(c.X + 7, c.Y), true, false);
            ctx.BeginFigure(new(c.X, c.Y - 7), false, false);
            ctx.LineTo(new(c.X, c.Y + 7), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, AimShadow, geometry);
        dc.DrawGeometry(null, AimPen, geometry);
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
