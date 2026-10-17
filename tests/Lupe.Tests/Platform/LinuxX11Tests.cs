using System.Text;
using Lupe.Core.Input;
using Lupe.Core.Model;
using Lupe.Core.Screen;
using Lupe.Platform.Linux;
using Lupe.Platform.Linux.Input;
using Lupe.Platform.Linux.Native;
using Lupe.Platform.Linux.Screen;

namespace Lupe.Tests.Platform;

/// <summary>Nur unter Linux mit X-Server (in CI: <c>xvfb-run</c>), nicht unter Wayland.</summary>
public sealed class X11FactAttribute : FactAttribute
{
    public X11FactAttribute()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            Skip = "Nur unter Linux mit X-Server (z. B. xvfb-run).";
    }
}

/// <summary>Ein kleiner Test-Client: eigenes Fenster mit Hintergrundfarbe, liest Tastendrücke wie ein echtes Programm.</summary>
internal sealed unsafe class X11TestWindow : IDisposable
{
    private readonly Thread _reader;
    private readonly StringBuilder _typed = new();
    private volatile bool _stop;

    public X11TestWindow(int x, int y, int width, int height, uint rgb, bool readKeys = false)
    {
        Display = X11.TryOpen(out string? error);
        Assert.True(Display != 0, error);
        nint root = X11.XDefaultRootWindow(Display);
        Window = X11.XCreateSimpleWindow(Display, root, x, y, (uint)width, (uint)height, 0, 0, rgb);
        X11.XSelectInput(Display, Window, X11.KeyPressMask | X11.ExposureMask | X11.StructureNotifyMask);
        X11.XMapRaised(Display, Window);
        X11.XSync(Display, 0);
        Thread.Sleep(150);
        _reader = new Thread(ReadKeys) { IsBackground = true };
        if (readKeys) _reader.Start();
    }

    public StringBuilder Log { get; } = new();
    public nint Display { get; }
    public nint Window { get; }

    public void Focus()
    {
        X11.XSetInputFocus(Display, Window, X11.RevertToParent, X11.CurrentTime);
        X11.XSync(Display, 0);
    }

    public string Typed
    {
        get { lock (_typed) return _typed.ToString(); }
    }

    /// <summary>Wartet, bis der Text angekommen ist (oder die Zeit um ist), und liefert, was ankam.</summary>
    public string WaitFor(string expected, int timeoutMs = 5000)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < until && Typed.Length < expected.Length) Thread.Sleep(20);
        Thread.Sleep(100);
        return Typed;
    }

    private void ReadKeys()
    {
        XEvent ev;
        byte* buffer = stackalloc byte[16];
        while (!_stop)
        {
            if (X11.XPending(Display) == 0)
            {
                Thread.Sleep(2);
                continue;
            }
            X11.XNextEvent(Display, &ev);
            if (ev.type == X11.MappingNotify)
            {
                X11.XRefreshKeyboardMapping(&ev);
                continue;
            }
            if (ev.type != X11.KeyPress) continue;
            X11.XLookupString(&ev.key, buffer, 16, out nuint keysym, 0);
            lock (_typed) Log.Append($"{ev.key.keycode}:{(uint)keysym:X} ");
            string? text = (uint)keysym switch
            {
                Keysyms.Return => "\n",
                Keysyms.Tab => "\t",
                var k when k is >= 0x01000000 and <= 0x0110FFFF => char.ConvertFromUtf32((int)(k - 0x01000000)),
                var k => Keysyms.ToChar(k)?.ToString(),
            };
            if (text is not null)
                lock (_typed) _typed.Append(text);
        }
    }

    public void Dispose()
    {
        _stop = true;
        if (_reader.IsAlive) _reader.Join(1000);
        X11.XDestroyWindow(Display, Window);
        X11.XCloseDisplay(Display);
    }
}

[Collection(LinuxSessionCollection.Name)]
public sealed class LinuxX11Tests
{
    [X11Fact]
    public void Monitore_und_virtueller_Bildschirm()
    {
        var screen = new LinuxScreenCapture();
        var monitors = screen.Monitors();
        Assert.NotEmpty(monitors);
        Assert.Single(monitors, m => m.IsPrimary);
        var virt = screen.VirtualScreen;
        Assert.True(virt.Width >= 640 && virt.Height >= 480, $"{virt}");
    }

    [X11Fact]
    public void XGetImage_liefert_das_Fenster_in_seiner_Farbe()
    {
        using var window = new X11TestWindow(100, 120, 60, 40, 0xFF0000); // Rot
        var screen = new LinuxScreenCapture();

        Assert.True(screen.TryCapture(new PixelRect(90, 110, 80, 60), out var image, out string? error), error);
        Assert.Equal((80, 60), (image!.Width, image.Height));
        int Inside(int x, int y, int c) => image.Bgra[y * image.Stride + x * 4 + c];
        Assert.Equal(255, Inside(30, 30, 2)); // R
        Assert.Equal(0, Inside(30, 30, 1));
        Assert.Equal(0, Inside(30, 30, 0));
        Assert.Equal(255, Inside(30, 30, 3));
        Assert.NotEqual(255, Inside(2, 2, 2)); // außerhalb des Fensters nicht rot
    }

