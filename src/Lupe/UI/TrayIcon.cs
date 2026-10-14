using System.Windows;
using System.Windows.Interop;
using Lupe.Interop;

namespace Lupe.UI;

/// <summary>
/// Symbol im Infobereich über Shell_NotifyIcon – ohne Windows Forms. Linksklick öffnet das Fenster, Rechtsklick
/// (oder Kontextmenütaste) zeigt das Menü: Ausschnitt scannen, Öffnen, Beenden. Startet Explorer neu, kommt das
/// Symbol wieder (TaskbarCreated).
/// </summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    private const int CallbackMessage = Native.WM_APP + 1;
    private const uint IconId = 1;
    private const uint CmdScan = 1, CmdOpen = 2, CmdExit = 3;

    private readonly HwndSource _window;
    private readonly uint _taskbarCreated;
    private readonly nint _icon;
    private string _tooltip;
    private string _scanLabel = "Ausschnitt scannen";
    private bool _disposed;

    public event Action? ScanRequested;
    public event Action? OpenRequested;
    public event Action? ExitRequested;

    /// <summary>Das Symbol steht im Infobereich (ohne Explorer, z. B. in einer Dienst-Sitzung, nicht).</summary>
    public bool IsVisible { get; private set; }

    public TrayIcon(string tooltip)
    {
        _tooltip = tooltip;
        // Unsichtbares Fenster der obersten Ebene (kein Nachrichtenfenster: das bekäme TaskbarCreated nicht)
        _window = new HwndSource(new HwndSourceParameters("LupeInfobereich") { Width = 0, Height = 0, WindowStyle = 0 });
        _window.AddHook(WndProc);
        _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        _icon = LoadIcon();
        IsVisible = Add();
    }

    /// <summary>Text im Menü für den Scan, z. B. mit Tastenkürzel.</summary>
    public void SetScanLabel(string label) => _scanLabel = label;

    public void SetTooltip(string text)
    {
        _tooltip = text;
        var data = Data(Native.NIF_TIP | Native.NIF_SHOWTIP);
        Native.Shell_NotifyIcon(Native.NIM_MODIFY, ref data);
    }

    /// <summary>Kurze Meldung am Symbol (unter Windows 11 als Benachrichtigung).</summary>
    public void ShowBalloon(string title, string text)
    {
        if (!IsVisible) return;
        var data = Data(Native.NIF_INFO);
        Copy(title, data.szInfoTitle, 64);
        Copy(text, data.szInfo, 256);
        data.dwInfoFlags = Native.NIIF_INFO | Native.NIIF_RESPECT_QUIET_TIME;
        Native.Shell_NotifyIcon(Native.NIM_MODIFY, ref data);
    }

    private bool Add()
    {
        var data = Data(Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP | Native.NIF_SHOWTIP);
        if (!Native.Shell_NotifyIcon(Native.NIM_ADD, ref data)) return false;
        data.uVersion = Native.NOTIFYICON_VERSION_4;
        Native.Shell_NotifyIcon(Native.NIM_SETVERSION, ref data);
        return true;
    }

    private Native.NOTIFYICONDATAW Data(uint flags)
    {
        var data = new Native.NOTIFYICONDATAW
        {
            cbSize = sizeof(Native.NOTIFYICONDATAW),
            hWnd = _window.Handle,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        Copy(_tooltip, data.szTip, 128);
        return data;
    }

    private static void Copy(string text, char* target, int size)
    {
        var span = new Span<char>(target, size);
        span.Clear();
        text.AsSpan(0, Math.Min(text.Length, size - 1)).CopyTo(span);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            // NOTIFYICON_VERSION_4: Ereignis im unteren Wort von lParam, Position in wParam
            int evt = (int)(lParam & 0xFFFF);
            switch (evt)
            {
                case Native.NIN_SELECT or Native.NIN_KEYSELECT or Native.WM_LBUTTONDBLCLK:
                    OpenRequested?.Invoke();
                    handled = true;
                    break;
                case Native.WM_CONTEXTMENU:
                    ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                    handled = true;
                    break;
            }
        }
        else if (_taskbarCreated != 0 && msg == (int)_taskbarCreated)
        {
            // Explorer neu gestartet: Symbol wieder anmelden
            IsVisible = Add();
        }
        return 0;
    }

    private void ShowMenu(int x, int y)
    {
        var menu = Native.CreatePopupMenu();
        if (menu == 0) return;
        try
        {
            Native.AppendMenu(menu, Native.MF_STRING, CmdScan, _scanLabel);
            Native.AppendMenu(menu, Native.MF_STRING, CmdOpen, "Lupe öffnen");
            Native.AppendMenu(menu, Native.MF_SEPARATOR, 0, null);
            Native.AppendMenu(menu, Native.MF_STRING, CmdExit, "Beenden");
            Native.SetMenuDefaultItem(menu, CmdOpen, 0);
            if (x == 0 && y == 0 && Native.GetCursorPos(out var p)) (x, y) = (p.X, p.Y);
            // Ohne Vordergrund schließt sich das Menü nicht, wenn man daneben klickt
            Native.SetForegroundWindow(_window.Handle);
            int command = Native.TrackPopupMenuEx(menu, Native.TPM_RETURNCMD | Native.TPM_RIGHTBUTTON | Native.TPM_NONOTIFY, x, y, _window.Handle, 0);
            Native.PostMessage(_window.Handle, Native.WM_NULL, 0, 0);
            switch ((uint)command)
            {
                case CmdScan: Application.Current?.Dispatcher.BeginInvoke(() => ScanRequested?.Invoke()); break;
                case CmdOpen: OpenRequested?.Invoke(); break;
                case CmdExit: Application.Current?.Dispatcher.BeginInvoke(() => ExitRequested?.Invoke()); break;
            }
        }
        finally
        {
            Native.DestroyMenu(menu);
        }
    }

    /// <summary>Symbol aus lupe.ico in der kleinen Symbolgröße des Systems (bei 150 % also 24 Pixel).</summary>
    private static nint LoadIcon()
    {
        try
        {
            var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Lupe;component/Assets/lupe.ico"))?.Stream;
            if (stream is null) return 0;
            using (stream)
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return IconFromIco(ms.ToArray(), Native.GetSystemMetrics(Native.SM_CXSMICON));
            }
        }
        catch (Exception ex) when (ex is IOException or UriFormatException or InvalidOperationException)
        {
            return 0;
        }
    }

    /// <summary>Wählt aus der ICO-Datei das Bild, das der gewünschten Größe am nächsten kommt (lieber größer).</summary>
    internal static nint IconFromIco(byte[] ico, int size)
    {
        if (ico.Length < 6 || BitConverter.ToUInt16(ico, 2) != 1) return 0;
        int count = BitConverter.ToUInt16(ico, 4);
        int best = -1, bestScore = int.MaxValue;
        for (int i = 0; i < count; i++)
        {
            int entry = 6 + i * 16;
            if (entry + 16 > ico.Length) break;
            int w = ico[entry] == 0 ? 256 : ico[entry];
            int score = w >= size ? w - size : (size - w) * 4;
            if (score < bestScore) { bestScore = score; best = entry; }
        }
        if (best < 0) return 0;
        int length = BitConverter.ToInt32(ico, best + 8), offset = BitConverter.ToInt32(ico, best + 12);
        if (offset < 0 || length <= 0 || offset + length > ico.Length) return 0;
        fixed (byte* p = &ico[offset])
            return Native.CreateIconFromResourceEx(p, (uint)length, true, 0x00030000, size, size, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var data = Data(0);
        Native.Shell_NotifyIcon(Native.NIM_DELETE, ref data);
        IsVisible = false;
        if (_icon != 0) Native.DestroyIcon(_icon);
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }
}
