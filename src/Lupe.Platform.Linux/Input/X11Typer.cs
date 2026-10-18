using Lupe.Core.Input;
using Lupe.Core.Platform;
using Lupe.Core.Resources;
using Lupe.Platform.Linux.Native;

namespace Lupe.Platform.Linux.Input;

/// <summary>
/// Tippen unter X11 über XTest, unabhängig vom Tastaturlayout: Zeichen, die das Layout auf einer Taste hat (ohne oder mit
/// Umschalt), gehen über diese Taste; alle anderen über einen freien Tastencode, dem kurz die passende Keysym zugewiesen
/// wird (wie xdotool). Mehrere freie Tastencodes wechseln sich ab, damit ein Zielprogramm, das die Tastenbelegung erst
/// beim Verarbeiten nachliest, nicht schon die Belegung des nächsten Zeichens sieht. Die Belegungen werden kurz nach dem
/// Tippen wieder entfernt.
/// </summary>
internal sealed unsafe class X11Typer : IDisposable
{
    public const int ErrorNoDisplay = 1, ErrorNoXTest = 2, ErrorNoSpareKey = 3;

    /// <summary>So lange nach dem letzten Tippen bleiben die vorübergehenden Belegungen bestehen.</summary>
    internal static TimeSpan RestoreDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Mindestabstand zwischen zwei Änderungen der Tastenbelegung.</summary>
    internal static TimeSpan RemapGap { get; set; } = TimeSpan.FromMilliseconds(25);

    private readonly Lock _gate = new();
    private nint _display;
    private string? _openError;
    private bool _hasXTest;
    private int _minKeycode, _maxKeycode, _perKeycode;
    private nuint[] _map = [];
    private readonly List<byte> _spare = [];
    private readonly Dictionary<byte, uint> _assigned = [];  // vorübergehend belegte Tastencodes
    private int _nextSpare;
    private long _lastRemap;
    private Timer? _restoreTimer;
    private long _lastSend;

    /// <summary>Warum gerade nicht getippt werden kann (deutsch), sonst null.</summary>
    public string? Problem()
    {
        lock (_gate)
        {
            if (!Open()) return string.Format(Strings.Linux_Typing_Failed, _openError);
            if (!_hasXTest) return Strings.Linux_Typing_NoXTest;
            return null;
        }
    }

    public int Send(ReadOnlySpan<KeyEvent> events, out int errorCode)
    {
        lock (_gate)
        {
            errorCode = 0;
            if (!Open())
            {
                errorCode = ErrorNoDisplay;
                return 0;
            }
            if (!_hasXTest)
            {
                errorCode = ErrorNoXTest;
                return 0;
            }
            LoadMapping();
            byte shift = X11.XKeysymToKeycode(_display, Keysyms.ShiftL);
            Premap(events, shift);
            int accepted = 0;
            for (int i = 0; i < events.Length; i++)
            {
                var e = events[i];
                bool up = (e.Flags & KeyFlags.Up) != 0;
                int consumed = 1;
                uint keysym;
                if ((e.Flags & KeyFlags.Unicode) != 0)
                {
                    char c = (char)e.Scan;
                    int cp = c;
                    if (char.IsHighSurrogate(c) && i + 1 < events.Length && (events[i + 1].Flags & KeyFlags.Unicode) != 0
                        && char.IsLowSurrogate((char)events[i + 1].Scan) && (events[i + 1].Flags & KeyFlags.Up) == (e.Flags & KeyFlags.Up))
                    {
                        cp = Keysyms.JoinSurrogates(c, (char)events[i + 1].Scan);
                        consumed = 2;
                    }
                    keysym = Keysyms.FromCodePoint(cp);
                }
                else if (e.VirtualKey == KeyboardTyper.VK_MASK)
                {
                    accepted++; // nur gegen das Windows-Startmenü nötig
                    continue;
                }
                else keysym = Keysyms.FromVirtualKey(e.VirtualKey);

                if (keysym == Keysyms.NoSymbol)
                {
                    accepted += consumed; // nicht tippbar – auslassen wie Steuerzeichen
                    i += consumed - 1;
                    continue;
                }
                if (!Press(keysym, up, shift))
                {
                    errorCode = ErrorNoSpareKey;
                    X11.XFlush(_display);
                    return accepted;
                }
                accepted += consumed;
                i += consumed - 1;
            }
            X11.XSync(_display, 0);
            ScheduleRestore();
            return accepted;
        }
    }

    public bool IsDown(int virtualKey)
    {
        lock (_gate)
        {
            if (!Open()) return false;
            byte* keys = stackalloc byte[32];
            X11.XQueryKeymap(_display, keys);
            foreach (byte kc in KeycodesFor(virtualKey))
                if (kc != 0 && (keys[kc >> 3] & (1 << (kc & 7))) != 0) return true;
            return false;
        }
    }

