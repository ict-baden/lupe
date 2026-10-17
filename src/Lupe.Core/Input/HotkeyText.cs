using System.Globalization;
using System.Text;
using Lupe.Core.Model;
using Lupe.Core.Platform;

namespace Lupe.Core.Input;

/// <summary>Anzeige und Eingabe von Tastenkürzeln wie „Strg+Alt+Q“.</summary>
/// <remarks>
/// <para><see cref="Format"/> schreibt die Umschalttasten in der Reihenfolge Win, Strg, Umschalt, Alt und die Taste mit
/// deutschem Namen (Druck, Pause, Einfg, Entf, Pos1, Ende, Bild auf, Leertaste, Num 5, F1–F24 …). Unbekannte Tasten
/// erscheinen als „Taste 0xNN“, sodass jeder Tastencode wieder eingelesen werden kann.</para>
/// <para><see cref="TryParse"/> ist tolerant: deutsche und englische Namen (Strg/Ctrl, Umschalt/Shift, Win/Windows,
/// Druck/PrintScreen, Einfg/Insert …), Groß/Klein und Leerzeichen sind egal, die Reihenfolge der Umschalttasten auch.
/// „Strg++“ meint die Plus-Taste. Genau eine Taste ist nötig.</para>
/// </remarks>
public static class HotkeyText
{
    private const int VK_SNAPSHOT = 0x2C;
    private const int VK_PAUSE = 0x13;
    private const int VK_SCROLL = 0x91;
    private const int VK_F1 = 0x70;
    private const int VK_F24 = 0x87;
    private const int VK_NUMPAD0 = 0x60;

    /// <summary>Tasten mit festem Namen: erster Name zur Anzeige, die übrigen werden beim Einlesen auch erkannt.</summary>
    internal static readonly (int Vk, string[] Names)[] NamedKeys =
    [
        (0x08, ["Rücktaste", "Backspace", "Back", "Rück", "Ruecktaste"]),
        (0x09, ["Tab", "Tabulator"]),
        (0x0D, ["Eingabe", "Enter", "Return", "Eingabetaste"]),
        (VK_PAUSE, ["Pause", "Break", "Untbr", "Unterbrechen"]),
        (0x14, ["Feststell", "CapsLock", "Caps", "Feststelltaste"]),
        (0x1B, ["Esc", "Escape"]),
        (0x20, ["Leertaste", "Space", "Leer", "Leerzeichen", "Spacebar"]),
        (0x21, ["Bild auf", "PageUp", "PgUp", "Prior", "Bild hoch", "Bild↑"]),
        (0x22, ["Bild ab", "PageDown", "PgDn", "PgDown", "Next", "Bild runter", "Bild↓"]),
        (0x23, ["Ende", "End"]),
        (0x24, ["Pos1", "Home"]),
        (0x25, ["Pfeil links", "Left", "Links", "←"]),
        (0x26, ["Pfeil oben", "Up", "Oben", "Hoch", "↑"]),
        (0x27, ["Pfeil rechts", "Right", "Rechts", "→"]),
        (0x28, ["Pfeil unten", "Down", "Unten", "Runter", "↓"]),
        (VK_SNAPSHOT, ["Druck", "PrintScreen", "PrtSc", "PrtScn", "PrtScr", "Print", "PrintScrn", "Drucken", "Snapshot"]),
        (0x2D, ["Einfg", "Insert", "Ins", "Einfügen", "Einfuegen"]),
        (0x2E, ["Entf", "Delete", "Del", "Entfernen"]),
        (0x5D, ["Menü", "Menu", "Apps", "Menue", "Kontextmenü", "Anwendung"]),
        (0x6A, ["Num Mal", "Num*", "NumMultiply", "Multiply"]),
        (0x6B, ["Num Plus", "NumAdd", "Add"]),
        (0x6D, ["Num Minus", "Num-", "NumSubtract", "Subtract"]),
        (0x6E, ["Num Komma", "Num,", "Num.", "NumDecimal", "Decimal", "Num Punkt"]),
        (0x6F, ["Num Geteilt", "Num/", "NumDivide", "Divide", "Num Durch"]),
        (0x90, ["NumLock", "Num-Feststell", "Num Feststell"]),
        (VK_SCROLL, ["Rollen", "ScrollLock", "Scroll", "Rollen-Feststell"]),
        (0x5F, ["Standby", "Sleep", "Energiesparmodus"]),
        // Browser-, Medien- und Starttasten (gehen auch ohne Strg/Alt/Win, siehe Problem)
        (0xA6, ["Browser zurück", "BrowserBack"]),
        (0xA7, ["Browser vor", "BrowserForward"]),
        (0xA8, ["Browser neu laden", "BrowserRefresh"]),
        (0xA9, ["Browser Stopp", "BrowserStop"]),
        (0xAA, ["Browser Suche", "BrowserSearch"]),
        (0xAB, ["Browser Favoriten", "BrowserFavorites"]),
        (0xAC, ["Browser Startseite", "BrowserHome"]),
        (0xAD, ["Stumm", "VolumeMute", "Mute"]),
        (0xAE, ["Leiser", "VolumeDown"]),
        (0xAF, ["Lauter", "VolumeUp"]),
        (0xB0, ["Nächster Titel", "MediaNextTrack", "NextTrack"]),
        (0xB1, ["Vorheriger Titel", "MediaPrevTrack", "PrevTrack", "PreviousTrack"]),
        (0xB2, ["Stopp", "MediaStop"]),
        (0xB3, ["Wiedergabe/Pause", "MediaPlayPause", "PlayPause"]),
        (0xB4, ["E-Mail", "LaunchMail", "Mail"]),
        (0xB5, ["Medienauswahl", "LaunchMediaSelect", "MediaSelect"]),
        (0xB6, ["Start 1", "LaunchApp1", "App1"]),
        (0xB7, ["Start 2", "LaunchApp2", "App2"]),
        (0xBB, ["Plus", "OemPlus"]),
        (0xBC, ["Komma", ",", "OemComma"]),
        (0xBD, ["Minus", "-", "OemMinus"]),
        (0xBE, ["Punkt", ".", "OemPeriod"]),
    ];

