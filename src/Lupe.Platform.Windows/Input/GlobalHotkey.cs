// SPDX-License-Identifier: MIT
using System.ComponentModel;
using System.Runtime.InteropServices;
using Lupe.Core.Input;
using Lupe.Core.Model;
using Lupe.Core.Platform;
using Lupe.Core.Resources;

namespace Lupe.Platform.Windows.Input;

/// <summary>Systemweites Tastenkürzel (RegisterHotKey) auf eigenem Thread mit Nachrichtenfenster.</summary>
/// <remarks>
/// <para>Der Konstruktor meldet das Kürzel an und kehrt erst zurück, wenn das Ergebnis feststeht: danach sagen
/// <see cref="IsRegistered"/> und <see cref="Error"/>, ob es geklappt hat. Gedrückt halten löst nur einmal aus
/// (MOD_NOREPEAT). <see cref="Dispose"/> meldet das Kürzel ab und beendet den Thread; danach kommt kein
/// <see cref="Pressed"/> mehr. Dispose ist mehrfach aufrufbar, auch aus einem Pressed-Handler heraus.</para>
/// <para>Läuft gerade ein Pressed-Handler, wartet Dispose (höchstens 5 s) auf ihn. Ein Handler sollte deshalb nicht
/// synchron auf den UI-Thread warten (Dispatcher.BeginInvoke/InvokeAsync statt Invoke) – sonst stehen beide, wenn der
/// UI-Thread gleichzeitig Dispose aufruft.</para>
/// <para>Kürzel mit Strg+Alt (ohne Win) lassen AltGr-Zeichen durch, siehe <see cref="AltGr"/>.</para>
/// </remarks>
public sealed class GlobalHotkey : IGlobalHotkey
{
    internal const int HotkeyId = 0x4C55; // „LU“
    internal const uint WM_STOP = Native.WM_APP + 0x4C;
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly Hotkey _hotkey;
    private readonly HotkeyApi _api;
    private readonly Lock _gate = new();
    private readonly ManualResetEventSlim _started = new();
    private readonly Thread? _thread;
    private volatile uint _threadId;
    private nint _window;
    private volatile bool _registered;
    private volatile string? _error;
    private volatile bool _disposed;

    public GlobalHotkey(Hotkey hotkey) : this(hotkey, HotkeyApi.System) { }

    internal GlobalHotkey(Hotkey hotkey, HotkeyApi api)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        ArgumentNullException.ThrowIfNull(api);
        _hotkey = hotkey;
        _api = api;

