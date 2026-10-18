using Lupe.Core.Platform;
using Lupe.Core.Resources;
using Lupe.Platform.Linux.Native;

namespace Lupe.Platform.Linux.Input;

/// <summary>
/// Das Vordergrundfenster unter X11: <c>_NET_ACTIVE_WINDOW</c> des Fenstermanagers (ohne Fenstermanager der Fokus),
/// nach vorn holen über die EWMH-Nachricht <c>_NET_ACTIVE_WINDOW</c>. Unter Wayland verrät der Desktop das nicht –
/// dort tippt Lupe einfach in das Fenster, das nach dem Schließen der Auswahl den Fokus bekommt.
/// </summary>
public sealed class LinuxActiveWindow : IActiveWindow
{
    private readonly Lock _gate = new();
    private nint _display;
    private bool _failed;

    public bool IsSupported => LinuxSession.Kind == SessionKind.X11 && Open();

    public nint Foreground()
    {
        lock (_gate)
        {
            if (LinuxSession.Kind != SessionKind.X11 || !Open()) return 0;
            return ForegroundCore();
        }
    }

    private nint ForegroundCore()
    {
        var active = X11.GetLongProperty(_display, X11.XDefaultRootWindow(_display), "_NET_ACTIVE_WINDOW", X11.XA_WINDOW);
        if (active.Length > 0 && active[0] != 0) return active[0];
        X11.XGetInputFocus(_display, out nint focus, out _);
        return focus is 0 or 1 ? 0 : focus; // None = 0, PointerRoot = 1
    }

    public bool IsOwnWindow(nint window)
    {
        if (window == 0) return false;
        lock (_gate)
        {
            if (!Open()) return false;
            var pid = X11.GetLongProperty(_display, window, "_NET_WM_PID", X11.XA_CARDINAL);
            return pid.Length > 0 && pid[0] == Environment.ProcessId;
        }
    }

    public async Task<string?> ActivateAsync(nint window, CancellationToken ct = default)
    {
        if (LinuxSession.Kind == SessionKind.Wayland)
            return null; // nicht möglich und nicht nötig: der Fokus geht nach der Auswahl von selbst zurück
        if (window == 0) return Strings.Linux_ActiveWindow_None;
        lock (_gate)
        {
            if (!Open()) return Strings.Linux_ActiveWindow_NoXServer;
            if (ForegroundCore() == window) return null;
            int error = RequestActivation(_display, window);
            if (error == 3) return Strings.Linux_ActiveWindow_Gone; // BadWindow
        }
        for (int i = 0; i < 25; i++)
        {
            await Task.Delay(20, ct).ConfigureAwait(false);
            lock (_gate)
                if (ForegroundCore() == window) return null;
        }
        return Strings.Linux_ActiveWindow_ActivateFailed;
    }

    /// <summary>Bittet den Fenstermanager per EWMH, das Fenster zu aktivieren; liefert den X-Fehlercode (0 = keiner).</summary>
    private static unsafe int RequestActivation(nint display, nint window)
    {
        nint root = X11.XDefaultRootWindow(display);
        bool hasWindowManager = X11.GetLongProperty(display, root, "_NET_SUPPORTING_WM_CHECK", X11.XA_WINDOW).Length > 0;
        nint atom = X11.XInternAtom(display, "_NET_ACTIVE_WINDOW", 0);
        return X11Errors.Trap(display, () => Send(display, root, window, atom, hasWindowManager));
    }

    private static unsafe void Send(nint display, nint root, nint window, nint atom, bool hasWindowManager)
    {
        var ev = new XEvent();
        ev.client.type = X11.ClientMessage;
        ev.client.send_event = 1;
        ev.client.window = window;
        ev.client.message_type = atom;
        ev.client.format = 32;
        ev.client.l0 = 2; // Quelle: Pager/Werkzeug – Fenstermanager folgen dem ohne Fokusdiebstahl-Schutz
        ev.client.l1 = X11.CurrentTime;
        X11.XSendEvent(display, root, 0, X11.SubstructureRedirectMask | X11.SubstructureNotifyMask, &ev);
        X11.XRaiseWindow(display, window);
        // Ohne Fenstermanager den Fokus direkt setzen
        if (!hasWindowManager) X11.XSetInputFocus(display, window, X11.RevertToParent, X11.CurrentTime);
    }

    private bool Open()
    {
        if (_display != 0) return true;
        if (_failed) return false;
        _display = X11.TryOpen(out _);
        _failed = _display == 0;
        return !_failed;
    }
}
