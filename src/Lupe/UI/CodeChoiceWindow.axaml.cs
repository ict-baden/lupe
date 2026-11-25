// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Lupe.Core.Model;
using Lupe.Core.Resources;
using Lupe.Interop;
using Lupe.Services;
using MonitorArea = Lupe.Core.Screen.MonitorArea;

namespace Lupe.UI;

/// <summary>
/// Auswahl, wenn der Bildschirm-Scan mehrere Codes gefunden hat: die Aktionen laufen für den gewählten – oder alle
/// werden zeilenweise kopiert. Tastatur: Pfeile, 1–9, Eingabe, Esc.
/// </summary>
public partial class CodeChoiceWindow : Window
{
    public enum Outcome { Cancelled, Chosen, CopyAll }

    private readonly TaskCompletionSource<(Outcome, DecodedCode?)> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly (int X, int Y)? _anchor;
    private readonly MonitorArea? _monitor;

    /// <summary>Für den XAML-Vorschauer.</summary>
    public CodeChoiceWindow() => InitializeComponent();

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
        TitleText.Text = string.Format(Strings.App_Choice_Title, items.Count);
        ExplainText.Text = actions.Length == 0
            ? Strings.App_Choice_ExplainNoActions
            : string.Format(Strings.App_Choice_Explain, actions);
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            Native.ShowOnEverySpace(this);
            Place();
            Activate();
            List.Focus();
            List.ContainerFromIndex(0)?.Focus();
        };
        // Jetzt mit der wirklichen Höhe (SizeToContent) nachsetzen
        SizeChanged += (_, _) => Place();
        Closed += (_, _) => _result.TrySetResult((Outcome.Cancelled, null));
    }

    /// <summary>Zeigt die Auswahl und wartet auf die Entscheidung.</summary>
    public Task<(Outcome Outcome, DecodedCode? Code)> ChooseAsync()
    {
        Native.ShowOnEverySpace(this);
        Show();
        return _result.Task;
    }

    /// <summary>Mittig auf die Auswahl, ganz auf der Arbeitsfläche ihres Monitors (Größe in Pixeln = Punkte × Skalierung).</summary>
    private void Place()
    {
        if (_anchor is not { } a || Bounds.Height <= 0) return;
        double scale = _monitor?.Scale is > 0 ? _monitor.Scale : DesktopScaling;
        int w = (int)Math.Ceiling(Bounds.Width * scale), h = (int)Math.Ceiling(Bounds.Height * scale);
        var (x, y) = _monitor is { } m ? Placement.CenterOn(a.X, a.Y, w, h, m.WorkArea) : (a.X - w / 2, a.Y - h / 2);
        Position = new PixelPoint(x, y);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        int index = e.Key is >= Key.D1 and <= Key.D9 ? e.Key - Key.D1 : e.Key is >= Key.NumPad1 and <= Key.NumPad9 ? e.Key - Key.NumPad1 : -1;
        if (index >= 0 && index < List.ItemCount)
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
        else if (e.Key == Key.Enter)
        {
            Take_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Take_Click(object? sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not CodeItem item) return;
        _result.TrySetResult((Outcome.Chosen, item.Code));
        Close();
    }

    private void CopyAll_Click(object? sender, RoutedEventArgs e)
    {
        _result.TrySetResult((Outcome.CopyAll, null));
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        _result.TrySetResult((Outcome.Cancelled, null));
        Close();
    }

    private void List_DoubleTapped(object? sender, TappedEventArgs e) => Take_Click(sender, e);
}
