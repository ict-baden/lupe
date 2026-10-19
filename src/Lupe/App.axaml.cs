// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Lupe.Core.Input;
using Lupe.Core.Model;
using Lupe.Core.Platform;
using Lupe.Core.Resources;
using Lupe.Services;
using Lupe.UI;
using Lupe.UI.Theme;

namespace Lupe;

/// <summary>
/// Die Oberfläche: Hauptfenster, Infobereich, globales Tastenkürzel, Bildschirm-Scan. Lupe läuft weiter, wenn das
/// Fenster geschlossen wird (Einstellung, Standard an) – beendet wird über das Symbol im Infobereich.
/// </summary>
public partial class App : Application
{
    private readonly StartOptions _options;
    private readonly SingleInstance? _instance;
    private AppState? _state;
    private MainWindow? _window;
    private AppTray? _tray;
    private IGlobalHotkey? _hotkey;
    private ScreenScan? _scan;
    private bool _exiting;
    private bool _cleanedUp;

    /// <summary>Für den XAML-Vorschauer; Lupe selbst startet über <see cref="Program"/>.</summary>
    public App() : this(new StartOptions(AppMode.Demo), null) { }

    internal App(StartOptions options, SingleInstance? instance)
    {
        _options = options;
        _instance = instance;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    private IClassicDesktopStyleApplicationLifetime? Desktop => ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        Dispatcher.UIThread.UnhandledException += OnUnhandled;
        ThemeManager.Initialize(this);
        // Screenshots stellen Theme und Zustand selbst her (Screenshots.RunAsync)
        if (_options.Mode == AppMode.Screenshots || Desktop is not { } desktop) return;

        var state = _options.Mode == AppMode.Demo ? AppState.OpenDemo(strict: false, DateTimeOffset.Now) : AppState.Open();
        _state = state;
        _scan = new ScreenScan(state, () => _window);
        state.ScreenScanRequested += StartScreenScan;
        state.WindowRequested += slug =>
        {
            ShowMainWindow();
            _window?.ShowPage(slug);
        };
        state.SettingsChanged += OnSettingsChanged;
        state.HotkeySuspendChanged += OnHotkeySuspendChanged;
        desktop.ShutdownRequested += (_, _) => Cleanup();
        desktop.Exit += (_, _) => Cleanup();

        // Infobereich nur für die echte Lupe – die Demo endet mit ihrem Fenster
        if (_options.Mode != AppMode.Demo)
        {
            _tray = new AppTray(this, "Lupe");
            _tray.ScanRequested += StartScreenScan;
            _tray.OpenRequested += ShowMainWindow;
            _tray.ExitRequested += () => _ = ExitAsync();
        }
        // Die Demo meldet kein globales Kürzel an: Neben einer echten Lupe wäre es sonst „belegt“ (oder nähme es ihr weg).
        // „Ausschnitt scannen“ geht dort über den Knopf.
        if (_options.Mode != AppMode.Demo) RegisterHotkey(state.Settings.ScreenHotkey);
        _instance?.Listen(() => Dispatcher.UIThread.Post(ShowMainWindow), () => Dispatcher.UIThread.Post(StartExternalScan));

        // --bildschirm als erste Lupe: gleich scannen, danach wie beim Autostart im Infobereich weiter
        if (_options.ScanOnStart) Dispatcher.UIThread.Post(StartExternalScan, DispatcherPriority.Background);
        // Autostart: nur im Infobereich – außer es gibt keinen (dann wäre Lupe unsichtbar und unerreichbar)
        if (_options.Mode == AppMode.Background && _tray?.IsVisible == true) return;
        ShowMainWindow();
    }

    // ================= Fenster =================