    private IEnumerable<byte> KeycodesFor(int vk)
    {
        if (Keysyms.OemKeycodes.TryGetValue(vk, out byte oem)) return [oem];
        return vk switch
        {
            0x10 => [X11.XKeysymToKeycode(_display, Keysyms.ShiftL), X11.XKeysymToKeycode(_display, Keysyms.ShiftR)],
            0x11 => [X11.XKeysymToKeycode(_display, Keysyms.ControlL), X11.XKeysymToKeycode(_display, Keysyms.ControlR)],
            0x12 => [X11.XKeysymToKeycode(_display, Keysyms.AltL), X11.XKeysymToKeycode(_display, Keysyms.AltR),
                X11.XKeysymToKeycode(_display, Keysyms.IsoLevel3Shift)],
            0xA5 => [X11.XKeysymToKeycode(_display, Keysyms.AltR), X11.XKeysymToKeycode(_display, Keysyms.IsoLevel3Shift)],
            _ => Keysyms.FromVirtualKey(vk) is var ks and not Keysyms.NoSymbol ? [X11.XKeysymToKeycode(_display, ks)] : [],
        };
    }

    /// <summary>Drückt bzw. lässt die Taste für die Keysym los (über das Layout oder einen frei belegten Tastencode).</summary>
    private bool Press(uint keysym, bool up, byte shift)
    {
        // Schon vorübergehend belegt?
        foreach (var (kc, ks) in _assigned)
            if (ks == keysym) return Fake(kc, up);

        var (keycode, level) = FindInLayout(keysym);
        if (keycode != 0 && (level == 0 || shift != 0 && level == 1))
        {
            if (level == 1 && !up) X11.XTestFakeKeyEvent(_display, shift, 1, 0);
            Fake(keycode, up);
            if (level == 1 && up) X11.XTestFakeKeyEvent(_display, shift, 0, 0);
            return true;
        }
        if (up)
        {
            // Loslassen einer Taste, die gar nicht gedrückt wurde (z. B. Belegung schon entfernt) – nichts zu tun
            return true;
        }
        if (_spare.Count == 0) return false;

        byte spare = _spare[_nextSpare];
        _nextSpare = (_nextSpare + 1) % _spare.Count;
        if (_assigned.ContainsKey(spare))
        {
            // Alle freien Codes reihum benutzt: dem Ziel kurz Zeit geben, bevor eine Belegung wechselt
            X11.XSync(_display, 0);
            Thread.Sleep(30);
        }
        // Xlib-Programme lesen die geänderte Belegung erst beim Verarbeiten nach und verpassen Änderungen, die zu dicht
        // aufeinander folgen (dann kommt beim Ziel gar kein Zeichen an) – zwischen zwei Belegungen kurz warten.
        long wait = _lastRemap + (long)RemapGap.TotalMilliseconds - Environment.TickCount64;
        if (wait > 0) Thread.Sleep((int)wait);
        nuint* syms = stackalloc nuint[2];
        syms[0] = keysym;
        syms[1] = keysym;
        X11.XChangeKeyboardMapping(_display, spare, 2, syms, 1);
        X11.XSync(_display, 0);
        _lastRemap = Environment.TickCount64;
        _assigned[spare] = keysym;
        return Fake(spare, up);
    }

    /// <summary>
    /// Belegt vorab alle Zeichen des Stapels, die das Layout nicht hat (so viele freie Tastencodes es gibt), und wartet
    /// danach einmal. Xlib-Programme lesen eine geänderte Belegung erst beim Verarbeiten nach; wechselt sie zwischen den
    /// Tastendrücken, kam auf einem langsamen Rechner (CI unter Xvfb) für einige Tasten keine Keysym an.
    /// </summary>
    private void Premap(ReadOnlySpan<KeyEvent> events, byte shift)
    {
        bool changed = false;
        nuint* syms = stackalloc nuint[2];
        for (int i = 0; i < events.Length; i++)
        {
            var e = events[i];
            if ((e.Flags & KeyFlags.Unicode) == 0 || (e.Flags & KeyFlags.Up) != 0) continue;
            char c = (char)e.Scan;
            int cp = c;
            if (char.IsHighSurrogate(c) && i + 1 < events.Length && char.IsLowSurrogate((char)events[i + 1].Scan))
            {
                cp = Keysyms.JoinSurrogates(c, (char)events[++i].Scan);
            }
            uint keysym = Keysyms.FromCodePoint(cp);
            if (keysym == Keysyms.NoSymbol || _assigned.ContainsValue(keysym)) continue;
            var (keycode, level) = FindInLayout(keysym);
            if (keycode != 0 && (level == 0 || shift != 0 && level == 1)) continue;
            byte free = _spare.FirstOrDefault(kc => !_assigned.ContainsKey(kc));
            if (free == 0) break; // alle belegt – den Rest wechselt Press reihum
            syms[0] = keysym;
            syms[1] = keysym;
            X11.XChangeKeyboardMapping(_display, free, 2, syms, 1);
            _assigned[free] = keysym;
            changed = true;
        }
        if (!changed) return;
        X11.XSync(_display, 0);
        Thread.Sleep(RemapGap);
        _lastRemap = Environment.TickCount64;
    }

