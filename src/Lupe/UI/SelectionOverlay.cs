// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Path = Avalonia.Controls.Shapes.Path;
using Lupe.Core.Model;
using Lupe.Core.Resources;
using Lupe.Interop;
using Lupe.Services;
using CorePixelRect = Lupe.Core.Screen.PixelRect;
using MonitorArea = Lupe.Core.Screen.MonitorArea;

namespace Lupe.UI;

/// <summary>
/// Auswahl-Overlay über alle Monitore: je Monitor ein randloses Fenster mit seinem Teil des abgedunkelten Standbilds,
/// Rechteck aufziehen, Esc oder Rechtsklick bricht ab, Klick ohne Ziehen nimmt den ganzen Monitor unter dem Mauszeiger.
/// </summary>
/// <remarks>
/// Ein Fenster je Monitor statt eines über den ganzen virtuellen Desktop: macOS und Wayland erlauben keine Fenster über
/// mehrere Bildschirme, und jeder Monitor hat so seine eigene Skalierung. Der Inhalt ist in Bildpixeln angelegt und um
/// 1/Skalierung verkleinert – so liegt jedes Pixel des Standbilds genau auf seinem Bildschirmpixel.
/// </remarks>
internal sealed class SelectionOverlay
{
    private readonly PixelImage _desktop;
    private readonly CorePixelRect _virtual;
    private readonly IReadOnlyList<MonitorArea> _monitors;
    private readonly List<OverlayWindow> _windows = [];
    private TaskCompletionSource<CorePixelRect?>? _result;

    public SelectionOverlay(PixelImage desktop, CorePixelRect virtualScreen, IReadOnlyList<MonitorArea> monitors)
    {
        _desktop = desktop;
        _virtual = virtualScreen;
        _monitors = monitors;
    }

    /// <summary>Zeigt das Overlay und wartet auf die Auswahl (Rechteck in Koordinaten des virtuellen Desktops) oder null bei Abbruch.</summary>
    public Task<CorePixelRect?> SelectAsync()
    {
        _result = new TaskCompletionSource<CorePixelRect?>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Ohne Monitorliste (System nennt keine): ein Fenster über das ganze Bild
        IEnumerable<MonitorArea> areas = _monitors.Count > 0
            ? _monitors
            : [new MonitorArea(_virtual, _virtual, 1, true, "")];
        bool anyPrimary = areas.Any(m => m.IsPrimary);
        bool first = true;
        foreach (var monitor in areas)
        {
            bool primary = monitor.IsPrimary || (!anyPrimary && first);
            first = false;
            var window = new OverlayWindow(_desktop, _virtual, monitor, primary, screenshot: false);
            window.Finished += Finish;
            // macOS: auf dem gerade sichtbaren Schreibtisch, auch über einer App im Vollbild (sonst wartet die Auswahl
            // unsichtbar auf dem Schreibtisch des Lupe-Fensters)
            Native.ShowOnEverySpace(window);
            window.Deactivated += (_, _) => Dispatcher.UIThread.Post(CheckActive, DispatcherPriority.Background);
            _windows.Add(window);
        }
        // Der Hauptmonitor zuletzt: Er bekommt den Fokus (Esc, Tasten)
        foreach (var window in _windows.OrderBy(w => w.IsPrimary)) window.Show();
        return _result.Task;
    }

    /// <summary>Wer wegklickt oder mit Alt+Tab wechselt, will nicht mehr wählen (Wechsel zwischen den Overlays zählt nicht).</summary>
    private void CheckActive()
    {
        if (_result is null || _result.Task.IsCompleted) return;
        if (_windows.Any(w => w.IsActive || w.IsDragging)) return;
        Finish(null);
    }

    private void Finish(CorePixelRect? result)
    {
        if (_result is null || _result.Task.IsCompleted) return;
        _result.TrySetResult(result);
        foreach (var window in _windows) window.CloseOverlay();
        _windows.Clear();
    }
}