    private static readonly Dictionary<int, string> DisplayNames = NamedKeys.ToDictionary(k => k.Vk, k => k.Names[0]);

    private static readonly Dictionary<string, int> KeyAliases = BuildAliases();

    private static readonly Dictionary<string, Modifier> ModifierAliases = new(StringComparer.Ordinal)
    {
        ["strg"] = Modifier.Ctrl, ["ctrl"] = Modifier.Ctrl, ["control"] = Modifier.Ctrl, ["ctl"] = Modifier.Ctrl, ["steuerung"] = Modifier.Ctrl,
        ["alt"] = Modifier.Alt,
        ["altgr"] = Modifier.Ctrl | Modifier.Alt,
        ["umschalt"] = Modifier.Shift, ["shift"] = Modifier.Shift, ["umsch"] = Modifier.Shift, ["hochstell"] = Modifier.Shift, ["hochstelltaste"] = Modifier.Shift,
        ["win"] = Modifier.Win, ["windows"] = Modifier.Win, ["windowstaste"] = Modifier.Win, ["super"] = Modifier.Win, ["meta"] = Modifier.Win,
    };

    [Flags]
    private enum Modifier { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

    /// <summary>Lesbarer Text wie „Strg+Alt+Q“, „Strg+Umschalt+F9“ oder „Win+Alt+S“.</summary>
    public static string Format(Hotkey hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        var sb = new StringBuilder();
        if (hotkey.Win) sb.Append("Win+");
        if (hotkey.Ctrl) sb.Append("Strg+");
        if (hotkey.Shift) sb.Append("Umschalt+");
        if (hotkey.Alt) sb.Append("Alt+");
        sb.Append(KeyName(hotkey.Key));
        return sb.ToString();
    }

    /// <summary>Liest ein Tastenkürzel ein; bei <see langword="false"/> ist <paramref name="hotkey"/> null.</summary>
    public static bool TryParse(string text, out Hotkey hotkey)
    {
        hotkey = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var tokens = text.Split('+').Select(t => t.Trim()).ToList();
        // Ein „+“ am Ende ist die Plus-Taste: „Strg++“ bzw. „Strg+Num +“.
        if (tokens.Count >= 2 && tokens[^1].Length == 0)
        {
            if (tokens[^2].Length == 0)
            {
                tokens.RemoveAt(tokens.Count - 1);
                tokens[^1] = "Plus";
            }
            else if (Normalize(tokens[^2]) == "num")
            {
                tokens.RemoveAt(tokens.Count - 1);
                tokens[^1] = "Num Plus";
            }
        }

        var modifiers = Modifier.None;
        int? key = null;
        foreach (string token in tokens)
        {
            if (token.Length == 0) return false;
            string normalized = Normalize(token);
            if (ModifierAliases.TryGetValue(normalized, out var modifier))
            {
                modifiers |= modifier;
                continue;
            }
            if (key is not null || !TryParseKey(normalized, out int vk)) return false;
            key = vk;
        }
        if (key is null) return false;

        hotkey = new Hotkey(
            Ctrl: modifiers.HasFlag(Modifier.Ctrl),
            Alt: modifiers.HasFlag(Modifier.Alt),
            Shift: modifiers.HasFlag(Modifier.Shift),
            Win: modifiers.HasFlag(Modifier.Win),
            Key: key.Value);
        return true;
    }

