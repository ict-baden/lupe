using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Lupe.Core.Input;
using Lupe.Interop;
using Lupe.UI.Pages;
using Lupe.UI.Theme;

namespace Lupe.UI;

/// <summary>Hauptfenster: Navigation links (wie Vitrine und Zutritt), Seiten rechts, Meldungen unten.</summary>
public partial class MainWindow : Window, IThemedWindow
{
    private sealed record PageEntry(RadioButton Nav, string Slug, string Title, string Subtitle, UserControl Page);

    /// <summary>Seiten in der Reihenfolge, in der <c>--screenshots</c> sie aufnimmt (Dateiname = Kürzel).</summary>
    internal static readonly string[] PageSlugs = ["kamera", "bildschirm", "datei", "verlauf", "einstellungen", "info"];

    private readonly AppState _state;
    private readonly bool _screenshot;
    private readonly PageEntry[] _pages;
    private readonly FilePage _file;
    private readonly DispatcherTimer _infoTimer;
    private PageEntry? _current;
    private bool _pageVisible;

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

        _infoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _infoTimer.Tick += (_, _) =>
        {
            _infoTimer.Stop();
            InfoBar.Visibility = Visibility.Collapsed;
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

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => { if (state.StartupProblem is { } problem) ShowNotice(NoticeKind.Warning, problem); };
        IsVisibleChanged += (_, _) => SyncPageVisibility();
        StateChanged += (_, _) => SyncPageVisibility();
        PreviewKeyDown += OnPreviewKeyDown;
        if (state.IsDemo && !screenshotMode)
        {
            Title = "Lupe – Demo";
            AppTitle.Text = "Lupe · Demo";
        }

        UpdateStatus();
        UpdateBadges();
        NavCamera.IsChecked = true;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Screenshots: kein Mica (gibt es in Bildern nicht), normaler Hintergrund aus dem Theme
        if (_screenshot) return;
        ThemeManager.ApplyWindowChrome(this);
        // Ohne Ein- und Ausblend-Animation: Beim Bildschirm-Scan muss das Fenster sofort weg sein, sonst steht es
        // halb durchsichtig im Standbild
        int disable = 1;
        Native.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, Native.DWMWA_TRANSITIONS_FORCEDISABLED, ref disable, sizeof(int));
    }

    public void ThemeChanged()
    {
        if (!_screenshot) ThemeManager.ApplyWindowChrome(this);
    }

    // ================= Navigation =================

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        // Während InitializeComponent gibt es die Seiten noch nicht
        if (_pages is null || _pages.FirstOrDefault(p => p.Nav == sender) is not { } entry) return;
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

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        // Strg+V außerhalb von Eingabefeldern: Bild aus der Zwischenablage lesen (Seite „Datei“)
        if (e.Key == Key.V && Keyboard.FocusedElement is not TextBox)
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
        if (_state.HotkeyError is { } error)
        {
            StatusDetail.Text = "belegt – siehe Einstellungen";
            StatusCircle.SetResourceReference(Border.BackgroundProperty, "SystemFillColorCautionBackgroundBrush");
            StatusGlyph.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");
            StatusGlyph.Text = "\uE7BA";
            StatusCircle.ToolTip = error;
            Nav.SetBadge(NavScreen, "!");
            Nav.SetBadgeTone(NavScreen, "Warnung");
        }
        else
        {
            StatusDetail.Text = _state.HotkeyActive ? "scannt einen Ausschnitt" : "noch nicht angemeldet";
            StatusCircle.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
            StatusGlyph.SetResourceReference(TextBlock.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
            StatusGlyph.Text = "\uE765";
            StatusCircle.ToolTip = $"{hotkey} scannt einen Bildschirmausschnitt – aus jedem Programm.";
            Nav.SetBadge(NavScreen, null);
        }
    }

    private void UpdateBadges()
    {
        int count = _state.History.Entries.Count;
        Nav.SetBadge(NavHistory, count == 0 ? null : count > 999 ? "999+" : count.ToString(Labels.German));
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
        InfoIcon.SetResourceReference(TextBlock.ForegroundProperty, iconBrush);
        if (background is null) InfoBar.Background = System.Windows.Media.Brushes.Transparent;
        else InfoBar.SetResourceReference(Border.BackgroundProperty, background);
        InfoBar.Visibility = Visibility.Visible;

        // Erfolg und Hinweise verschwinden von selbst, Fehler und Warnungen bleiben bis zum Schließen
        _infoTimer.Stop();
        if (kind is NoticeKind.Info or NoticeKind.Success) _infoTimer.Start();
    }

    private void CloseInfo_Click(object sender, RoutedEventArgs e)
    {
        _infoTimer.Stop();
        InfoBar.Visibility = Visibility.Collapsed;
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
        InfoBar.Visibility = Visibility.Collapsed;
        ShowPage(slug);
        await ((IPage)_current!.Page).PrepareScreenshotAsync();
    }
}
