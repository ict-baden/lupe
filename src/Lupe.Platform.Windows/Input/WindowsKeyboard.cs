using System.Runtime.InteropServices;
using Lupe.Core.Input;
using Lupe.Core.Platform;

namespace Lupe.Platform.Windows.Input;

/// <summary>Die echte Tastatur unter Windows: SendInput, GetAsyncKeyState, hochauflösender Wartezeitgeber, UIPI-Prüfung.</summary>
public sealed unsafe class WindowsKeyboard : IKeyboardOutput
{
    public static WindowsKeyboard Instance { get; } = new();

    private WindowsKeyboard() { }

    public int Send(ReadOnlySpan<KeyEvent> events, out int errorCode)
    {
        if (events.IsEmpty)
        {
            errorCode = 0;
            return 0;
        }
        var inputs = new Native.INPUT[events.Length];
        for (int i = 0; i < events.Length; i++) inputs[i] = ToInput(events[i]);
        return Send(inputs, out errorCode);
    }

    /// <summary>Schickt fertige INPUT-Strukturen an SendInput.</summary>
    internal static int Send(ReadOnlySpan<Native.INPUT> inputs, out int win32Error)
    {
        if (inputs.IsEmpty)
        {
            win32Error = 0;
            return 0;
        }
        uint sent;
        fixed (Native.INPUT* p = inputs)
            sent = Native.SendInput((uint)inputs.Length, p, sizeof(Native.INPUT));
        win32Error = sent == inputs.Length ? 0 : Marshal.GetLastPInvokeError();
        return (int)sent;
    }

    /// <summary>Ein Ereignis als INPUT für SendInput (die Flags haben dieselben Werte wie KEYEVENTF_*).</summary>
    internal static Native.INPUT ToInput(KeyEvent e) => new()
    {
        type = Native.INPUT_KEYBOARD,
        u = new Native.INPUTUNION
        {
            ki = new Native.KEYBDINPUT { wVk = e.VirtualKey, wScan = e.Scan, dwFlags = (uint)e.Flags },
        },
    };

    /// <summary>Warum SendInput gar nichts angenommen hat.</summary>
    public string DescribeRejection(int errorCode) => Rejection(errorCode);

    internal static string Rejection(int errorCode) => errorCode switch
    {
        Native.ERROR_ACCESS_DENIED =>
            "Eintippen nicht möglich – Windows nimmt gerade keine Tastatureingaben an (gesperrter Bildschirm, Anmeldedialog oder keine sichtbare Sitzung).",
        // SendInput liefert 0 ohne Fehlercode, wenn ein anderes Programm die Eingabe gerade sperrt (BlockInput).
        0 => "Eintippen nicht möglich – Windows hat die Tastatureingabe nicht angenommen (gerade von einem anderen Programm gesperrt?).",
        _ => $"Eintippen nicht möglich (Windows-Fehler {errorCode}).",
    };

    public bool IsDown(int virtualKey) => Native.GetAsyncKeyState(virtualKey) < 0;

    /// <summary>
    /// Wartet genau: Gewöhnliche Wartezeiten teilt Windows in 15,6-ms-Schritte ein (aus 20 ms Zeichenpause wurden 31 ms),
    /// und einen feineren Takt per timeBeginPeriod gewährt Windows 11 Prozessen mit minimiertem Fenster nicht verlässlich –
    /// genau dann tippt Lupe aber als Handscanner. Ein hochauflösender Wartezeitgeber hält die Zeit unabhängig davon.
    /// </summary>
    public void Pause(int milliseconds, CancellationToken ct)
    {
        if (milliseconds <= 0) return;
        nint timer = Native.CreateWaitableTimerExW(0, null, Native.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, Native.TIMER_ALL_ACCESS);
        if (timer == 0)
        {
            ct.WaitHandle.WaitOne(milliseconds);
            return;
        }
        try
        {
            long due = -10_000L * milliseconds; // negativ = relativ, in 100-ns-Schritten
            if (!Native.SetWaitableTimer(timer, &due, 0, 0, 0, false))
            {
                ct.WaitHandle.WaitOne(milliseconds);
                return;
            }
            using var wait = new BorrowedWaitHandle(timer);
            WaitHandle.WaitAny([wait, ct.WaitHandle]);
        }
        finally
        {
            Native.CloseHandle(timer);
        }
    }

    /// <summary>Ein fremdes Windows-Handle als WaitHandle (gibt es nicht frei).</summary>
    private sealed class BorrowedWaitHandle : WaitHandle
    {
        public BorrowedWaitHandle(nint handle) => SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(handle, ownsHandle: false);
    }

    public string? ForegroundProblem() => Elevation.ForegroundProblem();
}