    private bool Fake(byte keycode, bool up) => X11.XTestFakeKeyEvent(_display, keycode, up ? 0 : 1, 0) != 0;

    /// <summary>Tastencode und Ebene (0 = ohne, 1 = mit Umschalt) der Keysym in der Grundgruppe des Layouts.</summary>
    private (byte Keycode, int Level) FindInLayout(uint keysym)
    {
        int count = _maxKeycode - _minKeycode + 1;
        for (int level = 0; level < Math.Min(2, _perKeycode); level++)
            for (int k = 0; k < count; k++)
            {
                byte kc = (byte)(_minKeycode + k);
                if (_assigned.ContainsKey(kc)) continue;
                if (_map[k * _perKeycode + level] == keysym) return (kc, level);
            }
        return (0, 0);
    }

    private void LoadMapping()
    {
        X11.XDisplayKeycodes(_display, out _minKeycode, out _maxKeycode);
        int count = _maxKeycode - _minKeycode + 1;
        nuint* map = X11.XGetKeyboardMapping(_display, (byte)_minKeycode, count, out _perKeycode);
        if (map == null)
        {
            _map = [];
            _perKeycode = 0;
            return;
        }
        try
        {
            _map = new ReadOnlySpan<nuint>(map, count * _perKeycode).ToArray();
        }
        finally
        {
            X11.XFree(map);
        }
        if (_spare.Count == 0)
        {
            // Freie Tastencodes: ohne jede Keysym, aufsteigend (Xlib fasst Änderungen zu einem Bereich zusammen)
            for (int k = 1; k < count && _spare.Count < 10; k++)
            {
                bool empty = true;
                for (int l = 0; l < _perKeycode; l++)
                    if (_map[k * _perKeycode + l] != 0) { empty = false; break; }
                if (empty) _spare.Add((byte)(_minKeycode + k));
            }
        }
    }

    private void ScheduleRestore()
    {
        if (_assigned.Count == 0) return;
        _lastSend = Environment.TickCount64;
        _restoreTimer ??= new Timer(_ => RestoreWhenIdle());
        _restoreTimer.Change(RestoreDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Zeitgeber: Belegungen erst entfernen, wenn seit dem letzten Tippen <see cref="RestoreDelay"/> vergangen ist. Lief der
    /// Zeitgeber ab, während gerade getippt wurde, wartete er auf die Sperre und hätte die eben gesetzten Belegungen sofort
    /// danach entfernt – das Ziel las dann für diese Tasten keine Keysym (CI unter Xvfb).
    /// </summary>
    private void RestoreWhenIdle()
    {
        lock (_gate)
        {
            long left = _lastSend + (long)RestoreDelay.TotalMilliseconds - Environment.TickCount64;
            if (left > 0)
            {
                _restoreTimer?.Change(TimeSpan.FromMilliseconds(left), Timeout.InfiniteTimeSpan);
                return;
            }
            Restore();
        }
    }

    /// <summary>Entfernt die vorübergehenden Belegungen wieder.</summary>
    internal void Restore()
    {
        lock (_gate)
        {
            if (_display == 0 || _assigned.Count == 0) return;
            nuint* none = stackalloc nuint[2];
            none[0] = none[1] = 0;
            foreach (byte kc in _assigned.Keys) X11.XChangeKeyboardMapping(_display, kc, 2, none, 1);
            _assigned.Clear();
            X11.XSync(_display, 0);
        }
    }

    private bool Open()
    {
        if (_display != 0) return true;
        if (_openError is not null) return false;
        _display = X11.TryOpen(out _openError);
        if (_display == 0) return false;
        _hasXTest = X11.HasXTest(_display);
        return true;
    }

    public void Dispose()
    {
        _restoreTimer?.Dispose();
        Restore();
        lock (_gate)
        {
            if (_display != 0) X11.XCloseDisplay(_display);
            _display = 0;
        }
    }
}
