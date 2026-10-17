namespace Lupe.Platform.Linux.Input;

/// <summary>
/// Umrechnung zwischen Zeichen, X11-Keysyms und Windows-Virtual-Key-Codes (dem gemeinsamen Tastenvokabular von Lupe).
/// Reine Tabellen, ohne X-Server testbar.
/// </summary>
internal static class Keysyms
{
    public const uint NoSymbol = 0;
    public const uint Return = 0xFF0D, Tab = 0xFF09, BackSpace = 0xFF08, Escape = 0xFF1B;
    public const uint ShiftL = 0xFFE1, ShiftR = 0xFFE2, ControlL = 0xFFE3, ControlR = 0xFFE4;
    public const uint AltL = 0xFFE9, AltR = 0xFFEA, SuperL = 0xFFEB, SuperR = 0xFFEC, IsoLevel3Shift = 0xFE03;

    /// <summary>Keysym für ein Unicode-Zeichen (Code Point): Latin-1 direkt, sonst 0x01000000 + Code Point.</summary>
    public static uint FromCodePoint(int codePoint)
    {
        if (codePoint is >= 0x20 and <= 0x7E or >= 0xA0 and <= 0xFF) return (uint)codePoint;
        return codePoint switch
        {
            '\n' or '\r' => Return,
            '\t' => Tab,
            '\b' => BackSpace,
            0x1B => Escape,
            0x20AC => 0x20AC, // EuroSign (alte Keysym, aber so in jedem Layout)
            _ when codePoint is > 0xFF and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF) => 0x01000000u | (uint)codePoint,
            _ => NoSymbol,
        };
    }

    /// <summary>Tottasten: Keysym → das Zeichen, das sie allein schreiben.</summary>
    private static readonly Dictionary<uint, char> DeadKeys = new()
    {
        [0xFE50] = '`', [0xFE51] = '´', [0xFE52] = '^', [0xFE53] = '~', [0xFE54] = '¯', [0xFE55] = '˘',
        [0xFE56] = '˙', [0xFE57] = '¨', [0xFE58] = '˚', [0xFE59] = '˝', [0xFE5A] = 'ˇ', [0xFE5B] = '¸', [0xFE5C] = '˛',
    };

    /// <summary>Häufige alte Keysyms außerhalb von Latin-1 (AltGr-Ebene deutscher, polnischer … Layouts).</summary>
    private static readonly Dictionary<uint, char> Legacy = new()
    {
        [0x20AC] = '€', [0x1A3] = 'Ł', [0x1B3] = 'ł', [0x1A1] = 'Ą', [0x1B1] = 'ą', [0x1AF] = 'Ż', [0x1BF] = 'ż',
        [0xAFD] = '‚', [0xAFE] = '„', [0xAD2] = '“', [0xAD3] = '”', [0xAD0] = '‘', [0xAD1] = '’', [0xAA9] = '—', [0xAAA] = '–',
        [0xAAE] = '…', [0xAF1] = '†', [0xAC9] = '™', [0x8FB] = '←', [0x8FC] = '↑', [0x8FD] = '→', [0x8FE] = '↓',
        [0x2B1] = 'ħ', [0x3BF] = 'ŋ',
    };

    /// <summary>Das Zeichen einer Keysym; null, wenn sie kein (bekanntes) Zeichen schreibt.</summary>
    public static char? ToChar(uint keysym)
    {
        if (keysym is >= 0x20 and <= 0x7E or >= 0xA0 and <= 0xFF) return (char)keysym;
        if (keysym is >= 0x01000100 and <= 0x0100FFFF)
        {
            int cp = (int)(keysym - 0x01000000);
            return cp is >= 0xD800 and <= 0xDFFF ? null : (char)cp;
        }
        if (DeadKeys.TryGetValue(keysym, out char dead)) return dead;
        if (Legacy.TryGetValue(keysym, out char legacy)) return legacy;
        return null;
    }

    /// <summary>
    /// Feste Tasten (Lage auf der Tastatur): Windows-OEM-Codes nach der US-Lage → X-Tastencode (evdev + 8). Unter
    /// Windows hängen diese Codes vom Layout ab; unter Linux zählt die Lage, das Zeichen liefert das aktuelle Layout.
    /// </summary>
    internal static readonly IReadOnlyDictionary<int, byte> OemKeycodes = new Dictionary<int, byte>
    {
        [0xBA] = 47, // VK_OEM_1      – AC10 (US ;, DE Ö)
        [0xBB] = 21, // VK_OEM_PLUS   – AE12 (US =, DE ´)
        [0xBC] = 59, // VK_OEM_COMMA  – AB08
        [0xBD] = 20, // VK_OEM_MINUS  – AE11 (US -, DE ß)
        [0xBE] = 60, // VK_OEM_PERIOD – AB09
        [0xBF] = 61, // VK_OEM_2      – AB10 (US /, DE -)
        [0xC0] = 49, // VK_OEM_3      – TLDE (US `, DE ^)
        [0xDB] = 34, // VK_OEM_4      – AD11 (US [, DE Ü)
        [0xDC] = 51, // VK_OEM_5      – BKSL (US \, DE #)
        [0xDD] = 35, // VK_OEM_6      – AD12 (US ], DE +)
        [0xDE] = 48, // VK_OEM_7      – AC11 (US ', DE Ä)
        [0xE2] = 94, // VK_OEM_102    – LSGT (DE <)
    };

    /// <summary>Feste Keysym einer Virtual-Key (unabhängig vom Layout); NoSymbol für OEM-Tasten und Unbekanntes.</summary>
    public static uint FromVirtualKey(int vk) => vk switch
    {
        >= 0x41 and <= 0x5A => (uint)(vk + 0x20),       // A–Z → a–z
        >= 0x30 and <= 0x39 => (uint)vk,                // 0–9
        >= 0x70 and <= 0x87 => (uint)(0xFFBE + vk - 0x70), // F1–F24
        >= 0x60 and <= 0x69 => (uint)(0xFFB0 + vk - 0x60), // Ziffernblock 0–9
        0x6A => 0xFFAA, 0x6B => 0xFFAB, 0x6C => 0xFFAC, 0x6D => 0xFFAD, 0x6E => 0xFFAE, 0x6F => 0xFFAF,
        0x20 => 0x20,
        0x08 => BackSpace, 0x09 => Tab, 0x0D => Return, 0x1B => Escape,
        0x13 => 0xFF13, 0x14 => 0xFFE5, 0x90 => 0xFF7F, 0x91 => 0xFF14, 0x2C => 0xFF61,
        0x21 => 0xFF55, 0x22 => 0xFF56, 0x23 => 0xFF57, 0x24 => 0xFF50,
        0x25 => 0xFF51, 0x26 => 0xFF52, 0x27 => 0xFF53, 0x28 => 0xFF54,
        0x2D => 0xFF63, 0x2E => 0xFFFF, 0x5D => 0xFF67,
        0x10 or 0xA0 => ShiftL, 0xA1 => ShiftR,
        0x11 or 0xA2 => ControlL, 0xA3 => ControlR,
        0x12 or 0xA4 => AltL, 0xA5 => AltR,
        0x5B => SuperL, 0x5C => SuperR,
        _ => NoSymbol,
    };

    /// <summary>Die Virtual-Key einer Keysym ohne Layoutbezug (Buchstaben, Ziffern, F-Tasten …); null sonst.</summary>
    public static int? ToVirtualKey(uint keysym)
    {
        if (keysym is >= 'a' and <= 'z') return (int)keysym - 0x20;
        if (keysym is >= 'A' and <= 'Z') return (int)keysym;
        if (keysym is >= '0' and <= '9') return (int)keysym;
        for (int vk = 1; vk < 0xFF; vk++)
            if (vk is not (0x10 or 0x11 or 0x12) && FromVirtualKey(vk) == keysym) return vk;
        return null;
    }

    /// <summary>Fügt UTF-16-Hälften wieder zu einem Code Point (Core schickt Emoji als Surrogatpaar).</summary>
    public static int JoinSurrogates(char high, char low) => char.ConvertToUtf32(high, low);
}
