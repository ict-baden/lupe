using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Lupe.Core.Model;
using Lupe.Core.Screen;
using Lupe.Interop;
using Lupe.Services;

namespace Lupe.UI;

/// <summary>
/// Auswahl-Overlay über alle Monitore: abgedunkeltes Standbild des Desktops, Rechteck aufziehen, Esc oder Rechtsklick
/// bricht ab, Klick ohne Ziehen nimmt den ganzen Monitor unter dem Mauszeiger.
/// </summary>
/// <remarks>
/// Ein einziges Fenster über den ganzen virtuellen Desktop, in physischen Pixeln gesetzt (SetWindowPos). Der Inhalt ist
/// in Bildpixeln angelegt und um 1/DPI-Faktor des Fensters verkleinert – so liegt jedes Pixel des Standbilds genau
/// auf seinem Bildschirmpixel, auch wenn die Monitore verschiedene Skalierungen haben (WPF zeichnet ein Fenster mit
/// einer DPI; welche, verrät <see cref="VisualTreeHelper.GetDpi"/>).
/// </remarks>
internal sealed class SelectionOverlay : Window
{
    private const double ClickTolerance = 4; // Pixel: weniger Bewegung gilt als Klick

    private readonly PixelImage _desktop;
    private readonly PixelRect _virtual;
    private readonly IReadOnlyList<MonitorArea> _monitors;
    private readonly bool _screenshot;
    private readonly Canvas _surface;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly System.Windows.Shapes.Path _shade;
    private readonly Rectangle _frame;
    private readonly Border _sizeTag;
    private readonly TextBlock _sizeText;
    private readonly Border _hint;
    private readonly ScaleTransform _uiScale = new(1, 1);
    private Point? _start;
    private Rect _selection = Rect.Empty;
    private TaskCompletionSource<PixelRect?>? _result;

