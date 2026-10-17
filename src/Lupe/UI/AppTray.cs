using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Lupe.UI;

/// <summary>
/// Symbol im Infobereich (Windows), in der Menüleiste (macOS) bzw. als StatusNotifierItem (Linux) über Avalonias
/// <see cref="TrayIcon"/>. Klick öffnet das Fenster, das Menü bietet: Ausschnitt scannen, Lupe öffnen, Beenden.
/// </summary>
internal sealed class AppTray : IDisposable
{
    private readonly Application _app;
    private readonly TrayIcon _icon;
    private readonly NativeMenuItem _scan;
    private bool _disposed;

    public event Action? ScanRequested;
    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public AppTray(Application app, string tooltip)
    {
        _app = app;
        _scan = new NativeMenuItem("Ausschnitt scannen");
        _scan.Click += (_, _) => Dispatcher.UIThread.Post(() => ScanRequested?.Invoke());
        var open = new NativeMenuItem("Lupe öffnen");
        open.Click += (_, _) => Dispatcher.UIThread.Post(() => OpenRequested?.Invoke());
        var exit = new NativeMenuItem("Beenden");
        exit.Click += (_, _) => Dispatcher.UIThread.Post(() => ExitRequested?.Invoke());

        _icon = new TrayIcon
        {
            ToolTipText = tooltip,
            Menu = new NativeMenu { Items = { _scan, open, new NativeMenuItemSeparator(), exit } },
            IsVisible = true,
        };
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Lupe/Assets/lupe.ico"));
            _icon.Icon = new WindowIcon(stream);
        }
        catch (Exception ex) when (ex is IOException or FileNotFoundException or InvalidOperationException)
        {
            // Ohne Symbol bleibt das Menü trotzdem erreichbar
        }
        _icon.Clicked += (_, _) => OpenRequested?.Invoke();
        TrayIcon.SetIcons(app, [_icon]);
        IsVisible = Available();
    }

    /// <summary>
    /// Das Symbol ist zu sehen. Windows und macOS haben immer einen Infobereich bzw. eine Menüleiste; unter Linux braucht
    /// es eine Sitzung mit D-Bus (StatusNotifierItem) – GNOME zeigt es erst mit der Erweiterung „AppIndicator“.
    /// </summary>
    public bool IsVisible { get; }

    private static bool Available()
    {
        if (!OperatingSystem.IsLinux()) return true;
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"))
               && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"));
    }

    /// <summary>Text im Menü für den Scan, z. B. mit Tastenkürzel.</summary>
    public void SetScanLabel(string label) => _scan.Header = label;

    public void SetTooltip(string text) => _icon.ToolTipText = text;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.IsVisible = false;
        TrayIcon.SetIcons(_app, []);
        _icon.Dispose();
    }
}