    /// <summary>
    /// Warum sich das Kürzel nicht als globales Tastenkürzel eignet (deutsch), sonst null. Abgelehnt werden ungültige
    /// Tastencodes, Maustasten, eine Umschalttaste als Haupttaste und normale Schreibtasten ohne Strg, Alt oder Win –
    /// „Q“ oder „Umschalt+Q“ global zu belegen hieße, dass man in keinem Programm mehr Q tippen kann. Allein erlaubt sind
    /// F1–F24, Druck, Pause, Rollen sowie Browser-, Medien- und Starttasten.
    /// </summary>
    internal static string? Problem(Hotkey hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        int vk = hotkey.Key;
        if (vk is < 1 or > 0xFE) return $"Kein gültiges Tastenkürzel (Tastencode {vk}).";
        if (vk is 0x01 or 0x02 or 0x04 or 0x05 or 0x06) return "Maustasten gehen nicht als Tastenkürzel.";
        if (vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5))
            return "Strg, Alt, Umschalt und Win allein gehen nicht – bitte noch eine Taste dazunehmen.";
        if (!hotkey.Ctrl && !hotkey.Alt && !hotkey.Win && !StandsAlone(vk))
            return $"„{Format(hotkey)}“ würde das normale Tippen stören – bitte Strg, Alt oder Win dazunehmen.";
        return null;
    }

    /// <summary>
    /// Hinweis für die Einstellungen bei einem Kürzel mit Strg+Alt (ohne Win), sonst null: Windows meldet AltGr als Strg+Alt,
    /// auf deutschen Tastaturen kann das Kürzel also ein AltGr-Zeichen abfangen (Strg+Alt+Q = AltGr+Q = „@“). Lupe tippt das
    /// Zeichen in dem Fall nach (siehe <see cref="GlobalHotkey"/>), sicherer ist trotzdem ein Kürzel mit Win. Kennt das
    /// Tastaturlayout des aufrufenden Threads ein Zeichen auf der Taste, nennt der Hinweis es.
    /// </summary>
    public static string? AltGrWarning(Hotkey hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        var layout = LupePlatform.Current.KeyboardLayout;
        return layout.AltGrIsCtrlAlt ? AltGrWarning(hotkey, layout.AltGrText) : null;
    }

