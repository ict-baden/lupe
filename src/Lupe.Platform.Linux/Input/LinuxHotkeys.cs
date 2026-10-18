using System.Diagnostics;
using System.Runtime.InteropServices;
using Lupe.Core.Input;
using Lupe.Core.Model;
using Lupe.Core.Platform;
using Lupe.Core.Resources;
using Lupe.Platform.Linux.Native;
using Lupe.Platform.Linux.Portal;
using Tmds.DBus.Protocol;

namespace Lupe.Platform.Linux.Input;

/// <summary>Globale Tastenkürzel: X11 über <c>XGrabKey</c>, Wayland über das GlobalShortcuts-Portal (KDE, GNOME ab 48 …).</summary>
public sealed class LinuxHotkeys : IGlobalHotkeyProvider
{
    internal static string WaylandUnsupported => Strings.Linux_Hotkey_WaylandUnsupported;
    internal static string NoSession => Strings.Linux_Hotkey_NoSession;

    private readonly Lazy<string?> _waylandReason = new(() =>
    {
        try
        {
            return Task.Run(async () =>
            {
                using var portal = await PortalConnection.ConnectAsync().ConfigureAwait(false);
                return await portal.GetVersionAsync(PortalHotkey.Iface).ConfigureAwait(false) > 0 ? null : WaylandUnsupported;
            }).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is PortalException or DBusExceptionBase or IOException)
        {
            return WaylandUnsupported;
        }
    });

    public string? UnsupportedReason => LinuxSession.Kind switch
    {
        SessionKind.X11 => null,
        SessionKind.Wayland => _waylandReason.Value,
        _ => NoSession,
    };

    public IGlobalHotkey Register(Hotkey hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        if (HotkeyText.Problem(hotkey) is { } problem) return new FailedHotkey(problem);
        return LinuxSession.Kind switch
        {
            SessionKind.X11 => new X11Hotkey(hotkey),
            SessionKind.Wayland when UnsupportedReason is { } reason => new FailedHotkey(reason),
            SessionKind.Wayland => new PortalHotkey(hotkey),
            _ => new FailedHotkey(NoSession),
        };
    }

    /// <summary>X11-Modifikatoren des Kürzels.</summary>
    internal static uint Modifiers(Hotkey h) =>
        (h.Shift ? X11.ShiftMask : 0) | (h.Ctrl ? X11.ControlMask : 0) | (h.Alt ? X11.Mod1Mask : 0) | (h.Win ? X11.Mod4Mask : 0);

    /// <summary>Feststell- und Num-Taste dürfen das Kürzel nicht verhindern: jede Kombination mit anmelden.</summary>
    internal static readonly uint[] IgnoredVariants = [0, X11.LockMask, X11.Mod2Mask, X11.LockMask | X11.Mod2Mask];

    /// <summary>Tastencode der Virtual-Key im aktuellen Layout (OEM nach Lage), 0 wenn unbekannt.</summary>
    internal static byte KeycodeFor(nint display, int vk)
    {
        if (Keysyms.OemKeycodes.TryGetValue(vk, out byte oem)) return oem;
        uint keysym = Keysyms.FromVirtualKey(vk);
        return keysym == Keysyms.NoSymbol ? (byte)0 : X11.XKeysymToKeycode(display, keysym);
    }
}

/// <summary>
/// Ein Kürzel über <c>XGrabKey</c> auf dem Wurzelfenster, mit eigener X-Verbindung und eigenem Thread, der auf die
/// Tastendrücke wartet. Gedrückt halten löst nur einmal aus.
/// </summary>
internal sealed unsafe class X11Hotkey : IGlobalHotkey
{
    private readonly Hotkey _hotkey;
    private readonly Thread? _thread;
    private readonly TaskCompletionSource<string?> _outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _stop;
    private int _wakeRead = -1, _wakeWrite = -1;

    public X11Hotkey(Hotkey hotkey)
    {
        _hotkey = hotkey;
        _thread = new Thread(Run) { IsBackground = true, Name = "Lupe X11-Tastenkürzel" };
        _thread.Start();
        Error = _outcome.Task.GetAwaiter().GetResult();
        IsRegistered = Error is null;
    }

    public event EventHandler? Pressed;
    public bool IsRegistered { get; }
    public string? Error { get; }

