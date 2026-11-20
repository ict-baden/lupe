// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Lupe.Core.Model;
using Lupe.Core.Resources;
using Lupe.Interop;
using Lupe.Services;
using CorePixelRect = Lupe.Core.Screen.PixelRect;
using MonitorArea = Lupe.Core.Screen.MonitorArea;

namespace Lupe.UI;

/// <summary>
/// Hinweis nach einem Bildschirm-Scan, wenn das Lupe-Fenster nicht vorn ist: was erkannt wurde, was passiert ist,
/// Knöpfe zum Kopieren und Öffnen. Unten rechts auf dem Monitor des Ausschnitts, nimmt nie den Fokus (sonst landete
/// das Eintippen hier) und verschwindet nach ein paar Sekunden – außer die Maus steht darauf.
/// </summary>
public partial class ResultToast : Window
{
    private static ResultToast? _open;

    private readonly AppState _state = null!;
    private readonly CodeItem? _item;
    private readonly CorePixelRect? _workArea;
    private readonly DispatcherTimer _timer = new();
    private string _page = "bildschirm";

    /// <summary>Für den XAML-Vorschauer.</summary>
    public ResultToast() => InitializeComponent();

    internal ResultToast(AppState state, CodeItem? item, string title, string done, string problems, MonitorArea? monitor, bool screenshot = false)
    {
        InitializeComponent();
        _state = state;
        _item = item;
        _workArea = monitor?.WorkArea;

        TitleText.Text = title;
        SubtitleText.Text = $"Lupe · {(item is null ? Strings.App_Nav_Screen : item.FormatLabel)}";
        ContentText.Text = item?.Text ?? Strings.App_Toast_NoCodeTip;
        ToolTip.SetTip(ContentText, item?.ToolTipText);
        DoneText.Text = done;
        DoneRow.IsVisible = done.Length > 0;
        ProblemText.Text = problems;
        ProblemText.IsVisible = problems.Length > 0;
        CopyButton.IsVisible = item is not null;
        LinkButton.IsVisible = item?.HasLink == true;
        ToolTip.SetTip(LinkButton, item?.LinkText);
        if (item is null)
        {
            IconGlyph.Text = "\uE7BA";
            IconCircle.SetResource(Border.BackgroundProperty, "SystemFillColorCautionBackgroundBrush");
            IconGlyph.SetResource(TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");
        }

        _timer.Interval = TimeSpan.FromSeconds(item is null ? 5 : 8);
        _timer.Tick += (_, _) =>
        {
            if (IsPointerOver) return;
            Close();
        };
        if (screenshot)
        {
            // Im Bild auf dem Hintergrund der App (ein durchsichtiges Fenster gibt es dort nicht)
            this.SetResource(BackgroundProperty, "ApplicationBackgroundBrush");
            // Ohne Schatten: Skia zeichnet den Inhalt einer Fläche mit BoxShadow in einer RenderTargetBitmap mit mehr als
            // 96 dpi vergrößert (Avalonia 12.1.4)
            Card.BoxShadow = default;
        }
        else
        {
            Opened += (_, _) =>
            {
                Native.MakeNoActivate(this);
                Place();
            };
            SizeChanged += (_, _) => Place();
            PointerExited += (_, _) => { _timer.Stop(); _timer.Start(); };
        }
        Closed += (_, _) =>
        {
            _timer.Stop();
            if (_open == this) _open = null;
        };
    }

    /// <summary>Ein allgemeiner Hinweis (z. B. „Lupe läuft weiter“) unten rechts, ohne Code.</summary>
    internal static void ShowHint(AppState state, string title, string text)
    {
        var toast = new ResultToast(state, null, title, "", "", null)
        {
            _page = "kamera",
        };
        toast.ContentText.Text = text;
        toast.SubtitleText.Text = "Lupe";
        toast.IconGlyph.Text = "\uE946";
        toast.IconCircle.SetResource(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
        toast.IconGlyph.SetResource(TextBlock.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
        toast.OpenText.Text = Strings.App_Tray_Open;
        toast._timer.Interval = TimeSpan.FromSeconds(8);
        toast.Present();
    }

    /// <summary>Zeigt den Hinweis; ein noch offener früherer verschwindet.</summary>
    internal void Present()
    {
        CloseOpen();
        _open = this;
        Show();
        _timer.Start();
    }

    /// <summary>Schließt einen noch offenen Hinweis (vor dem nächsten Bildschirmfoto: er liegt immer oben). true, wenn einer offen war.</summary>
    internal static bool CloseOpen()
    {
        if (_open is not { } open) return false;
        open.Close();
        return true;
    }

    /// <summary>Unten rechts auf die Arbeitsfläche – der durchsichtige Rand (Schatten) ergibt 16 Punkte Abstand.</summary>
    private void Place()
    {
        double scale = DesktopScaling > 0 ? DesktopScaling : 1;
        int w = (int)Math.Ceiling(Bounds.Width * scale), h = (int)Math.Ceiling(Bounds.Height * scale);
        if (w <= 0 || h <= 0) return;
        PixelRect area;
        if (_workArea is { } work) area = new PixelRect(work.X, work.Y, work.Width, work.Height);
        else if (Screens.Primary is { } primary) area = primary.WorkingArea;
        else return;
        Position = new PixelPoint(area.X + area.Width - w, area.Y + area.Height - h);
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (_item is null) return;
        if (await Shell.CopyAsync(_item.Text) is { } error) ProblemText.Text = error;
        else
        {
            DoneText.Text = Labels.Actions(ResultActions.Kopieren);
            _state.Hits.Record(_item, ResultActions.Kopieren);
        }
        DoneRow.IsVisible = DoneText.Text?.Length > 0;
        ProblemText.IsVisible = ProblemText.Text?.Length > 0;
    }

    private void Link_Click(object? sender, RoutedEventArgs e)
    {
        if (_item?.Link is not { } link) return;
        if (Shell.OpenLink(link) is { } error) ProblemText.Text = error;
        else _state.Hits.Record(_item, ResultActions.LinkOeffnen);
        ProblemText.IsVisible = ProblemText.Text?.Length > 0;
    }

    private void Open_Click(object? sender, RoutedEventArgs e)
    {
        Close();
        _state.RequestWindow(_page);
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
