// SPDX-License-Identifier: MIT
using Lupe.Core.Input;
using Lupe.Core.Platform;
using Lupe.Core.Resources;
using Lupe.Platform.Linux.Native;

namespace Lupe.Platform.Linux.Input;

/// <summary>Tippen unter Linux: X11 über XTest, Wayland über das RemoteDesktop-Portal (siehe <see cref="X11Typer"/>, <see cref="PortalTyper"/>).</summary>
public sealed class LinuxKeyboard : IKeyboardOutput, IDisposable
{
    internal static string NoSession => Strings.Linux_Typing_NoSession;

    private readonly Lazy<X11Typer> _x11 = new(() => new X11Typer());
    private readonly Lazy<PortalTyper> _portal = new(() => new PortalTyper());

    public int Send(ReadOnlySpan<KeyEvent> events, out int errorCode)
    {
        switch (LinuxSession.Kind)
        {
            case SessionKind.X11: return _x11.Value.Send(events, out errorCode);
            case SessionKind.Wayland: return _portal.Value.Send(events, out errorCode);
            default:
                errorCode = -1;
                return 0;
        }
    }

    public bool IsDown(int virtualKey) =>
        // Unter Wayland verrät der Desktop gedrückte Tasten nicht; XWayland sieht sie nur bei eigenem Fokus
        LinuxSession.Kind == SessionKind.X11 && _x11.Value.IsDown(virtualKey);

    public void Pause(int milliseconds, CancellationToken ct)
    {
        if (milliseconds > 0) ct.WaitHandle.WaitOne(milliseconds);
    }

    public string? ForegroundProblem() => LinuxSession.Kind switch
    {
        SessionKind.X11 => _x11.Value.Problem(),
        SessionKind.Wayland => _portal.Value.Problem(),
        _ => NoSession,
    };

    public string DescribeRejection(int errorCode) => LinuxSession.Kind switch
    {
        SessionKind.Wayland => _portal.Value.DescribeRejection(),
        SessionKind.X11 => errorCode switch
        {
            X11Typer.ErrorNoDisplay => Strings.Linux_Typing_NoXServer,
            X11Typer.ErrorNoXTest => Strings.Linux_Typing_NoXTest,
            X11Typer.ErrorNoSpareKey => Strings.Linux_Typing_NoSpareKey,
            _ => Strings.Linux_Typing_Rejected,
        },
        _ => NoSession,
    };

    public void Dispose()
    {
        if (_x11.IsValueCreated) _x11.Value.Dispose();
        if (_portal.IsValueCreated) _portal.Value.Dispose();
    }
}

/// <summary>
/// Was das X11-Tastaturlayout auf welche Taste legt (XKB, Gruppe 0). Buchstaben und Ziffern behalten ihre
/// Virtual-Key-Codes; OEM-Codes stehen für die Lage der Taste (siehe <see cref="Keysyms.OemKeycodes"/>). Ohne X-Server
/// (reines Wayland) unbekannt.
/// </summary>
public sealed unsafe class LinuxKeyboardLayout : IKeyboardLayout
{
    private readonly Lock _gate = new();
    private nint _display;
    private bool _failed;

    public char? KeyChar(int virtualKey)
    {
        lock (_gate)
        {
            if (Keycode(virtualKey) is not { } kc) return null;
            return Keysyms.ToChar((uint)X11.XkbKeycodeToKeysym(_display, kc, 0, 0));
        }
    }

    public int? KeyForChar(char c)
    {
        if (c is >= 'a' and <= 'z') return c - 0x20;
        if (c is >= 'A' and <= 'Z') return c;
        if (c is >= '0' and <= '9') return c;
        lock (_gate)
        {
            if (!Open()) return null;
            uint keysym = Keysyms.FromCodePoint(c);
            if (keysym == Keysyms.NoSymbol) return null;
            foreach (var (vk, kc) in Keysyms.OemKeycodes)
                if ((uint)X11.XkbKeycodeToKeysym(_display, kc, 0, 0) == keysym) return vk;
            // Tottasten (z. B. ^ und ´ im deutschen Layout) schreiben ihr Zeichen über die Dead-Keysym
            foreach (var (vk, kc) in Keysyms.OemKeycodes)
                if (Keysyms.ToChar((uint)X11.XkbKeycodeToKeysym(_display, kc, 0, 0)) == c) return vk;
            return null;
        }
    }

    /// <summary>Unter X11 ist AltGr eine eigene Taste (ISO_Level3_Shift), nicht Strg+Alt.</summary>
    public bool AltGrIsCtrlAlt => false;

    public string? AltGrText(int virtualKey, bool shift)
    {
        lock (_gate)
        {
            if (Keycode(virtualKey) is not { } kc) return null;
            return Keysyms.ToChar((uint)X11.XkbKeycodeToKeysym(_display, kc, 0, shift ? 3 : 2))?.ToString();
        }
    }

    private byte? Keycode(int vk)
    {
        if (!Open()) return null;
        if (Keysyms.OemKeycodes.TryGetValue(vk, out byte oem)) return oem;
        uint keysym = Keysyms.FromVirtualKey(vk);
        if (keysym == Keysyms.NoSymbol) return null;
        byte kc = X11.XKeysymToKeycode(_display, keysym);
        return kc == 0 ? null : kc;
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