    public SelectionOverlay(PixelImage desktop, PixelRect virtualScreen, IReadOnlyList<MonitorArea> monitors, bool screenshot = false)
    {
        _desktop = desktop;
        _virtual = virtualScreen;
        _monitors = monitors;
        _screenshot = screenshot;

        Title = "Lupe – Ausschnitt wählen";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = !screenshot;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        UseLayoutRounding = false;
        SnapsToDevicePixels = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SetResourceReference(FontFamilyProperty, "UiFont");

        _surface = new Canvas { Width = desktop.Width, Height = desktop.Height, LayoutTransform = _scale, ClipToBounds = true };
        var image = new Image { Source = Images.ToBitmap(desktop), Width = desktop.Width, Height = desktop.Height, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        _surface.Children.Add(image);

        _shade = new System.Windows.Shapes.Path { Fill = new SolidColorBrush(Color.FromArgb(0x96, 0x08, 0x08, 0x10)), IsHitTestVisible = false };
        _surface.Children.Add(_shade);

        _frame = new Rectangle { StrokeThickness = 2, Visibility = Visibility.Collapsed, IsHitTestVisible = false, SnapsToDevicePixels = true };
        _frame.SetResourceReference(Shape.StrokeProperty, "AccentFillColorDefaultBrush");
        _surface.Children.Add(_frame);

        _sizeText = new TextBlock { FontSize = 12, Foreground = Brushes.White };
        _sizeTag = new Border
        {
            Child = _sizeText, CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2, 7, 3),
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x10, 0x10, 0x10)), Visibility = Visibility.Collapsed,
            IsHitTestVisible = false, LayoutTransform = _uiScale,
        };
        _surface.Children.Add(_sizeTag);

        _hint = BuildHint();
        _surface.Children.Add(_hint);

        Content = _surface;
        UpdateShade();

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            UpdateScale();
            PlaceWindow();
            PlaceHint();
            if (!_screenshot)
            {
                Activate();
                Focus();
            }
        };
        if (!screenshot)
        {
            // Wer wegklickt oder mit Alt+Tab wechselt, will nicht mehr wählen
            Deactivated += (_, _) => Finish(null);
            MouseLeftButtonDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseUp;
            MouseRightButtonUp += (_, e) => { e.Handled = true; Finish(null); };
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { e.Handled = true; Finish(null); }
            };
        }
        Closed += (_, _) => _result?.TrySetResult(null);
    }

    /// <summary>Zeigt das Overlay und wartet auf die Auswahl (Rechteck in Koordinaten des virtuellen Desktops) oder null bei Abbruch.</summary>
    public Task<PixelRect?> SelectAsync()
    {
        _result = new TaskCompletionSource<PixelRect?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Show();
        return _result.Task;
    }

    /// <summary>Nur für Screenshots: ein gewähltes Rechteck (in Bildpixeln) zeigen.</summary>
    public void ShowSelection(Rect rect)
    {
        _selection = rect;
        UpdateSelection();
    }

    /// <summary>Für Screenshots: Inhalt in Bildpixeln (ohne DPI-Ausgleich).</summary>
    public FrameworkElement Surface => _surface;

    // ================= Fenster =================

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(AllowLargerThanScreen);
        if (!_screenshot)
        {
            int disable = 1;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_TRANSITIONS_FORCEDISABLED, ref disable, sizeof(int));
        }
        PlaceWindow();
        UpdateScale();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateScale();
        // WPF hat das Fenster auf die neue DPI umgerechnet – wieder genau auf den virtuellen Desktop legen
        Dispatcher.BeginInvoke(PlaceWindow);
    }

    private void PlaceWindow()
    {
        if (_screenshot) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, _virtual.X, _virtual.Y, _virtual.Width, _virtual.Height, Native.SWP_NOACTIVATE);
    }

    private void UpdateScale()
    {
        var dpi = _screenshot ? new DpiScale(1, 1) : VisualTreeHelper.GetDpi(this);
        _scale.ScaleX = 1 / dpi.DpiScaleX;
        _scale.ScaleY = 1 / dpi.DpiScaleY;
        // Hinweis, Größenangabe und Rahmen sollen in normaler Größe erscheinen, nicht in Bildpixeln
        _uiScale.ScaleX = dpi.DpiScaleX;
        _uiScale.ScaleY = dpi.DpiScaleY;
        _frame.StrokeThickness = Math.Max(2, Math.Round(2 * dpi.DpiScaleX));
    }

    private static nint AllowLargerThanScreen(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != Native.WM_GETMINMAXINFO) return 0;
        var info = Marshal.PtrToStructure<Native.MINMAXINFO>(lParam);
        info.ptMaxSize = info.ptMaxTrackSize = new Native.POINT { X = 64_000, Y = 64_000 };
        Marshal.StructureToPtr(info, lParam, false);
        return 0;
    }

    // ================= Maus =================

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _start = Clamp(e.GetPosition(_surface));
        _selection = new Rect(_start.Value, _start.Value);
        _hint.Visibility = Visibility.Collapsed;
        CaptureMouse();
        UpdateSelection();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_start is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        _selection = new Rect(start, Clamp(e.GetPosition(_surface)));
        UpdateSelection();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_start is not { } start) return;
        ReleaseMouseCapture();
        var end = Clamp(e.GetPosition(_surface));
        _start = null;
        e.Handled = true;

        if (Math.Abs(end.X - start.X) < ClickTolerance && Math.Abs(end.Y - start.Y) < ClickTolerance)
        {
            // Klick ohne Ziehen: der ganze Monitor unter dem Mauszeiger
            int x = _virtual.X + (int)end.X, y = _virtual.Y + (int)end.Y;
            var monitor = _monitors.FirstOrDefault(m => x >= m.Bounds.X && x < m.Bounds.X + m.Bounds.Width && y >= m.Bounds.Y && y < m.Bounds.Y + m.Bounds.Height);
            Finish(monitor?.Bounds ?? _virtual);
            return;
        }
        var r = new Rect(start, end);
        Finish(new PixelRect(_virtual.X + (int)Math.Floor(r.X), _virtual.Y + (int)Math.Floor(r.Y), Math.Max(1, (int)Math.Ceiling(r.Width)), Math.Max(1, (int)Math.Ceiling(r.Height))));
    }

    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, _desktop.Width), Math.Clamp(p.Y, 0, _desktop.Height));

    private void Finish(PixelRect? result)
    {
        if (_result is null || _result.Task.IsCompleted) return;
        _result.TrySetResult(result);
        Close();
    }

    // ================= Zeichnen =================

    private void UpdateSelection()
    {
        UpdateShade();
        if (_selection.IsEmpty || _selection.Width < 1 || _selection.Height < 1)
        {
            _frame.Visibility = _sizeTag.Visibility = Visibility.Collapsed;
            return;
        }
        double t = _frame.StrokeThickness;
        Canvas.SetLeft(_frame, _selection.X - t);
        Canvas.SetTop(_frame, _selection.Y - t);
        _frame.Width = _selection.Width + 2 * t;
        _frame.Height = _selection.Height + 2 * t;
        _frame.Visibility = Visibility.Visible;

        _sizeText.Text = $"{(int)Math.Round(_selection.Width)} × {(int)Math.Round(_selection.Height)}";
        _sizeTag.Visibility = Visibility.Visible;
        _sizeTag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double below = _selection.Bottom + 8 * _uiScale.ScaleY;
        double top = below + _sizeTag.DesiredSize.Height <= _desktop.Height ? below : Math.Max(0, _selection.Top - _sizeTag.DesiredSize.Height - 8 * _uiScale.ScaleY);
        Canvas.SetLeft(_sizeTag, Math.Clamp(_selection.X, 0, Math.Max(0, _desktop.Width - _sizeTag.DesiredSize.Width)));
        Canvas.SetTop(_sizeTag, top);
    }

    private void UpdateShade()
    {
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry(new Rect(0, 0, _desktop.Width, _desktop.Height)));
        if (!_selection.IsEmpty && _selection.Width >= 1 && _selection.Height >= 1) group.Children.Add(new RectangleGeometry(_selection));
        group.Freeze();
        _shade.Data = group;
    }

    private Border BuildHint()
    {
        var text = new TextBlock
        {
            Text = "Rechteck um den Code aufziehen  ·  Klick: ganzer Bildschirm  ·  Esc: abbrechen",
            FontSize = 14, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
        };
        var icon = new TextBlock { Text = "\uE7A8", FontSize = 16, Foreground = Brushes.White, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(icon);
        panel.Children.Add(text);
        return new Border
        {
            Child = panel, CornerRadius = new CornerRadius(8), Padding = new Thickness(18, 10, 18, 11),
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x20, 0x20, 0x20)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            IsHitTestVisible = false, LayoutTransform = _uiScale,
        };
    }

    /// <summary>Hinweis oben mittig auf dem Hauptmonitor (sonst auf dem ganzen Bild).</summary>
    private void PlaceHint()
    {
        _hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var primary = _monitors.FirstOrDefault(m => m.IsPrimary)?.Bounds ?? _virtual;
        double x = primary.X - _virtual.X + (primary.Width - _hint.DesiredSize.Width) / 2;
        double y = primary.Y - _virtual.Y + 28 * _uiScale.ScaleY;
        Canvas.SetLeft(_hint, Math.Max(0, x));
        Canvas.SetTop(_hint, Math.Max(0, y));
    }

    /// <summary>Für Screenshots: Hinweis neu setzen, nachdem Größe und Maßstab feststehen.</summary>
    public void Relayout()
    {
        UpdateScale();
        PlaceHint();
        UpdateSelection();
    }
}