    /// <param name="altGrText">Was AltGr (+ Umschalt) + Taste im gewünschten Layout schreibt (siehe <see cref="IKeyboardLayout.AltGrText"/>).</param>
    internal static string? AltGrWarning(Hotkey hotkey, Func<int, bool, string?> altGrText)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        if (!AltGrApplies(hotkey)) return null;
        var text = Format(hotkey);
        var character = altGrText(hotkey.Key, hotkey.Shift);
        var detail = character is null ? "" : $" (AltGr+{(hotkey.Shift ? "Umschalt+" : "")}{KeyName(hotkey.Key)} schreibt „{character}“)";
        return $"{text} kann auf deutschen Tastaturen mit AltGr-Zeichen kollidieren{detail} – Windows meldet AltGr als Strg+Alt. "
            + "Lupe tippt das Zeichen dann nach; sicherer ist ein Kürzel mit Win, z. B. Win+Umschalt+Q.";
    }

    /// <summary>Nur Kürzel mit Strg und Alt (ohne Win) kollidieren mit AltGr (wo das System AltGr als Strg+Alt meldet).</summary>
    public static bool AltGrApplies(Hotkey hotkey) => hotkey.Ctrl && hotkey.Alt && !hotkey.Win;

    private static bool StandsAlone(int vk)
        => vk is (>= VK_F1 and <= VK_F24) or VK_SNAPSHOT or VK_PAUSE or VK_SCROLL or (>= 0xA6 and <= 0xB7);

    /// <summary>Anzeigename einer Taste (Virtual-Key-Code).</summary>
    internal static string KeyName(int vk)
    {
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= VK_F1 and <= VK_F24) return "F" + (vk - VK_F1 + 1).ToString(CultureInfo.InvariantCulture);
        if (vk is >= VK_NUMPAD0 and <= VK_NUMPAD0 + 9) return "Num " + (vk - VK_NUMPAD0).ToString(CultureInfo.InvariantCulture);
        if (DisplayNames.TryGetValue(vk, out string? name)) return name;
        if (vk is >= 0xBA and <= 0xF5 && LayoutChar(vk) is { } c) return c;
        return $"Taste 0x{vk:X2}";
    }

    private static bool TryParseKey(string normalized, out int vk)
    {
        vk = 0;
        if (normalized.Length == 1)
        {
            char c = normalized[0];
            if (c is >= 'a' and <= 'z') { vk = 'A' + (c - 'a'); return true; }
            if (c is >= '0' and <= '9') { vk = c; return true; }
        }
        if (normalized.Length is 2 or 3 && normalized[0] == 'f'
            && int.TryParse(normalized.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int f) && f is >= 1 and <= 24)
        {
            vk = VK_F1 + f - 1;
            return true;
        }
        foreach (string prefix in (ReadOnlySpan<string>)["numpad", "num"])
        {
            if (normalized.Length == prefix.Length + 1 && normalized.StartsWith(prefix, StringComparison.Ordinal) && char.IsAsciiDigit(normalized[^1]))
            {
                vk = VK_NUMPAD0 + (normalized[^1] - '0');
                return true;
            }
        }
        if (KeyAliases.TryGetValue(normalized, out vk)) return true;

        // „Taste 0xBA“ (so zeigt Format unbekannte Tasten) oder kurz „0xBA“
        var hex = normalized.AsSpan();
        if (hex.StartsWith("taste")) hex = hex[5..];
        if (hex.StartsWith("0x") && hex.Length is > 2 and <= 6
            && int.TryParse(hex[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code) && code is >= 1 and <= 0xFE)
        {
            vk = code;
            return true;
        }

        // Ein einzelnes Zeichen wie „ü“ oder „#“: über das aktuelle Tastaturlayout
        if (normalized.Length == 1 && LayoutKey(normalized[0]) is { } layoutVk)
        {
            vk = layoutVk;
            return true;
        }
        vk = 0;
        return false;
    }

    /// <summary>Kleinschreibung ohne Leerzeichen – „Bild auf“, „bildAuf“ und „BILD AUF“ werden gleich.</summary>
    private static string Normalize(string token)
    {
        var sb = new StringBuilder(token.Length);
        foreach (char c in token)
            if (!char.IsWhiteSpace(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    private static Dictionary<string, int> BuildAliases()
    {
        var aliases = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (vk, names) in NamedKeys)
            foreach (string name in names)
                aliases.TryAdd(Normalize(name), vk);
        return aliases;
    }

    /// <summary>
    /// Das Zeichen, das eine OEM-Taste im aktuellen Tastaturlayout ohne Umschalttaste schreibt (z. B. „Ü“, „#“, „&lt;“) –
    /// nur, wenn es sich eindeutig wieder dieser Taste zuordnen lässt, sonst null.
    /// </summary>
    private static string? LayoutChar(int vk)
    {
        if (LupePlatform.Current.KeyboardLayout.KeyChar(vk) is not { } c) return null;
        if (c <= ' ' || c == '+' || char.IsControl(c) || char.IsWhiteSpace(c) || char.IsSurrogate(c)) return null;
        return LayoutKey(char.ToLowerInvariant(c)) == vk ? char.ToUpperInvariant(c).ToString() : null;
    }

    /// <summary>Die Taste, die im aktuellen Tastaturlayout ohne Umschalttaste das Zeichen schreibt, sonst null.</summary>
    private static int? LayoutKey(char c)
    {
        var layout = LupePlatform.Current.KeyboardLayout;
        foreach (char candidate in (ReadOnlySpan<char>)[char.ToLowerInvariant(c), char.ToUpperInvariant(c)])
            if (layout.KeyForChar(candidate) is { } vk and >= 1 and <= 0xFE) return vk;
        return null;
    }
}
