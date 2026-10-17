using Lupe.Core.Input;
using Lupe.Core.Model;

namespace Lupe.Platform.Windows.Input;

/// <summary>
/// Schutz für Tastenkürzel mit Strg+Alt: Windows meldet AltGr als Strg+Alt, ein global belegtes Strg+Alt+Q schluckt
/// auf deutscher Tastatur also AltGr+Q = „@“ (ebenso Strg+Alt+E = „€“ …). Kommt das Kürzel, während die rechte Alt-Taste
/// (AltGr) gedrückt ist, und kennt das Tastaturlayout des Vordergrundfensters dafür ein Zeichen, wird dieses Zeichen
/// nachgetippt statt das Kürzel auszulösen. Mit der linken Alt-Taste funktioniert das Kürzel wie gewohnt.
/// </summary>
internal static unsafe class AltGr
{
    /// <summary>Das Zeichen, das der Benutzer gerade mit AltGr tippen wollte – null, wenn es ein echtes Tastenkürzel ist.</summary>
    public static string? TextIfHeld(Hotkey hotkey)
    {
        if (!Applies(hotkey)) return null;
        bool rightAlt = Native.GetAsyncKeyState(KeyboardTyper.VK_RMENU) < 0;
        bool leftAlt = Native.GetAsyncKeyState(KeyboardTyper.VK_LMENU) < 0;
        if (!rightAlt || leftAlt) return null;
        return Text(hotkey.Key, hotkey.Shift, ForegroundLayout());
    }

    /// <summary>Nur Kürzel mit Strg und Alt (ohne Win) kollidieren mit AltGr.</summary>
    public static bool Applies(Hotkey hotkey) => HotkeyText.AltGrApplies(hotkey);

    /// <summary>Tippt das von Windows geschluckte AltGr-Zeichen nach (Unicode, ohne auf das Loslassen zu warten).</summary>
    public static void Retype(string text)
    {
        var inputs = KeyboardTyper.BuildInputs(text, null);
        WindowsKeyboard.Instance.Send(inputs, out _);
    }

    /// <summary>Tastaturlayout des Fensters, in das gerade getippt wird (jeder Thread kann ein eigenes haben).</summary>
    public static nint ForegroundLayout()
    {
        nint window = Native.GetForegroundWindow();
        uint thread = window != 0 ? Native.GetWindowThreadProcessId(window, out _) : 0;
        return Native.GetKeyboardLayout(thread);
    }

    /// <summary>Was AltGr (+ Umschalt) + Taste im Layout schreibt; null bei keinem Zeichen, Steuerzeichen oder Tottaste.</summary>
    internal static string? Text(int vk, bool shift, nint layout)
    {
        if (vk is < 1 or > 0xFE) return null;
        byte* state = stackalloc byte[256];
        new Span<byte>(state, 256).Clear();
        state[KeyboardTyper.VK_CONTROL] = 0x80;
        state[KeyboardTyper.VK_LCONTROL] = 0x80;
        state[KeyboardTyper.VK_MENU] = 0x80;
        state[KeyboardTyper.VK_RMENU] = 0x80;
        if (shift)
        {
            state[KeyboardTyper.VK_SHIFT] = 0x80;
            state[KeyboardTyper.VK_LSHIFT] = 0x80;
        }
        uint scan = Native.MapVirtualKeyExW((uint)vk, Native.MAPVK_VK_TO_VSC, layout);
        char* buffer = stackalloc char[8];
        int count = Native.ToUnicodeEx((uint)vk, scan, state, buffer, 8, Native.TOUNICODE_NO_STATE_CHANGE, layout);
        if (count <= 0) return null; // nichts oder Tottaste
        var text = new string(buffer, 0, Math.Min(count, 8));
        foreach (char c in text)
            if (char.IsControl(c)) return null; // Strg+Buchstabe ergibt Steuerzeichen, das ist kein AltGr-Zeichen
        return text;
    }
}