        if (HotkeyText.Problem(hotkey) is { } problem)
        {
            _error = problem;
            return;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "Lupe-Tastenkürzel " + HotkeyText.Format(hotkey) };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_started.Wait(StartTimeout))
        {
            lock (_gate)
            {
                // Der Thread meldet ein später doch noch angemeldetes Kürzel selbst wieder ab (siehe Run).
                _disposed = true;
                _registered = false;
                _error = string.Format(Strings.Win_Hotkey_RegisterTimeout, HotkeyText.Format(hotkey));
            }
        }
    }

    /// <summary>Wird auf einem Hintergrund-Thread ausgelöst.</summary>
    public event EventHandler? Pressed;

    public bool IsRegistered => _registered;

    /// <summary>Warum die Anmeldung scheiterte (z. B. Kürzel von einem anderen Programm belegt), sonst null.</summary>
    public string? Error => _error;

    /// <summary>Das Nachrichtenfenster (für Tests), 0 wenn keins (mehr) existiert.</summary>
    internal nint WindowHandle => _window;

    /// <summary>Der Thread mit der Nachrichtenschleife (für Tests), null wenn keiner gestartet wurde.</summary>
    internal Thread? WorkerThread => _thread;

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _registered = false;
        }
        var thread = _thread;
        if (thread is null || !thread.IsAlive) return;
        uint threadId = _threadId;
        if (threadId != 0) Native.PostThreadMessageW(threadId, WM_STOP, 0, 0);
        // Aus einem Pressed-Handler heraus nicht auf sich selbst warten: die Schleife endet, sobald der Handler zurückkehrt.
        if (Thread.CurrentThread != thread) thread.Join(StopTimeout);
    }

    private unsafe void Run()
    {
        bool registeredWithWindows = false;
        try
        {
            Native.MSG msg;
            _threadId = Native.GetCurrentThreadId();
            Native.PeekMessageW(&msg, 0, 0, 0, Native.PM_NOREMOVE); // legt die Nachrichtenwarteschlange an
            // Reines Nachrichtenfenster („Message“ ist eine Systemklasse). Scheitert es, geht WM_HOTKEY an den Thread.
            _window = Native.CreateWindowExW(0, "Message", null, 0, 0, 0, 0, 0, Native.HWND_MESSAGE, 0, 0, 0);

            int error = _api.Register(_window, HotkeyId, Modifiers(_hotkey), (uint)_hotkey.Key);
            registeredWithWindows = error == 0;
            bool keep;
            lock (_gate)
            {
                keep = registeredWithWindows && !_disposed;
                _registered = keep;
                // Nach einer Zeitüberschreitung im Konstruktor (_disposed) bleibt dessen Meldung stehen.
                if (!registeredWithWindows && !_disposed) _error = Describe(error, _hotkey);
            }
            _started.Set();
            if (!keep) return;

            while (!_disposed)
            {
                int result = Native.GetMessageW(&msg, 0, 0, 0);
                if (result is 0 or -1 || msg.message == WM_STOP) break;
                if (msg.message == Native.WM_HOTKEY && msg.wParam == HotkeyId)
                {
                    OnHotkey();
                    continue;
                }
                Native.TranslateMessage(&msg);
                Native.DispatchMessageW(&msg);
            }
        }
        finally
        {
            if (registeredWithWindows) _api.Unregister(_window, HotkeyId);
            _registered = false;
            if (_window != 0)
            {
                Native.DestroyWindow(_window);
                _window = 0;
            }
            _started.Set();
        }
    }

    private void OnHotkey()
    {
        if (_disposed) return;
        if (AltGr.Applies(_hotkey) && _api.AltGrText(_hotkey) is { Length: > 0 } text)
        {
            _api.Retype(text);
            return;
        }
        Pressed?.Invoke(this, EventArgs.Empty);
    }

    internal static uint Modifiers(Hotkey hotkey)
        => (hotkey.Alt ? Native.MOD_ALT : 0)
         | (hotkey.Ctrl ? Native.MOD_CONTROL : 0)
         | (hotkey.Shift ? Native.MOD_SHIFT : 0)
         | (hotkey.Win ? Native.MOD_WIN : 0)
         | Native.MOD_NOREPEAT;

    /// <summary>Deutsche Meldung zu einem Fehler von RegisterHotKey.</summary>
    internal static string Describe(int win32Error, Hotkey hotkey)
    {
        string text = HotkeyText.Format(hotkey);
        return win32Error switch
        {
            Native.ERROR_HOTKEY_ALREADY_REGISTERED => string.Format(Strings.Win_Hotkey_AlreadyRegistered, text),
            Native.ERROR_REQUIRES_INTERACTIVE_WINDOWSTATION =>
                string.Format(Strings.Win_Hotkey_NoInteractiveDesktop, text),
            Native.ERROR_INVALID_PARAMETER => string.Format(Strings.Win_Hotkey_Invalid, text),
            <= 0 => string.Format(Strings.Win_Hotkey_RegisterFailed, text), // Windows nannte keinen Grund
            _ => string.Format(Strings.Win_Hotkey_RegisterFailedReason, text, new Win32Exception(win32Error).Message),
        };
    }
}

/// <summary>Die Windows-Aufrufe hinter <see cref="GlobalHotkey"/> – austauschbar, damit sich Thread, Fenster und Abmeldung ohne Desktop testen lassen.</summary>
internal sealed class HotkeyApi
{
    /// <summary>Meldet an; liefert 0 oder den Win32-Fehler.</summary>
    public required Func<nint, int, uint, uint, int> Register { get; init; }
    public required Action<nint, int> Unregister { get; init; }
    /// <summary>Das gerade per AltGr getippte Zeichen (dann kein Tastenkürzel), sonst null.</summary>
    public Func<Hotkey, string?> AltGrText { get; init; } = _ => null;
    public Action<string> Retype { get; init; } = _ => { };

    public static HotkeyApi System { get; } = new()
    {
        Register = (window, id, modifiers, vk) =>
        {
            if (Native.RegisterHotKey(window, id, modifiers, vk)) return 0;
            int error = Marshal.GetLastPInvokeError();
            return error != 0 ? error : -1;
        },
        Unregister = (window, id) => Native.UnregisterHotKey(window, id),
        AltGrText = AltGr.TextIfHeld,
        Retype = AltGr.Retype,
    };
}
