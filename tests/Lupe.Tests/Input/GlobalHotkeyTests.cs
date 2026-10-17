using System.Collections.Concurrent;
using Lupe.Core.Input;
using Lupe.Core.Model;
using Lupe.Tests.Screen;

using Lupe.Platform.Windows.Input;

namespace Lupe.Tests.Input;

/// <summary>
/// Thread, Nachrichtenfenster, Nachrichtenschleife und Abmeldung von GlobalHotkey – mit echtem Thread und echtem
/// Fenster, nur RegisterHotKey selbst ist ersetzt (in Sitzung 0 verweigert Windows es). WM_HOTKEY wird per PostMessage
/// an das Nachrichtenfenster geschickt, genau wie Windows es beim Tastendruck tut.
/// </summary>
public sealed class GlobalHotkeyTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    /// <summary>Strg+Alt+Q – der frühere Standard, an dem sich der AltGr-Schutz zeigt (AltGr+Q = „@“).</summary>
    internal static readonly Hotkey CtrlAltQ = new(Ctrl: true, Alt: true, Shift: false, Win: false, Key: 0x51);

    private sealed class Recorder
    {
        public ConcurrentQueue<(nint Window, int Id, uint Modifiers, uint Vk, int Thread, ApartmentState Apartment)> Registered { get; } = new();
        public ConcurrentQueue<(nint Window, int Id, int Thread)> Unregistered { get; } = new();
        public ConcurrentQueue<string> Retyped { get; } = new();
        public int Error { get; init; }
        public string? AltGr { get; set; }

        public HotkeyApi Api => new()
        {
            Register = (window, id, modifiers, vk) =>
            {
                Registered.Enqueue((window, id, modifiers, vk, Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
                return Error;
            },
            Unregister = (window, id) => Unregistered.Enqueue((window, id, Environment.CurrentManagedThreadId)),
            AltGrText = _ => AltGr,
            Retype = text => Retyped.Enqueue(text),
        };
    }

    private static void PostHotkey(GlobalHotkey hotkey, int id = GlobalHotkey.HotkeyId)
        => Assert.True(Native.PostMessageW(hotkey.WindowHandle, Native.WM_HOTKEY, id, 0), "WM_HOTKEY ließ sich nicht senden");

    [Fact]
    public void Meldet_auf_eigenem_STA_Thread_mit_Nachrichtenfenster_und_NOREPEAT_an()
    {
        var recorder = new Recorder();
        using var hotkey = new GlobalHotkey(CtrlAltQ, recorder.Api);

        Assert.True(hotkey.IsRegistered);
        Assert.Null(hotkey.Error);
        var call = Assert.Single(recorder.Registered);
        Assert.NotEqual(Environment.CurrentManagedThreadId, call.Thread);
        Assert.Equal(ApartmentState.STA, call.Apartment);
        Assert.NotEqual(0, call.Window);
        Assert.Equal(hotkey.WindowHandle, call.Window);
        Assert.True(Native.IsWindow(call.Window));
        Assert.Equal(Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, call.Modifiers);
        Assert.Equal(0x51u, call.Vk);
        Assert.True(hotkey.WorkerThread!.IsBackground);
    }

    [Theory]
    [InlineData(false, false, true, true, Native.MOD_SHIFT | Native.MOD_WIN)]
    [InlineData(true, false, true, false, Native.MOD_CONTROL | Native.MOD_SHIFT)]
    [InlineData(false, true, false, false, Native.MOD_ALT)]
    public void Umschalttasten_werden_zu_MOD_Flags(bool ctrl, bool alt, bool shift, bool win, uint expected)
        => Assert.Equal(expected | Native.MOD_NOREPEAT, GlobalHotkey.Modifiers(new Hotkey(ctrl, alt, shift, win, 0x78)));

    [Fact]
    public void WM_HOTKEY_loest_Pressed_auf_einem_Hintergrund_Thread_aus()
    {
        var recorder = new Recorder();
        using var hotkey = new GlobalHotkey(Hotkey.Default, recorder.Api);
        var threads = new ConcurrentQueue<int>();
        using var pressed = new SemaphoreSlim(0);
        hotkey.Pressed += (sender, _) =>
        {
            Assert.Same(hotkey, sender);
            threads.Enqueue(Environment.CurrentManagedThreadId);
            pressed.Release();
        };

        PostHotkey(hotkey);
        PostHotkey(hotkey);

        Assert.True(pressed.Wait(Wait));
        Assert.True(pressed.Wait(Wait));
        Assert.All(threads, id => Assert.Equal(hotkey.WorkerThread!.ManagedThreadId, id));
        Assert.DoesNotContain(Environment.CurrentManagedThreadId, threads);
    }

    [Fact]
    public void Fremde_Kennung_und_andere_Nachrichten_loesen_nichts_aus()
    {
        var recorder = new Recorder();
        using var hotkey = new GlobalHotkey(Hotkey.Default, recorder.Api);
        int count = 0;
        hotkey.Pressed += (_, _) => Interlocked.Increment(ref count);
        using var marker = new SemaphoreSlim(0);

        PostHotkey(hotkey, id: 4711);
        Assert.True(Native.PostMessageW(hotkey.WindowHandle, Native.WM_APP + 7, 0, 0));
        // Danach ein echtes, damit sicher ist, dass die beiden vorigen verarbeitet wurden
        hotkey.Pressed += (_, _) => marker.Release();
        PostHotkey(hotkey);

        Assert.True(marker.Wait(Wait));
        Assert.Equal(1, Volatile.Read(ref count));
    }

    [Fact]
    public void Dispose_meldet_auf_demselben_Thread_ab_beendet_ihn_und_zerstoert_das_Fenster()
    {
        var recorder = new Recorder();
        var hotkey = new GlobalHotkey(Hotkey.Default, recorder.Api);
        nint window = hotkey.WindowHandle;
        var thread = hotkey.WorkerThread!;

        hotkey.Dispose();

        Assert.False(hotkey.IsRegistered);
        Assert.False(thread.IsAlive);
        Assert.False(Native.IsWindow(window));
        Assert.Equal(0, hotkey.WindowHandle);
        var registered = Assert.Single(recorder.Registered);
        var unregistered = Assert.Single(recorder.Unregistered);
        Assert.Equal(registered.Window, unregistered.Window);
        Assert.Equal(registered.Id, unregistered.Id);
        Assert.Equal(registered.Thread, unregistered.Thread); // RegisterHotKey und UnregisterHotKey müssen vom selben Thread kommen
    }

    [Fact]
    public void Dispose_mehrfach_ist_harmlos()
    {
        var recorder = new Recorder();
        var hotkey = new GlobalHotkey(Hotkey.Default, recorder.Api);

        hotkey.Dispose();
        hotkey.Dispose();
        hotkey.Dispose();

        Assert.Single(recorder.Unregistered);
    }

    [Fact]
    public void Nach_Dispose_kommt_kein_Pressed_mehr()
    {
        var recorder = new Recorder();
        var hotkey = new GlobalHotkey(Hotkey.Default, recorder.Api);
        int count = 0;
        hotkey.Pressed += (_, _) => Interlocked.Increment(ref count);
        nint window = hotkey.WindowHandle;

        hotkey.Dispose();

        Assert.False(Native.PostMessageW(window, Native.WM_HOTKEY, GlobalHotkey.HotkeyId, 0)); // Fenster ist weg
        Thread.Sleep(50);
        Assert.Equal(0, Volatile.Read(ref count));
    }

    [Fact]
    public void Dispose_aus_dem_Pressed_Handler_heraus_haengt_nicht()
    {
        var recorder = new Recorder();
        var hotkey = new GlobalHotkey(Hotkey.Default, recorder.Api);
        using var handled = new ManualResetEventSlim();
        hotkey.Pressed += (_, _) =>
        {
            hotkey.Dispose();
            handled.Set();
        };

        PostHotkey(hotkey);

        Assert.True(handled.Wait(Wait));
        Assert.True(hotkey.WorkerThread!.Join(Wait));
        Assert.False(hotkey.IsRegistered);
        Assert.Single(recorder.Unregistered);
    }

    [Fact]
    public void Dispose_wartet_auf_einen_laufenden_Handler()
    {
        var recorder = new Recorder();
        var hotkey = new GlobalHotkey(Hotkey.Default, recorder.Api);
        using var entered = new ManualResetEventSlim();
        bool finished = false;
        hotkey.Pressed += (_, _) =>
        {
            entered.Set();
            Thread.Sleep(200);
            Volatile.Write(ref finished, true);
        };
        PostHotkey(hotkey);
        Assert.True(entered.Wait(Wait));

        hotkey.Dispose();

        Assert.True(Volatile.Read(ref finished));
        Assert.False(hotkey.WorkerThread!.IsAlive);
    }

    [Fact]
    public void Belegtes_Kuerzel_meldet_deutschen_Fehler_und_beendet_den_Thread()
    {
        var recorder = new Recorder { Error = Native.ERROR_HOTKEY_ALREADY_REGISTERED };
        using var hotkey = new GlobalHotkey(Hotkey.Default, recorder.Api);

        Assert.False(hotkey.IsRegistered);
        Assert.Equal("Win+Umschalt+Q ist schon von einem anderen Programm belegt.", hotkey.Error);
        Assert.True(hotkey.WorkerThread!.Join(Wait));
        Assert.Empty(recorder.Unregistered); // nichts abzumelden
    }

    [Theory]
    [InlineData(Native.ERROR_REQUIRES_INTERACTIVE_WINDOWSTATION, "ohne sichtbaren Desktop")]
    [InlineData(Native.ERROR_INVALID_PARAMETER, "kein gültiges Tastenkürzel")]
    [InlineData(Native.ERROR_ACCESS_DENIED, "konnte nicht angemeldet werden")]
    public void Andere_Fehler_auf_Deutsch(int error, string part)
    {
        var recorder = new Recorder { Error = error };
        using var hotkey = new GlobalHotkey(new Hotkey(true, false, true, false, 0x78), recorder.Api);

        Assert.False(hotkey.IsRegistered);
        Assert.Contains(part, hotkey.Error);
        Assert.Contains("Strg+Umschalt+F9", hotkey.Error);
    }

    [Fact]
    public void Fehlschlag_ohne_Windows_Grund_hat_keinen_englischen_Zusatz()
        => Assert.Equal("Das Tastenkürzel Win+Umschalt+Q konnte nicht angemeldet werden.", GlobalHotkey.Describe(-1, Hotkey.Default));

    [Fact]
    public void Ungeeignetes_Kuerzel_wird_gar_nicht_erst_angemeldet()
    {
        var recorder = new Recorder();
        using var hotkey = new GlobalHotkey(new Hotkey(false, false, false, false, 0x51), recorder.Api); // nur Q

        Assert.False(hotkey.IsRegistered);
        Assert.Contains("würde das normale Tippen stören", hotkey.Error);
        Assert.Empty(recorder.Registered);
        Assert.Null(hotkey.WorkerThread);
    }

    [Fact]
    public void Standard_Win_Umschalt_Q_meldet_MOD_WIN_und_MOD_SHIFT_an()
    {
        var recorder = new Recorder();
        using var hotkey = new GlobalHotkey(Hotkey.Default, recorder.Api);

        Assert.True(hotkey.IsRegistered);
        var call = Assert.Single(recorder.Registered);
        Assert.Equal(Native.MOD_WIN | Native.MOD_SHIFT | Native.MOD_NOREPEAT, call.Modifiers);
        Assert.Equal(0x51u, call.Vk);
    }

    [Fact]
    public void Standard_kollidiert_nicht_mit_AltGr()
    {
        // Auch wenn gerade AltGr gedrückt wäre: Win+Umschalt+Q ist nie ein AltGr-Zeichen und löst den Scan aus
        var recorder = new Recorder { AltGr = "@" };
        using var hotkey = new GlobalHotkey(Hotkey.Default, recorder.Api);
        using var pressed = new SemaphoreSlim(0);
        hotkey.Pressed += (_, _) => pressed.Release();

        PostHotkey(hotkey);

        Assert.True(pressed.Wait(Wait));
        Assert.Empty(recorder.Retyped);
        Assert.False(AltGr.Applies(Hotkey.Default));
    }

    [Fact]
    public void AltGr_Zeichen_wird_nachgetippt_statt_Pressed_auszuloesen()
    {
        var recorder = new Recorder { AltGr = "@" };
        using var hotkey = new GlobalHotkey(CtrlAltQ, recorder.Api);
        int pressed = 0;
        hotkey.Pressed += (_, _) => Interlocked.Increment(ref pressed);

        PostHotkey(hotkey);
        SpinWait.SpinUntil(() => !recorder.Retyped.IsEmpty, Wait);

        Assert.Equal(["@"], recorder.Retyped);
        Assert.Equal(0, Volatile.Read(ref pressed));
    }

    [Fact]
    public void Ohne_Strg_und_Alt_gibt_es_keinen_AltGr_Schutz()
    {
        var recorder = new Recorder { AltGr = "@" };
        using var hotkey = new GlobalHotkey(new Hotkey(false, false, true, true, 0x51), recorder.Api); // Win+Umschalt+Q
        using var pressed = new SemaphoreSlim(0);
        hotkey.Pressed += (_, _) => pressed.Release();

        PostHotkey(hotkey);

        Assert.True(pressed.Wait(Wait));
        Assert.Empty(recorder.Retyped);
    }

    [Fact]
    public void Viele_Instanzen_nacheinander_hinterlassen_keine_Threads_und_Fenster()
    {
        var recorder = new Recorder();
        var windows = new List<nint>();
        var threads = new List<Thread>();
        for (int i = 0; i < 40; i++)
        {
            using var hotkey = new GlobalHotkey(Hotkey.Default, recorder.Api);
            Assert.True(hotkey.IsRegistered);
            windows.Add(hotkey.WindowHandle);
            threads.Add(hotkey.WorkerThread!);
        }

        Assert.All(threads, t => Assert.False(t.IsAlive));
        Assert.All(windows, w => Assert.False(Native.IsWindow(w)));
        Assert.Equal(40, recorder.Unregistered.Count);
    }

    [Fact]
    public void Null_ist_ein_Programmierfehler()
        => Assert.Throws<ArgumentNullException>(() => new GlobalHotkey(null!));
}

/// <summary>Was AltGr auf dem deutschen Layout schreibt – Grundlage des AltGr-Schutzes.</summary>
public sealed class AltGrTests
{
    [GermanKeyboardFact]
    public void Deutsches_Layout_kennt_die_AltGr_Zeichen()
    {
        nint layout = InteractiveDesktop.ThreadLayout;
        Assert.Equal("@", AltGr.Text(0x51, shift: false, layout));  // Q
        Assert.Equal("€", AltGr.Text(0x45, shift: false, layout));  // E
        Assert.Equal("{", AltGr.Text(0x37, shift: false, layout));  // 7
        Assert.Equal("\\", AltGr.Text(0xDB, shift: false, layout)); // ß
        Assert.Equal("µ", AltGr.Text(0x4D, shift: false, layout));  // M
    }

    [GermanKeyboardFact]
    public void Tasten_ohne_AltGr_Belegung_sind_echte_Kuerzel()
    {
        nint layout = InteractiveDesktop.ThreadLayout;
        Assert.Null(AltGr.Text(0x41, shift: false, layout)); // A
        Assert.Null(AltGr.Text(0x78, shift: false, layout)); // F9
        Assert.Null(AltGr.Text(0x2C, shift: false, layout)); // Druck
        Assert.Null(AltGr.Text(0, shift: false, layout));
    }

    [Fact]
    public void Schutz_greift_nur_bei_Strg_und_Alt_ohne_Win()
    {
        Assert.True(AltGr.Applies(GlobalHotkeyTests.CtrlAltQ));
        Assert.False(AltGr.Applies(Hotkey.Default)); // Win+Umschalt+Q
        Assert.True(AltGr.Applies(new Hotkey(true, true, true, false, 0x51)));
        Assert.False(AltGr.Applies(new Hotkey(true, true, false, true, 0x51)));
        Assert.False(AltGr.Applies(new Hotkey(true, false, false, false, 0x51)));
        Assert.False(AltGr.Applies(new Hotkey(false, true, false, false, 0x51)));
    }

    [Fact]
    public void Ohne_gedrueckte_rechte_Alt_Taste_ist_es_ein_echtes_Kuerzel()
        => Assert.Null(AltGr.TextIfHeld(GlobalHotkeyTests.CtrlAltQ)); // im Test drückt niemand AltGr
}

/// <summary>Echte Anmeldung bei Windows.</summary>
[Collection(DesktopCollection.Name)]
public sealed class GlobalHotkeySystemTests
{
    // Strg+Umschalt+Alt+F23: belegt in der Praxis niemand
    private static readonly Hotkey Rare = new(Ctrl: true, Alt: true, Shift: true, Win: false, Key: 0x86);

    [NonInteractiveFact]
    public void Ohne_sichtbaren_Desktop_meldet_die_Anmeldung_den_Grund()
    {
        using var hotkey = new GlobalHotkey(Rare);

        Assert.False(hotkey.IsRegistered);
        Assert.Equal(
            "Das Tastenkürzel Strg+Umschalt+Alt+F23 geht hier nicht – Lupe läuft ohne sichtbaren Desktop (z. B. als Dienst oder in Sitzung 0).",
            hotkey.Error);
    }

    [InteractiveFact]
    public void Zweite_Instanz_ist_belegt_und_Dispose_gibt_das_Kuerzel_frei()
    {
        var first = new GlobalHotkey(Rare);
        try
        {
            Assert.True(first.IsRegistered, first.Error);
            using (var second = new GlobalHotkey(Rare))
            {
                Assert.False(second.IsRegistered);
                Assert.Equal("Strg+Umschalt+Alt+F23 ist schon von einem anderen Programm belegt.", second.Error);
            }
        }
        finally
        {
            first.Dispose();
        }

        using var third = new GlobalHotkey(Rare);
        Assert.True(third.IsRegistered, third.Error);
    }

    [NonInteractiveFact]
    public void Standard_Win_Umschalt_Q_scheitert_in_Sitzung_0_nur_am_fehlenden_Desktop()
    {
        // Windows nimmt die Anmeldung an (kein „ungültiges Kürzel“), lehnt sie hier aber wegen der unsichtbaren Fensterstation ab
        using var hotkey = new GlobalHotkey(Hotkey.Default);

        Assert.False(hotkey.IsRegistered);
        Assert.Equal(
            "Das Tastenkürzel Win+Umschalt+Q geht hier nicht – Lupe läuft ohne sichtbaren Desktop (z. B. als Dienst oder in Sitzung 0).",
            hotkey.Error);
    }

    [InteractiveFact]
    public void Standard_Win_Umschalt_Q_laesst_sich_bei_Windows_anmelden()
    {
        // Viele Win+Buchstabe-Kürzel gehören Windows selbst (Win+Umschalt+S: Ausschnitt) – Win+Umschalt+Q nicht.
        using var hotkey = new GlobalHotkey(Hotkey.Default);
        if (!hotkey.IsRegistered && LupeRunsInThisSession())
        {
            // Die echte Lupe des Benutzers hält das Kürzel gerade – dann ist „belegt“ die richtige Antwort
            Assert.Equal("Win+Umschalt+Q ist schon von einem anderen Programm belegt.", hotkey.Error);
            return;
        }
        Assert.True(hotkey.IsRegistered, hotkey.Error);
        Assert.Null(hotkey.Error);
    }

    private static bool LupeRunsInThisSession()
    {
        int session = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        var running = System.Diagnostics.Process.GetProcessesByName("Lupe");
        try { return running.Any(p => p.SessionId == session); }
        finally { foreach (var p in running) p.Dispose(); }
    }

    [InteractiveFact]
    public void Tastendruck_loest_Pressed_aus()
    {
        using var hotkey = new GlobalHotkey(Rare);
        Assert.True(hotkey.IsRegistered, hotkey.Error);
        using var pressed = new SemaphoreSlim(0);
        hotkey.Pressed += (_, _) => pressed.Release();

        Press(KeyboardTyper.VK_LCONTROL, KeyboardTyper.VK_LSHIFT, KeyboardTyper.VK_LMENU, 0x86);

        Assert.True(pressed.Wait(TimeSpan.FromSeconds(5)), "Pressed kam nicht");
    }

    /// <summary>
    /// Drückt die Tasten nacheinander und lässt sie in umgekehrter Reihenfolge los. Davor ein Druck auf die unbelegte
    /// Maskentaste: Das Tastenkürzel verschluckt die Haupttaste, sonst sähe das Vordergrundfenster ein „nacktes“ Alt
    /// und würde sein Menü öffnen.
    /// </summary>
    internal static void Press(params int[] keys)
    {
        var inputs = new List<Native.INPUT>();
        foreach (int vk in keys) inputs.Add(KeyInput(vk, up: false));
        inputs.Add(KeyInput(KeyboardTyper.VK_MASK, up: false));
        inputs.Add(KeyInput(KeyboardTyper.VK_MASK, up: true));
        foreach (int vk in keys.Reverse()) inputs.Add(KeyInput(vk, up: true));
        Assert.Equal(inputs.Count, WindowsKeyboard.Send(inputs.ToArray(), out _));
    }

    internal static Native.INPUT KeyInput(int vk, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        u = new Native.INPUTUNION
        {
            ki = new Native.KEYBDINPUT
            {
                wVk = (ushort)vk,
                wScan = (ushort)Native.MapVirtualKeyW((uint)vk, Native.MAPVK_VK_TO_VSC),
                dwFlags = up ? Native.KEYEVENTF_KEYUP : 0,
            },
        },
    };
}
