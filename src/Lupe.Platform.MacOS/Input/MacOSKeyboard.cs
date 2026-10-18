using System.Diagnostics;
using Lupe.Core.Input;
using Lupe.Core.Platform;
using Lupe.Core.Resources;
using Lupe.Platform.MacOS.Native;

namespace Lupe.Platform.MacOS.Input;

/// <summary>
/// Tippen unter macOS mit CGEvents: Jedes Zeichen geht als Tastendruck auf Taste 0 mit
/// <c>CGEventKeyboardSetUnicodeString</c> hinaus – unabhängig vom Tastaturlayout, auch Umlaute und Emoji. Eingabe, Tab
/// und das Loslassen von Umschalttasten sind echte Tasten. Die Umschalt-Flags jedes Ereignisses werden auf 0 gesetzt,
/// damit eine noch gedrückte Befehlstaste kein Zeichen in einen Menübefehl verwandelt.
/// </summary>
/// <remarks>macOS stellt Ereignisse nur zu, wenn Lupe unter „Bedienungshilfen“ freigegeben ist (sonst verwirft es sie stillschweigend).</remarks>
public sealed class MacOSKeyboard : IKeyboardOutput
{
    internal const int ErrorNotMac = -1;
    internal const int ErrorNotTrusted = -2;
    internal const int ErrorCreateFailed = -3;

    private readonly Lock _gate = new();
    private nint _source;

    public int Send(ReadOnlySpan<KeyEvent> events, out int errorCode)
    {
        errorCode = 0;
        if (events.IsEmpty) return 0;
        if (!OperatingSystem.IsMacOS())
        {
            errorCode = ErrorNotMac;
            return 0;
        }
        if (!MacOSPermissions.Accessibility())
        {
            errorCode = ErrorNotTrusted;
            return 0;
        }

        var strokes = MacKeyPlan.Build(events);
        int accepted = 0;
        lock (_gate)
        {
            if (_source == 0) _source = CG.CGEventSourceCreate(CG.kCGEventSourceStateHIDSystemState); // 0 ist erlaubt (Standardquelle)
            foreach (var stroke in strokes)
            {
                if (!Post(stroke))
                {
                    errorCode = ErrorCreateFailed;
                    return accepted;
                }
                accepted = stroke.Consumed;
            }
        }
        return events.Length; // übersprungene Ereignisse (Maskentaste, unbekannte Tasten) gelten als angenommen
    }

    private unsafe bool Post(MacStroke stroke)
    {
        nint evt = CG.CGEventCreateKeyboardEvent(_source, stroke.KeyCode, stroke.Down ? (byte)1 : (byte)0);
        if (evt == 0) return false;
        try
        {
            if (stroke.Text is { } text)
                fixed (char* p = text)
                    CG.CGEventKeyboardSetUnicodeString(evt, (nuint)text.Length, p);
            CG.CGEventSetFlags(evt, 0);
            CG.CGEventPost(CG.kCGHIDEventTap, evt);
            return true;
        }
        finally
        {
            CF.CFRelease(evt);
        }
    }

    public bool IsDown(int virtualKey)
    {
        if (!OperatingSystem.IsMacOS()) return false;
        try
        {
            foreach (var code in MacKeyCodes.ForKeyState(virtualKey))
                if (CG.CGEventSourceKeyState(CG.kCGEventSourceStateHIDSystemState, code) != 0) return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Trace.TraceWarning($"Lupe: Tastenzustand nicht abfragbar: {ex.Message}");
        }
        return false;
    }

    public void Pause(int milliseconds, CancellationToken ct)
    {
        if (milliseconds <= 0) return;
        // Unter macOS sind die Zeitgeber fein genug (unter 1 ms Abweichung) – kein eigener Hochpräzisions-Timer nötig
        ct.WaitHandle.WaitOne(milliseconds);
    }

    public string? ForegroundProblem()
    {
        if (!OperatingSystem.IsMacOS()) return Strings.Mac_NotHere_Typing;
        if (MacOSPermissions.Accessibility()) return null;
        // Beim ersten Versuch den Systemdialog zeigen – er führt direkt zur richtigen Einstellung
        MacOSPermissions.RequestAccessibility();
        return MacOSPermissions.AccessibilityDenied;
    }

    public string DescribeRejection(int errorCode) => errorCode switch
    {
        ErrorNotMac => Strings.Mac_NotHere_Typing,
        ErrorNotTrusted => MacOSPermissions.AccessibilityDenied,
        _ => Strings.Mac_Typing_Rejected,
    };
}
