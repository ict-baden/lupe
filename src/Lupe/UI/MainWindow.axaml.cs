using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Lupe.Core.Input;
using Lupe.Interop;
using Lupe.UI.Pages;
using Lupe.UI.Theme;

namespace Lupe.UI;

/// <summary>Hauptfenster: Navigation links (wie Vitrine und Zutritt), Seiten rechts, Meldungen unten.</summary>
public partial class MainWindow : Window
{
    private sealed record PageEntry(NavButton Nav, string Slug, string Title, string Subtitle, UserControl Page);

    /// <summary>Seiten in der Reihenfolge, in der <c>--screenshots</c> sie aufnimmt (Dateiname = Kürzel).</summary>
    internal static readonly string[] PageSlugs = ["kamera", "bildschirm", "datei", "verlauf", "einstellungen", "info"];

    private readonly AppState _state = null!;
    private readonly bool _screenshot;
    private readonly PageEntry[] _pages = [];
    private readonly FilePage _file = null!;
    private readonly DispatcherTimer _infoTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private PageEntry? _current;
    private bool _pageVisible;

    /// <summary>Für den XAML-Vorschauer.</summary>
    public MainWindow() => InitializeComponent();

    internal MainWindow(AppState state, bool screenshotMode = false)
    {
        InitializeComponent();
        _state = state;
        _screenshot = screenshotMode;
        _file = new FilePage(state);
        _pages =
        [
            new(NavCamera, "kamera", "Kamera", "Code vor die Webcam halten – Lupe erkennt ihn von selbst", new CameraPage(state)),
            new(NavScreen, "bildschirm", "Bildschirm", "Einen Ausschnitt des Bildschirms lesen – per Tastenkürzel aus jedem Programm", new ScreenPage(state)),
            new(NavFile, "datei", "Datei", "Bild öffnen, hierher ziehen oder aus der Zwischenablage einfügen", _file),
            new(NavHistory, "verlauf", "Verlauf", "Alles, was Lupe erkannt hat – neueste zuerst", new HistoryPage(state)),
            new(NavSettings, "einstellungen", "Einstellungen", "Was mit Ergebnissen passiert – Änderungen gelten und speichern sofort", new SettingsPage(state)),
            new(NavInfo, "info", "Info", "Über Lupe", new InfoPage(state)),
        ];
        foreach (var page in _pages) page.Nav.IsCheckedChanged += Nav_Checked;

        _infoTimer.Tick += (_, _) =>
        {
            _infoTimer.Stop();
            InfoBar.IsVisible = false;
        };

        state.Notice += ShowNotice;
        state.PageRequested += ShowPage;
        state.HotkeyChanged += UpdateStatus;
        state.SettingsChanged += OnSettingsChanged;
        state.HistoryChanged += UpdateBadges;
        Closed += (_, _) =>
        {
            _infoTimer.Stop();
            state.Notice -= ShowNotice;
            state.PageRequested -= ShowPage;
            state.HotkeyChanged -= UpdateStatus;
            state.SettingsChanged -= OnSettingsChanged;
            state.HistoryChanged -= UpdateBadges;
        };

        Opened += (_, _) =>
        {
            // Screenshots: kein Mica (gibt es in Bildern nicht), normaler Hintergrund aus dem Theme
            if (!_screenshot)
            {
                ThemeManager.ApplyWindowChrome(this);
                // Ohne Ein- und Ausblend-Animation: Beim Bildschirm-Scan muss das Fenster sofort weg sein
                Native.DisableTransitions(this);
            }
            if (state.StartupProblem is { } problem) ShowNotice(NoticeKind.Warning, problem);
        };
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        if (state.IsDemo && !screenshotMode)
        {
            Title = "Lupe – Demo";
            AppTitle.Text = "Lupe · Demo";
        }

        UpdateStatus();
        UpdateBadges();
        NavCamera.IsChecked = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty || change.Property == WindowStateProperty) SyncPageVisibility();
    }

    // ================= Navigation =================

    private void Nav_Checked(object? sender, RoutedEventArgs e)
    {
        if (sender is not NavButton { IsChecked: true } nav || _pages.FirstOrDefault(p => p.Nav == nav) is not { } entry || entry == _current) return;
        if (_current is not null && _pageVisible) ((IPage)_current.Page).OnHidden();
        _pageVisible = false;
        _current = entry;
        PageHost.Content = entry.Page;
        PageTitle.Text = entry.Title;
        PageSubtitle.Text = entry.Subtitle;
        SyncPageVisibility();
    }

    public void ShowPage(string slug)
    {
        var entry = _pages.FirstOrDefault(p => p.Slug == slug) ?? _pages[0];
        if (entry.Nav.IsChecked == true) SyncPageVisibility();
        else entry.Nav.IsChecked = true;
    }

    /// <summary>Die aktuelle Seite erfährt, ob sie zu sehen ist – die Kamera läuft nur, solange man sie sieht.</summary>
    private void SyncPageVisibility()
    {
        if (_current is null) return;
        bool visible = _screenshot || (IsVisible && WindowState != WindowState.Minimized);
        if (visible == _pageVisible) return;
        _pageVisible = visible;
        if (visible) ((IPage)_current.Page).OnShown();
        else ((IPage)_current.Page).OnHidden();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // Strg (macOS: Befehlstaste)
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.KeyModifiers != command) return;
        // Strg+V außerhalb von Eingabefeldern: Bild aus der Zwischenablage lesen (Seite „Datei“)
        if (e.Key == Key.V && FocusManager?.GetFocusedElement() is not TextBox)
        {
            ShowPage("datei");
            _file.PasteFromClipboard();
            e.Handled = true;
            return;
        }
        // Strg+1 … Strg+6 springt zu den Seiten in der Reihenfolge der Navigation
        int index = e.Key is >= Key.D1 and <= Key.D6 ? e.Key - Key.D1 : e.Key is >= Key.NumPad1 and <= Key.NumPad6 ? e.Key - Key.NumPad1 : -1;
        if (index >= 0)
        {
            _pages[index].Nav.IsChecked = true;
            e.Handled = true;
        }
    }

    // ================= Status links =================

    private void OnSettingsChanged(Core.Model.LupeSettings old, Core.Model.LupeSettings now)
    {
        if (old.ScreenHotkey != now.ScreenHotkey) UpdateStatus();
    }

    private void UpdateStatus()
    {
        string hotkey;
        try { hotkey = HotkeyText.Format(_state.Settings.ScreenHotkey); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { hotkey = "Tastenkürzel"; }
        StatusTitle.Text = hotkey;
        if (_state.HotkeyOffInDemo)
        {
            // Die Demo meldet kein globales Kürzel an (neben einer echten Lupe wäre es sonst belegt)
            StatusDetail.Text = "in der Demo aus";
            StatusCircle.SetResource(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
            StatusGlyph.SetResource(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            StatusGlyph.Text = "\uE765";
            ToolTip.SetTip(StatusCircle, "In der Demo ist das Tastenkürzel nicht angemeldet – „Ausschnitt scannen“ auf der Seite Bildschirm geht trotzdem.");
            NavScreen.Badge = null;
        }
        else if (_state.HotkeyError is { } error)
        {
            StatusDetail.Text = "nicht verfügbar – siehe Einstellungen";
            StatusCircle.SetResource(Border.BackgroundProperty, "SystemFillColorCautionBackgroundBrush");
            StatusGlyph.SetResource(TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");
            StatusGlyph.Text = "\uE7BA";
            ToolTip.SetTip(StatusCircle, error);
            NavScreen.Badge = "!";
            NavScreen.BadgeWarning = true;
        }
        else
        {
            StatusDetail.Text = _state.HotkeyActive ? "scannt einen Ausschnitt" : "noch nicht angemeldet";
            StatusCircle.SetResource(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
            StatusGlyph.SetResource(TextBlock.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
            StatusGlyph.Text = "\uE765";
            ToolTip.SetTip(StatusCircle, $"{hotkey} scannt einen Bildschirmausschnitt – aus jedem Programm.");
            NavScreen.Badge = null;
            NavScreen.BadgeWarning = false;
        }
    }

    private void UpdateBadges()
    {
        int count = _state.History.Entries.Count;
        NavHistory.Badge = count == 0 ? null : count > 999 ? "999+" : count.ToString(Labels.German);
    }

    // ================= Meldungen =================

    private void ShowNotice(NoticeKind kind, string text)
    {
        var (glyph, iconBrush, background) = kind switch
        {
            NoticeKind.Error => ("\uE783", "SystemFillColorCriticalBrush", "SystemFillColorCriticalBackgroundBrush"),
            NoticeKind.Warning => ("\uE7BA", "SystemFillColorCautionBrush", "SystemFillColorCautionBackgroundBrush"),
            NoticeKind.Success => ("\uE73E", "SystemFillColorSuccessBrush", "SystemFillColorSuccessBackgroundBrush"),
            _ => ("\uE946", "AccentTextFillColorPrimaryBrush", (string?)null),
        };
        InfoIcon.Text = glyph;
        InfoText.Text = text;
        InfoIcon.SetResource(TextBlock.ForegroundProperty, iconBrush);
        if (background is null)
        {
            InfoBar.ClearValue(Border.BackgroundProperty);
            InfoBar.Background = Brushes.Transparent;
        }
        else InfoBar.SetResource(Border.BackgroundProperty, background);
        InfoBar.IsVisible = true;

        // Erfolg und Hinweise verschwinden von selbst, Fehler und Warnungen bleiben bis zum Schließen
        _infoTimer.Stop();
        if (kind is NoticeKind.Info or NoticeKind.Success) _infoTimer.Start();
    }

    private void CloseInfo_Click(object? sender, RoutedEventArgs e)
    {
        _infoTimer.Stop();
        InfoBar.IsVisible = false;
    }

    // ================= Beenden und Screenshots =================

    /// <summary>Kamera und laufende Arbeit beenden (vor dem Schließen der App).</summary>
    internal async Task StopAsync()
    {
        foreach (var page in _pages.Select(p => p.Page).OfType<CameraPage>()) await page.StopCameraAsync();
    }

    internal UserControl PageFor(string slug) => (_pages.FirstOrDefault(p => p.Slug == slug) ?? _pages[0]).Page;

    internal async Task PrepareScreenshotAsync(string slug)
    {
        _infoTimer.Stop();
        InfoBar.IsVisible = false;
        ShowPage(slug);
        await ((IPage)_current!.Page).PrepareScreenshotAsync();
    }
}
