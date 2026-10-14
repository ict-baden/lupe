using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Lupe.Core.Model;
using Lupe.Core.Screen;
using Lupe.Interop;
using Lupe.Services;
using Lupe.UI.Theme;

namespace Lupe.UI;

/// <summary>
/// Auswahl, wenn der Bildschirm-Scan mehrere Codes gefunden hat: die Aktionen laufen für den gewählten – oder alle
/// werden zeilenweise kopiert. Tastatur: Pfeile, 1–9, Eingabe, Esc.
/// </summary>
public partial class CodeChoiceWindow : Window, IThemedWindow
{
    public enum Outcome { Cancelled, Chosen, CopyAll }

    // Geschätzte Höhe in Punkten, bevor das Layout feststeht (drei Codes und Knöpfe)
    private const double EstimatedHeight = 420;

    private readonly TaskCompletionSource<(Outcome, DecodedCode?)> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly (int X, int Y)? _anchor;
    private readonly MonitorArea? _monitor;

    /// <param name="anchor">Mitte der Auswahl in physischen Bildschirmpixeln – dort erscheint das Fenster.</param>
    /// <param name="monitor">Monitor der Auswahl: Das Fenster bleibt ganz auf seiner Arbeitsfläche.</param>
    /// <param name="actions">Was nach der Wahl passiert, für die Erklärung („kopieren und eintippen“).</param>
    internal CodeChoiceWindow(IReadOnlyList<CodeItem> items, string actions, (int X, int Y)? anchor, MonitorArea? monitor = null)
    {
        InitializeComponent();
        _anchor = anchor;
        _monitor = monitor;
        List.ItemsSource = items;
        List.SelectedIndex = 0;
        TitleText.Text = $"{items.Count} Codes gefunden – welcher?";
        ExplainText.Text = actions.Length == 0
            ? "Der gewählte kommt ins Ergebnis; alle stehen schon im Verlauf."
            : $"Der gewählte wird {actions}. Alle stehen schon im Verlauf.";
        PreviewKeyDown += OnKey;
        SourceInitialized += (_, _) => Place(EstimatedHeight);
        Loaded += (_, _) =>
        {
            // Jetzt mit der wirklichen Höhe (SizeToContent) nachsetzen
            Place(ActualHeight > 0 ? ActualHeight : EstimatedHeight);
            Activate();
            List.Focus();
            (List.ItemContainerGenerator.ContainerFromIndex(0) as UIElement)?.Focus();
        };
        Closed += (_, _) => _result.TrySetResult((Outcome.Cancelled, null));
    }

    public void ThemeChanged() { }

    /// <summary>Zeigt die Auswahl und wartet auf die Entscheidung.</summary>
    public Task<(Outcome Outcome, DecodedCode? Code)> ChooseAsync()
    {
        Show();
        return _result.Task;
    }

    /// <summary>
    /// Mittig auf die Auswahl, ganz auf der Arbeitsfläche ihres Monitors. Größe in Pixeln = Punkte × Skalierung des
    /// Ziel-Monitors (dort landet das Fenster; WPF rechnet es danach selbst auf dessen DPI um).
    /// </summary>
    private void Place(double heightDip)
    {
        if (_anchor is not { } a) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;
        double scale = _monitor?.Scale is > 0 ? _monitor.Scale : System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        int w = (int)Math.Ceiling(Width * scale), h = (int)Math.Ceiling(heightDip * scale);
        var (x, y) = _monitor is { } m ? Placement.CenterOn(a.X, a.Y, w, h, m.WorkArea) : (a.X - w / 2, a.Y - h / 2);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        int index = e.Key is >= Key.D1 and <= Key.D9 ? e.Key - Key.D1 : e.Key is >= Key.NumPad1 and <= Key.NumPad9 ? e.Key - Key.NumPad1 : -1;
        if (index >= 0 && index < List.Items.Count)
        {
            List.SelectedIndex = index;
            Take_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Cancel_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Take_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not CodeItem item) return;
        _result.TrySetResult((Outcome.Chosen, item.Code));
        Close();
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        _result.TrySetResult((Outcome.CopyAll, null));
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _result.TrySetResult((Outcome.Cancelled, null));
        Close();
    }

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Take_Click(sender, e);
}