    private void ShowMainWindow()
    {
        if (_state is null || _exiting) return;
        if (_window is null)
        {
            _window = new MainWindow(_state);
            _window.Closing += OnWindowClosing;
        }
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        // Aktivieren allein reicht nicht immer (Fokusdiebstahl-Schutz) – kurz nach vorn holen
        _window.Topmost = true;
        _window.Topmost = false;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_exiting || _state is null) return;
        if (_state.Settings.KeepRunningInTray && _tray?.IsVisible == true)
        {
            e.Cancel = true;
            _window?.Hide();
            if (!_state.Settings.TrayHintShown)
            {
                ResultToast.ShowHint(_state, Strings.App_TrayHint_Title, string.Format(Strings.App_TrayHint_Text, HotkeyLabel()));
                _state.UpdateSettings(s => s with { TrayHintShown = true });
            }
            return;
        }
        // Erst die Kamera anhalten, dann schließen und beenden – aber nach diesem Ereignis: Während „Closing“ soll kein
        // weiteres Close() laufen. Die Ausnahme ginge sonst im verworfenen Task unter, das Fenster bliebe offen und beim
        // nächsten Schließen liefe Lupe unsichtbar weiter.
        e.Cancel = true;
        Dispatcher.UIThread.Post(() => _ = ExitAsync());
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        try
        {
            if (_window is not null) await _window.StopAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Beim Beenden nichts mehr melden – die Kamera gibt das System ohnehin frei
        }
        try { _window?.Close(); }
        catch (InvalidOperationException)
        {
            // Das Fenster schließt gerade schon – beendet wird trotzdem
        }
        Cleanup();
        Desktop?.Shutdown(0);
    }

    // ================= Tastenkürzel und Bildschirm-Scan =================

    private void StartScreenScan() => StartScreenScan(external: false);

    /// <summary><c>--bildschirm</c> (Tastenkürzel des Desktops): Der Scan kommt sicher aus einem anderen Programm.</summary>
    private void StartExternalScan() => StartScreenScan(external: true);

    private void StartScreenScan(bool external)
    {
        if (_state is null || _scan is null || _exiting) return;
        Ui.Run(_state, () => _scan.RunAsync(external), Strings.App_ScreenScan_Name);
    }

    private void RegisterHotkey(Hotkey hotkey)
    {
        if (_state is null) return;
        _hotkey?.Dispose();
        _hotkey = null;
        var label = HotkeyLabel(hotkey);
        try
        {
            var registered = LupePlatform.Current.Hotkeys.Register(hotkey);
            if (registered.IsRegistered)
            {
                registered.Pressed += (_, _) => Dispatcher.UIThread.Post(StartScreenScan);
                _hotkey = registered;
                _state.SetHotkeyStatus(true, null);
            }
            else
            {
                var error = registered.Error ?? string.Format(Strings.App_Hotkey_Unavailable, label);
                registered.Dispose();
                _state.SetHotkeyStatus(false, error);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _state.SetHotkeyStatus(false, string.Format(Strings.App_Hotkey_RegisterFailed, label, ex.Message));
        }
        _tray?.SetScanLabel(string.Format(Strings.App_Tray_ScanWithHotkey, label));
        _tray?.SetTooltip(_state.HotkeyError is null ? string.Format(Strings.App_Tray_TooltipReady, label) : Strings.App_Tray_TooltipNoHotkey);
    }

    private void OnSettingsChanged(LupeSettings old, LupeSettings now)
    {
        // Während der Aufnahme bleibt es abgemeldet – angemeldet wird das neue beim Ende der Aufnahme
        if (old.ScreenHotkey != now.ScreenHotkey && _options.Mode != AppMode.Demo && _state?.HotkeySuspended != true)
            RegisterHotkey(now.ScreenHotkey);
    }

    /// <summary>Die Einstellungen nehmen ein neues Tastenkürzel auf: solange das alte abmelden, sonst startete es einen Scan.</summary>
    private void OnHotkeySuspendChanged(bool suspend)
    {
        if (_state is null || _options.Mode == AppMode.Demo || _exiting) return;
        if (suspend)
        {
            _hotkey?.Dispose();
            _hotkey = null;
        }
        else if (_hotkey is null) RegisterHotkey(_state.Settings.ScreenHotkey);
    }

    private string HotkeyLabel() => _state is null ? "" : HotkeyLabel(_state.Settings.ScreenHotkey);

    private static string HotkeyLabel(Hotkey hotkey)
    {
        try { return HotkeyText.Format(hotkey); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return Strings.App_Hotkey_Fallback; }
    }

    // ================= Fehler und Aufräumen =================

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Niemals wegen eines Fehlers abstürzen – stattdessen freundlich melden (Screenshots: dort bricht der Lauf ab)
        if (_options.Mode == AppMode.Screenshots) return;
        e.Handled = true;
        _state?.Report(NoticeKind.Error, string.Format(Strings.App_UnexpectedError, e.Exception.Message));
    }

    private void Cleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        _hotkey?.Dispose();
        _hotkey = null;
        _tray?.Dispose();
        _tray = null;
        _state?.Dispose();
    }
}