    [X11Fact]
    public void Ausschnitt_ueber_den_Rand_hinaus_wird_schwarz_aufgefuellt()
    {
        var screen = new LinuxScreenCapture();
        var virt = screen.VirtualScreen;
        using var window = new X11TestWindow(virt.Width - 20, 0, 20, 20, 0x00FF00); // Grün in der Ecke

        Assert.True(screen.TryCapture(new PixelRect(virt.Width - 20, 0, 40, 20), out var image, out string? error), error);
        Assert.Equal(40, image!.Width);
        int o = 10 * image.Stride;
        Assert.Equal(255, image.Bgra[o + 5 * 4 + 1]);       // im Fenster: grün
        Assert.Equal(0, image.Bgra[o + 30 * 4 + 1]);        // jenseits des Bildschirms: schwarz
        Assert.Equal(255, image.Bgra[o + 30 * 4 + 3]);
    }

    [X11Fact]
    public void XTest_tippt_Unicode_unabhaengig_vom_Layout()
    {
        using var window = new X11TestWindow(10, 10, 200, 100, 0xFFFFFF, readKeys: true);
        window.Focus();
        using var keyboard = new LinuxKeyboard();
        const string text = "Hallo Wörld! ß€Łø→ Œ😀\tEnde";

        Assert.True(KeyboardTyper.TypeCore(text, "Enter", keyboard, out string? error, default), error);

        string typed = window.WaitFor(text + "\n");
        Assert.True(text + "\n" == typed, $"{typed} | {window.Log}");
    }

    [X11Fact]
    public void XTest_tippt_auch_mit_Zeichenpause_und_vielen_fremden_Zeichen()
    {
        using var window = new X11TestWindow(10, 10, 200, 100, 0xFFFFFF, readKeys: true);
        window.Focus();
        using var keyboard = new LinuxKeyboard();
        // Mehr fremde Zeichen als freie Tastencodes: die Belegungen müssen reihum wechseln
        const string text = "αβγδεζηθικλμνξοπρστυφχψω";

        Assert.True(KeyboardTyper.TypeCore(text, "", keyboard, out string? error, default, delayMs: 5), error);

        Assert.Equal(text, window.WaitFor(text));
    }

    [X11Fact]
    public unsafe void Gedrueckte_Tasten_werden_erkannt()
    {
        using var keyboard = new LinuxKeyboard();
        nint d = X11.TryOpen(out _);
        try
        {
            byte shift = X11.XKeysymToKeycode(d, Keysyms.ShiftL);
            X11.XTestFakeKeyEvent(d, shift, 1, 0);
            X11.XSync(d, 0);
            Assert.True(keyboard.IsDown(0x10));
            Assert.True(keyboard.IsDown(0xA0));
            X11.XTestFakeKeyEvent(d, shift, 0, 0);
            X11.XSync(d, 0);
            Assert.False(keyboard.IsDown(0x10));
        }
        finally
        {
            X11.XCloseDisplay(d);
        }
    }

    [X11Fact]
    public void XGrabKey_meldet_das_Kuerzel_und_belegte_Kuerzel()
    {
        var hotkeys = new LinuxHotkeys();
        Assert.Null(hotkeys.UnsupportedReason);
        // Strg+Alt+K (Strg+Alt+F-Tasten schaltet XKB auf die Textkonsolen um, die kommen nie an)
        var combo = new Hotkey(Ctrl: true, Alt: true, Shift: false, Win: false, Key: 0x4B);
        using var hotkey = hotkeys.Register(combo);
        Assert.True(hotkey.IsRegistered, hotkey.Error);
        int pressed = 0;
        hotkey.Pressed += (_, _) => Interlocked.Increment(ref pressed);

        // Zweite Anmeldung desselben Kürzels: belegt
        using (var second = hotkeys.Register(combo))
        {
            Assert.False(second.IsRegistered);
            Assert.Contains("belegt", second.Error);
        }

        // Mit eingeschalteter Num-Taste (Mod2) muss es trotzdem auslösen
        Press(["Control_L", "Alt_L", "K"], numLock: true);
        WaitUntil(() => Volatile.Read(ref pressed) == 1);
        Press(["Control_L", "Alt_L", "K"], numLock: false);
        WaitUntil(() => Volatile.Read(ref pressed) == 2);
        Press(["Control_L", "K"], numLock: false); // ohne Alt: nicht gemeint
        Thread.Sleep(200);
        Assert.Equal(2, Volatile.Read(ref pressed));

        hotkey.Dispose();
        using var again = hotkeys.Register(combo);
        Assert.True(again.IsRegistered, again.Error);
    }

