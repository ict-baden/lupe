using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Lupe.Tests.Input;

/// <summary>
/// Test, der echte Tastatureingaben oder Tastenkürzel braucht (SendInput, RegisterHotKey). Das geht nur in der
/// sichtbaren Fensterstation „WinSta0“ auf dem gerade aktiven Desktop – in Sitzung 0 (Dienste) wird übersprungen.
/// </summary>
public sealed class InteractiveFactAttribute : FactAttribute
{
    public InteractiveFactAttribute()
    {
        if (InteractiveDesktop.SkipReason is { } reason) Skip = reason;
    }
}

/// <summary>Test für das Verhalten ohne sichtbaren Desktop (läuft nur dort, wo <see cref="InteractiveFactAttribute"/> überspringt).</summary>
public sealed class NonInteractiveFactAttribute : FactAttribute
{
    public NonInteractiveFactAttribute()
    {
        if (InteractiveDesktop.SkipReason is null)
            Skip = "Nur ohne sichtbaren Desktop – hier würde wirklich in das Vordergrundfenster getippt.";
    }
}

/// <summary>Test, der das deutsche Tastaturlayout braucht (AltGr+Q = @).</summary>
public sealed class GermanKeyboardFactAttribute : FactAttribute
{
    public GermanKeyboardFactAttribute()
    {
        if (!InteractiveDesktop.IsGermanLayout(InteractiveDesktop.ThreadLayout))
            Skip = $"Braucht das deutsche Tastaturlayout (aktiv: 0x{InteractiveDesktop.ThreadLayout:X8}).";
    }
}

internal static class InteractiveDesktop
{
    private const int UOI_FLAGS = 1;
    private const int UOI_NAME = 2;
    private const uint WSF_VISIBLE = 0x0001;
    private const uint DESKTOP_READOBJECTS = 0x0001;

    private static readonly Lazy<string?> Reason = new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>null, wenn dieser Prozess Eingaben senden und Tastenkürzel anmelden kann, sonst der Grund.</summary>
    public static string? SkipReason => Reason.Value;

    public static nint ThreadLayout => GetKeyboardLayout(0);

    /// <summary>Hohes Wort des HKL = Layout-Kennung; 0x0407 = Deutsch (Deutschland).</summary>
    public static bool IsGermanLayout(nint layout) => (((long)layout >> 16) & 0xFFFF) == 0x0407;

    private static string? Probe()
    {
        int session = Process.GetCurrentProcess().SessionId;
        nint station = GetProcessWindowStation();
        string stationName = Name(station);
        var flags = new byte[12]; // USEROBJECTFLAGS: fInherit, fReserved, dwFlags
        if (!GetUserObjectInformationW(station, UOI_FLAGS, flags, flags.Length, out _)
            || (BitConverter.ToUInt32(flags, 8) & WSF_VISIBLE) == 0)
        {
            return $"Kein sichtbarer Desktop: Fensterstation „{stationName}“ in Sitzung {session} nimmt keine Tastatureingaben "
                + "und Tastenkürzel an (SendInput: Zugriff verweigert, RegisterHotKey: Fehler 1459).";
        }
        nint input = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
        if (input == 0)
            return $"Der aktive Desktop ist nicht zugänglich (gesperrt oder Anmeldebildschirm, Fehler {Marshal.GetLastWin32Error()}).";
        try
        {
            string inputName = Name(input), own = Name(GetThreadDesktop(GetCurrentThreadId()));
            if (!string.Equals(inputName, own, StringComparison.OrdinalIgnoreCase))
                return $"Aktiv ist der Desktop „{inputName}“, dieser Prozess läuft auf „{own}“.";
        }
        finally
        {
            CloseDesktop(input);
        }
        return null;
    }

    private static string Name(nint handle)
    {
        var buffer = new byte[512];
        if (!GetUserObjectInformationW(handle, UOI_NAME, buffer, buffer.Length, out int needed)) return "?";
        return Encoding.Unicode.GetString(buffer, 0, Math.Clamp(needed, 0, buffer.Length)).TrimEnd('\0');
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetProcessWindowStation();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetThreadDesktop(uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetUserObjectInformationW(nint handle, int index, byte[] info, int length, out int needed);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);

    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(nint desktop);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint threadId);
}