    private void Run()
    {
        nint display = X11.TryOpen(out string? openError);
        if (display == 0)
        {
            _outcome.TrySetResult(string.Format(Strings.Linux_Hotkey_RegisterFailedReason, HotkeyText.Format(_hotkey), openError));
            return;
        }
        nint root = X11.XDefaultRootWindow(display);
        byte keycode = LinuxHotkeys.KeycodeFor(display, _hotkey.Key);
        uint mods = LinuxHotkeys.Modifiers(_hotkey);
        try
        {
            if (keycode == 0)
            {
                _outcome.TrySetResult(string.Format(Strings.Linux_Hotkey_KeyNotInLayout, HotkeyText.Format(_hotkey)));
                return;
            }
            int error = X11Errors.Trap(display, () =>
            {
                foreach (uint extra in LinuxHotkeys.IgnoredVariants)
                    X11.XGrabKey(display, keycode, mods | extra, root, 0, X11.GrabModeAsync, X11.GrabModeAsync);
            });
            if (error != 0)
            {
                X11Errors.Trap(display, () =>
                {
                    foreach (uint extra in LinuxHotkeys.IgnoredVariants) X11.XUngrabKey(display, keycode, mods | extra, root);
                });
                _outcome.TrySetResult(error == X11.BadAccess
                    ? string.Format(Strings.Linux_Hotkey_AlreadyRegistered, HotkeyText.Format(_hotkey))
                    : string.Format(Strings.Linux_Hotkey_RegisterFailedX11, HotkeyText.Format(_hotkey), error));
                return;
            }
            int* fds = stackalloc int[2];
            if (Libc.pipe(fds) == 0)
            {
                _wakeRead = fds[0];
                _wakeWrite = fds[1];
            }
            _outcome.TrySetResult(null);
            Loop(display, keycode, mods);
        }
        finally
        {
            if (keycode != 0)
                X11Errors.Trap(display, () =>
                {
                    foreach (uint extra in LinuxHotkeys.IgnoredVariants) X11.XUngrabKey(display, keycode, mods | extra, root);
                });
            X11.XCloseDisplay(display);
            if (_wakeRead >= 0) Libc.close(_wakeRead);
            _outcome.TrySetResult(Strings.Linux_Hotkey_Unregistered);
        }
    }

    private void Loop(nint display, byte keycode, uint mods)
    {
        int fd = X11.XConnectionNumber(display);
        bool down = false;
        var poll = stackalloc Libc.PollFd[2];
        XEvent ev;
        while (!_stop)
        {
            while (X11.XPending(display) > 0)
            {
                X11.XNextEvent(display, &ev);
                if (ev.type is not (X11.KeyPress or X11.KeyRelease) || ev.key.keycode != keycode) continue;
                uint state = ev.key.state & (X11.ShiftMask | X11.ControlMask | X11.Mod1Mask | X11.Mod4Mask);
                if (ev.type == X11.KeyRelease)
                {
                    down = false;
                    continue;
                }
                if (state != mods || down) continue; // gehalten: Wiederholungen ignorieren
                down = true;
                Raise();
            }
            poll[0] = new Libc.PollFd { fd = fd, events = Libc.POLLIN };
            poll[1] = new Libc.PollFd { fd = _wakeRead, events = Libc.POLLIN };
            Libc.poll(poll, _wakeRead >= 0 ? 2u : 1u, 200);
        }
    }

    private void Raise()
    {
        var handler = Pressed;
        if (handler is null) return;
        foreach (EventHandler single in handler.GetInvocationList())
        {
            try { single(this, EventArgs.Empty); }
            catch (Exception ex) { Trace.TraceError($"Lupe: Fehler im Empfänger des Tastenkürzels: {ex}"); }
        }
    }

    public void Dispose()
    {
        if (_stop) return;
        _stop = true;
        if (_wakeWrite >= 0)
        {
            byte b = 1;
            Libc.write(_wakeWrite, &b, 1);
        }
        if (_thread is not null && _thread != Thread.CurrentThread) _thread.Join(TimeSpan.FromSeconds(2));
        if (_wakeWrite >= 0) Libc.close(_wakeWrite);
        _wakeWrite = -1;
    }
}

/// <summary>Ein Kürzel über das GlobalShortcuts-Portal. Welche Tasten es am Ende sind, entscheidet der Desktop (Vorschlag: das eingestellte Kürzel).</summary>
internal sealed class PortalHotkey : IGlobalHotkey
{
    internal const string Iface = "org.freedesktop.portal.GlobalShortcuts";
    internal const string ShortcutId = "bildschirm-scannen";

    private PortalConnection? _portal;
    private IDisposable? _watch;
    private string? _session;
    private int _disposed;

    public PortalHotkey(Hotkey hotkey)
    {
        try
        {
            Task.Run(() => BindAsync(hotkey)).GetAwaiter().GetResult();
            IsRegistered = true;
        }
        catch (Exception ex) when (ex is PortalException or DBusExceptionBase or IOException)
        {
            Error = ex is PortalException ? ex.Message : string.Format(Strings.Linux_Hotkey_PortalRegisterFailed, ex.Message.Trim());
            Dispose();
        }
    }

    public event EventHandler? Pressed;
    public bool IsRegistered { get; }
    public string? Error { get; }

