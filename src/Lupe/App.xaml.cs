using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Lupe.Cli;
using Lupe.Core.Input;
using Lupe.Core.Model;
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
    private TrayIcon? _tray;
    private GlobalHotkey? _hotkey;
    private ScreenScan? _scan;
    private bool _exiting;
    private bool _cleanedUp;

    internal App(StartOptions options, SingleInstance? instance)
    {
        _options = options;
        _instance = instance;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (_options.Mode == AppMode.Screenshots)
        {
            ConsoleSession.Attach();
            Dispatcher.InvokeAsync(RunScreenshotsAsync);
            return;
        }

        ThemeManager.Initialize();
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
        SessionEnding += (_, _) => Cleanup();

        // Infobereich nur für die echte Lupe – die Demo endet mit ihrem Fenster
        if (_options.Mode != AppMode.Demo)
        {
            _tray = new TrayIcon("Lupe");
            _tray.ScanRequested += StartScreenScan;
            _tray.OpenRequested += ShowMainWindow;
            _tray.ExitRequested += () => _ = ExitAsync();
        }
        RegisterHotkey(state.Settings.ScreenHotkey);
        _instance?.Listen(() => Dispatcher.BeginInvoke(ShowMainWindow));

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
            MainWindow = _window;
        }
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        // Aktivieren allein reicht nicht immer (Fokusdiebstahl-Schutz) – kurz nach vorn holen
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting || _state is null) return;
        if (_state.Preferences.KeepRunningInTray && _tray?.IsVisible == true)
        {
            e.Cancel = true;
            _window?.Hide();
            if (!_state.Preferences.TrayHintShown)
            {
                _tray.ShowBalloon("Lupe läuft weiter", $"{HotkeyLabel()} scannt einen Ausschnitt. Beenden über das Lupe-Symbol im Infobereich.");
                _state.UpdatePreferences(p => p with { TrayHintShown = true });
            }
            return;
        }
        // Erst die Kamera anhalten, dann schließen und beenden – aber nach diesem Ereignis: Während „Closing“ lässt WPF
        // kein weiteres Close() zu (InvalidOperationException), und ExitAsync liefe ohne Kamera bis dahin synchron durch.
        // Die Ausnahme ginge im verworfenen Task unter: Fenster bliebe offen, beim nächsten Schließen liefe Lupe
        // unsichtbar weiter und hielte die Einzelinstanz fest.
        e.Cancel = true;
        Dispatcher.BeginInvoke(() => _ = ExitAsync());
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
            // Beim Beenden nichts mehr melden – die Kamera gibt Windows ohnehin frei
        }
        try { _window?.Close(); }
        catch (InvalidOperationException)
        {
            // Das Fenster schließt gerade schon – beendet wird trotzdem
        }
        Cleanup();
        Shutdown(0);
    }

    // ================= Tastenkürzel und Bildschirm-Scan =================

    private void StartScreenScan()
    {
        if (_state is null || _scan is null || _exiting) return;
        Ui.Run(_state, _scan.RunAsync, "Bildschirm-Scan");
    }

    private void RegisterHotkey(Hotkey hotkey)
    {
        if (_state is null) return;
        _hotkey?.Dispose();
        _hotkey = null;
        var label = HotkeyLabel(hotkey);
        try
        {
            var registered = new GlobalHotkey(hotkey);
            if (registered.IsRegistered)
            {
                registered.Pressed += (_, _) => Dispatcher.BeginInvoke(StartScreenScan);
                _hotkey = registered;
                _state.SetHotkeyStatus(true, null);
            }
            else
            {
                var error = registered.Error ?? $"{label} ist nicht verfügbar.";
                registered.Dispose();
                _state.SetHotkeyStatus(false, error);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _state.SetHotkeyStatus(false, $"Das Tastenkürzel {label} ließ sich nicht anmelden: {ex.Message}");
        }
        _tray?.SetScanLabel($"Ausschnitt scannen\t{label}");
        _tray?.SetTooltip(_state.HotkeyError is null ? $"Lupe – {label} scannt einen Ausschnitt" : "Lupe – Tastenkürzel belegt");
    }

    private void OnSettingsChanged(LupeSettings old, LupeSettings now)
    {
        if (old.ScreenHotkey != now.ScreenHotkey) RegisterHotkey(now.ScreenHotkey);
    }

    private string HotkeyLabel() => _state is null ? "" : HotkeyLabel(_state.Settings.ScreenHotkey);

    private static string HotkeyLabel(Hotkey hotkey)
    {
        try { return HotkeyText.Format(hotkey); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return "Das Tastenkürzel"; }
    }

    // ================= Screenshots =================

    private async Task RunScreenshotsAsync()
    {
        int code;
        try
        {
            code = await Screenshots.RunAsync(_options.ScreenshotFolder, _options.Theme);
        }
        catch (Exception ex)
        {
            // Bewusst nicht verschluckt: Fehler (auch fehlende Core-Bausteine) sichtbar melden und mit 3 enden
            Console.Error.WriteLine($"Screenshots fehlgeschlagen: {ex}");
            code = CommandLine.ExitError;
        }
        Shutdown(code);
    }

    // ================= Fehler und Aufräumen =================

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Niemals wegen eines Fehlers abstürzen – stattdessen freundlich melden
        e.Handled = true;
        if (_options.Mode == AppMode.Screenshots)
        {
            Console.Error.WriteLine($"Screenshots fehlgeschlagen: {e.Exception}");
            Shutdown(CommandLine.ExitError);
            return;
        }
        _state?.Report(NoticeKind.Error, $"Unerwarteter Fehler: {e.Exception.Message}");
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

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }
}