    [X11Fact]
    public async Task Vordergrundfenster_merken_und_wieder_nach_vorn_holen()
    {
        var active = new LinuxActiveWindow();
        Assert.True(active.IsSupported);
        using var first = new X11TestWindow(10, 10, 50, 50, 0x0000FF);
        using var second = new X11TestWindow(100, 10, 50, 50, 0x00FF00);
        first.Focus();
        Assert.Equal(first.Window, active.Foreground());
        Assert.False(active.IsOwnWindow(first.Window)); // ohne _NET_WM_PID

        second.Focus();
        Assert.Equal(second.Window, active.Foreground());
        Assert.Null(await active.ActivateAsync(first.Window));
        Assert.Equal(first.Window, active.Foreground());
        Assert.NotNull(await active.ActivateAsync(0));
    }

    [X11Fact]
    public void Tastaturlayout_US_von_Xvfb()
    {
        var layout = new LinuxKeyboardLayout();
        Assert.Equal(';', layout.KeyChar(0xBA));
        Assert.Equal(0xBA, layout.KeyForChar(';'));
        Assert.Equal(0xDB, layout.KeyForChar('['));
        Assert.Equal(0x51, layout.KeyForChar('q'));
        Assert.Equal('q', layout.KeyChar(0x51));
        Assert.Null(layout.KeyForChar('ü'));
        Assert.Equal("[", HotkeyText.Format(new Hotkey(true, false, false, false, 0xDB)).Split('+')[^1]);
    }

    [X11Fact]
    public void Probleme_unter_X11_mit_XTest_keine()
    {
        Assert.DoesNotContain(new LinuxProblems().Check(), p => p.Feature is Lupe.Core.Platform.PlatformFeature.Typing or Lupe.Core.Platform.PlatformFeature.Screen);
    }

    private static void Press(string[] keys, bool numLock)
    {
        nint d = X11.TryOpen(out _);
        try
        {
            var codes = keys.Select(k => X11.XKeysymToKeycode(d, KeysymOf(k))).ToList();
            byte num = X11.XKeysymToKeycode(d, 0xFF7F);
            if (numLock) Tap(d, num);
            foreach (var c in codes) X11.XTestFakeKeyEvent(d, c, 1, 0);
            for (int i = codes.Count - 1; i >= 0; i--) X11.XTestFakeKeyEvent(d, codes[i], 0, 0);
            if (numLock) Tap(d, num);
            X11.XSync(d, 0);
        }
        finally
        {
            X11.XCloseDisplay(d);
        }
    }

    private static void Tap(nint d, byte code)
    {
        X11.XTestFakeKeyEvent(d, code, 1, 0);
        X11.XTestFakeKeyEvent(d, code, 0, 0);
    }

    private static nuint KeysymOf(string name) => name switch
    {
        "Control_L" => Keysyms.ControlL,
        "Alt_L" => Keysyms.AltL,
        "K" => 0x6B,
        _ => throw new ArgumentException(name),
    };

    private static void WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            Assert.True(Environment.TickCount64 < until, "Zeit abgelaufen");
            Thread.Sleep(20);
        }
    }
}

/// <summary>Nur unter Linux in einer Wayland-Sitzung mit Sitzungsbus und xdg-desktop-portal (in Tests: sway headless + xdg-desktop-portal-wlr).</summary>
public sealed class WaylandFactAttribute : FactAttribute
{
    public WaylandFactAttribute()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")))
            Skip = "Nur in einer Wayland-Sitzung mit Desktop-Portalen.";
    }
}

[Collection(LinuxSessionCollection.Name)]
public sealed class LinuxWaylandTests
{
    [WaylandFact]
    public void Screenshot_Portal_liefert_den_Ausschnitt()
    {
        var screen = new LinuxScreenCapture();
        Assert.True(screen.TryCapture(new PixelRect(0, 0, 120, 80), out var image, out string? error), error);
        Assert.Equal((120, 80), (image!.Width, image.Height));
        Assert.All(Enumerable.Range(0, 120 * 80), i => Assert.Equal(255, image.Bgra[i * 4 + 3]));
        // Nach dem ersten Foto ist die Größe des Desktops bekannt
        Assert.True(screen.VirtualScreen.Width > 0);
    }

    [WaylandFact]
    public void Ohne_RemoteDesktop_und_GlobalShortcuts_kommen_deutsche_Hinweise()
    {
        using var keyboard = new LinuxKeyboard();
        string? problem = keyboard.ForegroundProblem();
        var hotkeys = new LinuxHotkeys();
        // xdg-desktop-portal-wlr kann weder das eine noch das andere; andere Desktops (KDE, GNOME) schon
        if (problem is not null) Assert.Contains("Eintippen geht nicht", problem);
        if (hotkeys.UnsupportedReason is { } reason) Assert.Contains("Tastenkürzel", reason);
        Assert.False(new LinuxActiveWindow().IsSupported);
    }
}