/// <summary>Ein Overlay-Fenster für einen Monitor (für Screenshots auch ohne Eingabe).</summary>
internal sealed class OverlayWindow : Window
{
    private const double ClickTolerance = 4; // Pixel: weniger Bewegung gilt als Klick

    private readonly CorePixelRect _virtual;
    private readonly MonitorArea _monitor;
    private readonly bool _screenshot;
    private readonly int _width, _height;
    private readonly Canvas _surface;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly ScaleTransform _uiScale = new(1, 1);
    private readonly Path _shade;
    private readonly Rectangle _frame;
    private readonly LayoutTransformControl _sizeTag;
    private readonly TextBlock _sizeText;
    private readonly LayoutTransformControl? _hint;
    private Point? _start;
    private Rect? _selection;
    private bool _closing;

    /// <summary>Auswahl fertig (Rechteck im virtuellen Desktop) oder abgebrochen (null).</summary>
    public event Action<CorePixelRect?>? Finished;

    public bool IsPrimary { get; }
    public bool IsDragging => _start is not null;

    public OverlayWindow(PixelImage desktop, CorePixelRect virtualScreen, MonitorArea monitor, bool primary, bool screenshot)
    {
        _virtual = virtualScreen;
        _monitor = monitor;
        _screenshot = screenshot;
        IsPrimary = primary;

        Title = Strings.App_Overlay_Title;
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = !screenshot;
        Background = Brushes.Black;
        Cursor = new Cursor(StandardCursorType.Cross);
        UseLayoutRounding = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        this.SetResource(FontFamilyProperty, "UiFont");

        // Sein Teil des Standbilds (Koordinaten des Bildes = virtueller Desktop ab dessen linker oberer Ecke)
        var crop = Images.Crop(desktop, monitor.Bounds.X - virtualScreen.X, monitor.Bounds.Y - virtualScreen.Y, monitor.Bounds.Width, monitor.Bounds.Height)
                   ?? new PixelImage(1, 1, new byte[4]);
        _width = crop.Width;
        _height = crop.Height;

        _surface = new Canvas { Width = _width, Height = _height, ClipToBounds = true, Background = Brushes.Black };
        var image = new Image { Source = Images.ToBitmap(crop), Width = _width, Height = _height, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
        _surface.Children.Add(image);

        _shade = new Path { Fill = new SolidColorBrush(Color.FromArgb(0x96, 0x08, 0x08, 0x10)), IsHitTestVisible = false };
        _surface.Children.Add(_shade);

        _frame = new Rectangle { StrokeThickness = 2, IsVisible = false, IsHitTestVisible = false };
        _frame.SetResource(Shape.StrokeProperty, "AccentFillColorDefaultBrush");
        _surface.Children.Add(_frame);

        _sizeText = new TextBlock { FontSize = 12, Foreground = Brushes.White };
        _sizeTag = new LayoutTransformControl
        {
            LayoutTransform = _uiScale,
            IsVisible = false,
            IsHitTestVisible = false,
            Child = new Border
            {
                Child = _sizeText, CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2, 7, 3),
                Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x10, 0x10, 0x10)),
            },
        };
        _surface.Children.Add(_sizeTag);

        if (primary)
        {
            _hint = BuildHint();
            _surface.Children.Add(_hint);
        }

        Content = new LayoutTransformControl { LayoutTransform = _scale, Child = _surface };
        UpdateShade();

        if (!screenshot)
        {
            // Lage in Fensterkoordinaten; die Größe in Punkten folgt, sobald die Skalierung des Monitors feststeht
            Position = PositionOn(monitor);
            double guess = monitor.Scale > 0 ? monitor.Scale : 1;
            Width = monitor.Bounds.Width / guess;
            Height = monitor.Bounds.Height / guess;
            ShowActivated = primary;
            // Wayland setzt keine Fensterlage – dort deckt Vollbild den Monitor
            if (IsWayland) WindowState = WindowState.FullScreen;
            Opened += (_, _) =>
            {
                Native.DisableTransitions(this);
                Native.ShowOnEverySpace(this);
                FitToMonitor();
                if (primary) Activate();
            };
            ScalingChanged += (_, _) => FitToMonitor();
            _surface.PointerPressed += OnPointerPressed;
            _surface.PointerMoved += OnPointerMoved;
            _surface.PointerReleased += OnPointerReleased;
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { e.Handled = true; Finished?.Invoke(null); }
            };
        }
        Closed += (_, _) => { if (!_closing) Finished?.Invoke(null); };
        UpdateScale();
    }

    private static bool IsWayland => OperatingSystem.IsLinux()
        && string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase);

    /// <summary>Für Screenshots: Inhalt in Bildpixeln (ohne Skalierung).</summary>
    public Control Surface => _surface;

    public void CloseOverlay()
    {
        _closing = true;
        Close();
    }

    /// <summary>Nur für Screenshots: ein gewähltes Rechteck (in Bildpixeln) zeigen.</summary>
    public void ShowSelection(Rect rect)
    {
        _selection = rect;
        UpdateSelection();
    }

    // ================= Lage und Maßstab =================

    /// <summary>
    /// Pixel des Monitors je Punkt des Fensters. Windows und Linux: die Skalierung des Monitors selbst (Bounds sind dort
    /// echte Pixel je Monitor). macOS: der gemeinsame Faktor des Pixelraums (DisplayLayout, größter Faktor aller
    /// Bildschirme) – auf einem 1x-Monitor neben einem Retina-Bildschirm also 2, nicht die RenderScaling 1.
    /// </summary>
    internal static double PixelScale(bool macOS, MonitorArea monitor, double renderScaling) =>
        macOS && monitor.Scale > 0 ? monitor.Scale : renderScaling > 0 ? renderScaling : 1;

    /// <summary>Lage des Fensters: Avalonia.Native setzt sie in Punkten, die übrigen Plattformen in Pixeln.</summary>
    internal static (int X, int Y) WindowPosition(bool macOS, MonitorArea monitor) =>
        macOS && monitor.Scale > 0
            ? ((int)Math.Round(monitor.Bounds.X / monitor.Scale), (int)Math.Round(monitor.Bounds.Y / monitor.Scale))
            : (monitor.Bounds.X, monitor.Bounds.Y);

    private static PixelPoint PositionOn(MonitorArea monitor)
    {
        var (x, y) = WindowPosition(OperatingSystem.IsMacOS(), monitor);
        return new PixelPoint(x, y);
    }

    private void FitToMonitor()
    {
        double scale = PixelScale(OperatingSystem.IsMacOS(), _monitor, RenderScaling);
        if (!IsWayland)
        {
            Position = PositionOn(_monitor);
            Width = _monitor.Bounds.Width / scale;
            Height = _monitor.Bounds.Height / scale;
        }
        UpdateScale();
    }

    private void UpdateScale()
    {
        double scale = _screenshot ? 1 : PixelScale(OperatingSystem.IsMacOS(), _monitor, RenderScaling);
        _scale.ScaleX = _scale.ScaleY = 1 / scale;
        // Hinweis, Größenangabe und Rahmen sollen in normaler Größe erscheinen, nicht in Bildpixeln
        _uiScale.ScaleX = _uiScale.ScaleY = scale;
        _frame.StrokeThickness = Math.Max(2, Math.Round(2 * scale));
        PlaceHint();
        UpdateSelection();
    }

    // ================= Maus =================

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_surface);
        if (point.Properties.IsRightButtonPressed)
        {
            e.Handled = true;
            Finished?.Invoke(null);
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        _start = Clamp(point.Position);
        _selection = new Rect(_start.Value, _start.Value);
        if (_hint is not null) _hint.IsVisible = false;
        e.Pointer.Capture(_surface);
        UpdateSelection();
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_start is not { } start) return;
        _selection = new Rect(start, Clamp(e.GetPosition(_surface))).Normalize();
        UpdateSelection();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_start is not { } start) return;
        e.Pointer.Capture(null);
        var end = Clamp(e.GetPosition(_surface));
        _start = null;
        e.Handled = true;

        if (Math.Abs(end.X - start.X) < ClickTolerance && Math.Abs(end.Y - start.Y) < ClickTolerance)
        {
            // Klick ohne Ziehen: der ganze Monitor
            Finished?.Invoke(_monitor.Bounds);
            return;
        }
        var r = new Rect(start, end).Normalize();
        Finished?.Invoke(new CorePixelRect(_monitor.Bounds.X + (int)Math.Floor(r.X), _monitor.Bounds.Y + (int)Math.Floor(r.Y),
            Math.Max(1, (int)Math.Ceiling(r.Width)), Math.Max(1, (int)Math.Ceiling(r.Height))));
    }

    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, _width), Math.Clamp(p.Y, 0, _height));

    // ================= Zeichnen =================

    private void UpdateSelection()
    {
        UpdateShade();
        if (_selection is not { Width: >= 1, Height: >= 1 } selection)
        {
            _frame.IsVisible = _sizeTag.IsVisible = false;
            return;
        }
        double t = _frame.StrokeThickness;
        Canvas.SetLeft(_frame, selection.X - t);
        Canvas.SetTop(_frame, selection.Y - t);
        _frame.Width = selection.Width + 2 * t;
        _frame.Height = selection.Height + 2 * t;
        _frame.IsVisible = true;

        _sizeText.Text = $"{(int)Math.Round(selection.Width)} × {(int)Math.Round(selection.Height)}";
        _sizeTag.IsVisible = true;
        _sizeTag.Measure(Size.Infinity);
        double below = selection.Bottom + 8 * _uiScale.ScaleY;
        double top = below + _sizeTag.DesiredSize.Height <= _height ? below : Math.Max(0, selection.Top - _sizeTag.DesiredSize.Height - 8 * _uiScale.ScaleY);
        Canvas.SetLeft(_sizeTag, Math.Clamp(selection.X, 0, Math.Max(0, _width - _sizeTag.DesiredSize.Width)));
        Canvas.SetTop(_sizeTag, top);
    }

    private void UpdateShade()
    {
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry(new Rect(0, 0, _width, _height)));
        if (_selection is { Width: >= 1, Height: >= 1 } selection) group.Children.Add(new RectangleGeometry(selection));
        _shade.Data = group;
    }

    private LayoutTransformControl BuildHint()
    {
        var text = new TextBlock
        {
            Text = Strings.App_Overlay_Hint,
            FontSize = 14, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
        };
        var icon = new TextBlock { Text = "\uE7A8", FontSize = 16, Foreground = Brushes.White, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResource(TextBlock.FontFamilyProperty, "IconFont");
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(icon);
        panel.Children.Add(text);
        return new LayoutTransformControl
        {
            LayoutTransform = _uiScale,
            IsHitTestVisible = false,
            Child = new Border
            {
                Child = panel, CornerRadius = new CornerRadius(8), Padding = new Thickness(18, 10, 18, 11),
                Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x20, 0x20, 0x20)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            },
        };
    }

    /// <summary>Hinweis oben mittig (nur auf dem Hauptmonitor).</summary>
    private void PlaceHint()
    {
        if (_hint is null) return;
        _hint.Measure(Size.Infinity);
        Canvas.SetLeft(_hint, Math.Max(0, (_width - _hint.DesiredSize.Width) / 2));
        Canvas.SetTop(_hint, 28 * _uiScale.ScaleY);
    }

    /// <summary>Für Screenshots: Hinweis neu setzen, nachdem Größe und Maßstab feststehen.</summary>
    public void Relayout()
    {
        UpdateScale();
    }
}