    private async Task BindAsync(Hotkey hotkey)
    {
        _portal = await PortalConnection.ConnectAsync().ConfigureAwait(false);
        var created = await _portal.RequestAsync(Iface, "CreateSession", "a{sv}", null,
            new Dictionary<string, VariantValue> { ["session_handle_token"] = VariantValue.String(PortalConnection.NewToken()) },
            TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        _session = created.GetString("session_handle")
            ?? throw new PortalException(Strings.Linux_Hotkey_NoPortalSession);

        _watch = await _portal.Connection.WatchSignalAsync(PortalConnection.Bus, PortalConnection.ObjectPath, Iface, "Activated",
            (Message m, object? _) =>
            {
                var r = m.GetBodyReader();
                return (Session: r.ReadObjectPathAsString(), Id: r.ReadString());
            },
            (Notification<(string Session, string Id)> n) =>
            {
                if (n.HasValue && n.Value.Session == _session && n.Value.Id == ShortcutId && Volatile.Read(ref _disposed) == 0) Raise();
            }, ObserverFlags.None, emitOnCapturedContext: false, null).ConfigureAwait(false);

        var props = new Dictionary<string, VariantValue> { ["description"] = VariantValue.String(Strings.Linux_Hotkey_Description) };
        if (PreferredTrigger(hotkey) is { } trigger) props["preferred_trigger"] = VariantValue.String(trigger);
        var bound = await _portal.RequestAsync(Iface, "BindShortcuts", "oa(sa{sv})sa{sv}", (ref MessageWriter w) =>
        {
            w.WriteObjectPath(_session);
            var array = w.WriteArrayStart(DBusType.Struct);
            w.WriteStructureStart();
            w.WriteString(ShortcutId);
            w.WriteDictionary(props);
            w.WriteArrayEnd(array);
            w.WriteString("");
        }, [], TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        if (!bound.Ok)
            throw new PortalException(bound.Code == 1
                ? Strings.Linux_Hotkey_Rejected
                : Strings.Linux_Hotkey_NotAccepted);
    }

    /// <summary>Vorschlag im Format der XDG-Spezifikation „shortcuts“, z. B. „LOGO+SHIFT+q“; null, wenn die Taste keinen Namen hat.</summary>
    internal static string? PreferredTrigger(Hotkey hotkey)
    {
        uint keysym = Keysyms.FromVirtualKey(hotkey.Key);
        string? key = keysym == Keysyms.NoSymbol ? null : KeysymName(keysym);
        if (key is null) return null;
        var parts = new List<string>();
        if (hotkey.Ctrl) parts.Add("CTRL");
        if (hotkey.Alt) parts.Add("ALT");
        if (hotkey.Shift) parts.Add("SHIFT");
        if (hotkey.Win) parts.Add("LOGO");
        parts.Add(key);
        return string.Join('+', parts);
    }

    private static string? KeysymName(uint keysym)
    {
        if (keysym is >= 'a' and <= 'z' or >= '0' and <= '9') return ((char)keysym).ToString();
        if (keysym is >= 0xFFBE and <= 0xFFD5) return $"F{keysym - 0xFFBE + 1}";
        return keysym switch
        {
            0x20 => "space", 0xFF61 => "Print", 0xFF13 => "Pause", 0xFF14 => "Scroll_Lock", 0xFF0D => "Return", 0xFF09 => "Tab",
            0xFF1B => "Escape", 0xFF08 => "BackSpace", 0xFF50 => "Home", 0xFF57 => "End", 0xFF55 => "Prior", 0xFF56 => "Next",
            0xFF63 => "Insert", 0xFFFF => "Delete", 0xFF51 => "Left", 0xFF52 => "Up", 0xFF53 => "Right", 0xFF54 => "Down",
            _ => null,
        };
    }

    private void Raise()
    {
        var handler = Pressed;
        if (handler is null) return;
        foreach (EventHandler single in handler.GetInvocationList())
        {
            try { single(this, EventArgs.Empty); }
            catch (Exception ex) { Trace.TraceError($"Lupe: Fehler im Empfänger des Tastenkürzels: {ex}"); }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _watch?.Dispose();
        if (_session is not null && _portal is not null)
            try { _portal.CloseSessionAsync(_session).Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        _portal?.Dispose();
    }
}

internal static unsafe class Libc
{
    public const short POLLIN = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int fd;
        public short events, revents;
    }

    [DllImport("libc", SetLastError = true)] public static extern int poll(PollFd* fds, uint count, int timeout);
    [DllImport("libc", SetLastError = true)] public static extern int pipe(int* fds);
    [DllImport("libc", SetLastError = true)] public static extern nint write(int fd, void* buf, nint count);
    [DllImport("libc", SetLastError = true)] public static extern int close(int fd);
}
