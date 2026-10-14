using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Lupe.Core.Model;
using Lupe.Core.Screen;
using Lupe.Interop;
using Lupe.Services;

namespace Lupe.UI;

/// <summary>
/// Hinweis nach einem Bildschirm-Scan, wenn das Lupe-Fenster nicht vorn ist: was erkannt wurde, was passiert ist,
/// Knöpfe zum Kopieren und Öffnen. Unten rechts auf dem Monitor des Ausschnitts, nimmt nie den Fokus (sonst landete
/// das Eintippen hier) und verschwindet nach ein paar Sekunden – außer die Maus steht darauf.
/// </summary>
public partial class ResultToast : Window
{
    private static ResultToast? _open;

    private readonly AppState _state;
    private readonly CodeItem? _item;
    private readonly PixelRect? _workArea;
    private readonly double _monitorScale;
    private readonly DispatcherTimer _timer;

    internal ResultToast(AppState state, CodeItem? item, string title, string done, string problems, MonitorArea? monitor, bool screenshot = false)
    {
        InitializeComponent();
        _state = state;
        _item = item;
        _workArea = monitor?.WorkArea;
        _monitorScale = monitor?.Scale is > 0 ? monitor.Scale : 1;

        TitleText.Text = title;
        SubtitleText.Text = item is null ? "Lupe · Bildschirm" : $"Lupe · {item.FormatLabel}";
        ContentText.Text = item?.Text ?? "Tipp: Rechteck etwas größer ziehen, den Code ganz einschließen – oder die Ansicht vergrößern.";
        ContentText.ToolTip = item?.ToolTipText;
        DoneText.Text = done;
        DoneRow.Visibility = done.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ProblemText.Text = problems;
        ProblemText.Visibility = problems.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        LinkButton.Visibility = item?.HasLink == true ? Visibility.Visible : Visibility.Collapsed;
        LinkButton.ToolTip = item?.LinkText;
        if (item is null)
        {
            IconGlyph.Text = "\uE7BA";
            IconCircle.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "SystemFillColorCautionBackgroundBrush");
            IconGlyph.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(item is null ? 5 : 8) };
        _timer.Tick += (_, _) =>
        {
            if (IsMouseOver) return;
            Close();
        };
        if (!screenshot)
        {
            SourceInitialized += (_, _) => Native.MakeNoActivate(new WindowInteropHelper(this).Handle);
            Loaded += (_, _) => Place();
            SizeChanged += (_, _) => Place();
            MouseLeave += (_, _) => { _timer.Stop(); _timer.Start(); };
        }
        Closed += (_, _) =>
        {
            _timer.Stop();
            if (_open == this) _open = null;
        };
    }

    /// <summary>Zeigt den Hinweis; ein noch offener früherer verschwindet.</summary>
    internal void Present()
    {
        _open?.Close();
        _open = this;
        Show();
        _timer.Start();
    }

    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;
        if (_workArea is not { } area)
        {
            // Monitor unbekannt: unten rechts auf dem Hauptmonitor (Arbeitsfläche in Punkten)
            var work = SystemParameters.WorkArea;
            Left = work.Right - ActualWidth;
            Top = work.Bottom - ActualHeight;
            return;
        }
        int w = (int)Math.Ceiling(ActualWidth * _monitorScale), h = (int)Math.Ceiling(ActualHeight * _monitorScale);
        // Der Rand des Fensters (Schatten) ist durchsichtig – bündig mit der Arbeitsfläche ergibt so 16 Punkte Abstand
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, area.X + area.Width - w, area.Y + area.Height - h, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_item is null) return;
        if (Shell.Copy(_item.Text) is { } error) ProblemText.Text = error;
        else DoneText.Text = Labels.Actions(ResultActions.Kopieren);
        DoneRow.Visibility = Visibility.Visible;
        ProblemText.Visibility = ProblemText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (_item?.Link is { } link && Shell.OpenLink(link) is { } error) ProblemText.Text = error;
        ProblemText.Visibility = ProblemText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _state.RequestWindow("bildschirm");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
