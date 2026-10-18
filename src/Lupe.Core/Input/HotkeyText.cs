using System.Globalization;
using System.Text;
using Lupe.Core.Model;
using Lupe.Core.Platform;
using Lupe.Core.Resources;

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

    /// <summary>
    /// Tasten mit festem Namen zum Einlesen (deutsch zuerst, dann englische und weitere Schreibweisen). Angezeigt wird der
    /// Name aus den Sprachdateien (<see cref="KeyResource"/>); er wird beim Einlesen in jeder Sprache ebenfalls erkannt.
    /// </summary>
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

    private static readonly Dictionary<string, int> KeyAliases = BuildAliases();

    private static readonly Dictionary<string, Modifier> ModifierAliases = new(StringComparer.Ordinal)
    {
        ["strg"] = Modifier.Ctrl, ["ctrl"] = Modifier.Ctrl, ["control"] = Modifier.Ctrl, ["ctl"] = Modifier.Ctrl, ["steuerung"] = Modifier.Ctrl,
        ["alt"] = Modifier.Alt,
        ["altgr"] = Modifier.Ctrl | Modifier.Alt,
        ["umschalt"] = Modifier.Shift, ["shift"] = Modifier.Shift, ["umsch"] = Modifier.Shift, ["hochstell"] = Modifier.Shift, ["hochstelltaste"] = Modifier.Shift,
        ["win"] = Modifier.Win, ["windows"] = Modifier.Win, ["windowstaste"] = Modifier.Win, ["super"] = Modifier.Win, ["meta"] = Modifier.Win,
        // macOS: Wahltaste = Alt, Befehlstaste = Win (so bildet MacOSHotkeys die Tasten ab)
        ["option"] = Modifier.Alt, ["opt"] = Modifier.Alt, ["wahltaste"] = Modifier.Alt,
        ["cmd"] = Modifier.Win, ["command"] = Modifier.Win, ["befehl"] = Modifier.Win, ["befehlstaste"] = Modifier.Win,
    };

    /// <summary>Symbole der Mac-Tasten in Apples Reihenfolge: ⌃ Control (Strg), ⌥ Wahltaste (Alt), ⇧ Umschalt, ⌘ Befehl (Win).</summary>
    private static readonly (char Symbol, Modifier Modifier)[] MacSymbols =
        [('⌃', Modifier.Ctrl), ('⌥', Modifier.Alt), ('⇧', Modifier.Shift), ('⌘', Modifier.Win)];

    /// <summary>
    /// Schreibweise wie unter macOS (⌥⌘Q statt Win+Alt+Q)? Folgt dem System; die Tests schalten sie für die
    /// Windows-Schreibweise ab, damit sie auf jedem System gleich laufen.
    /// </summary>
    internal static bool MacStyle { get; set; } = OperatingSystem.IsMacOS();

    [Flags]
    private enum Modifier { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

    /// <summary>
    /// Lesbarer Text wie „Strg+Alt+Q“, „Strg+Umschalt+F9“ oder „Win+Alt+S“ – unter macOS mit den Symbolen der Mac-Tastatur
    /// wie in den Menüs: „⌥⌘Q“, „⌃⇧F9“.
    /// </summary>
    public static string Format(Hotkey hotkey) => Format(hotkey, MacStyle);

    internal static string Format(Hotkey hotkey, bool mac)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        var sb = new StringBuilder();
        if (mac)
        {
            foreach (var (symbol, modifier) in MacSymbols)
                if (Has(hotkey, modifier)) sb.Append(symbol);
            return sb.Append(KeyName(hotkey.Key)).ToString();
        }
        if (hotkey.Win) sb.Append(Strings.Core_Key_Win).Append('+');
        if (hotkey.Ctrl) sb.Append(Strings.Core_Key_Ctrl).Append('+');
        if (hotkey.Shift) sb.Append(Strings.Core_Key_Shift).Append('+');
        if (hotkey.Alt) sb.Append(Strings.Core_Key_Alt).Append('+');
        sb.Append(KeyName(hotkey.Key));
        return sb.ToString();
    }

    /// <summary>Liest ein Tastenkürzel ein; bei <see langword="false"/> ist <paramref name="hotkey"/> null.</summary>
    public static bool TryParse(string text, out Hotkey hotkey)
    {
        hotkey = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Mac-Symbole (⌥⌘Q, auch gemischt: ⌘+Umschalt+Q) stehen ohne „+“ vor der Taste
        var modifiers = Modifier.None;
        foreach (var (symbol, modifier) in MacSymbols)
        {
            if (!text.Contains(symbol, StringComparison.Ordinal)) continue;
            modifiers |= modifier;
            text = text.Replace(symbol.ToString(), "", StringComparison.Ordinal);
        }
        text = text.Trim();
        if (modifiers != Modifier.None && text.Length > 1 && text[0] == '+') text = text[1..].TrimStart(); // „⌘+Umschalt+Q“
        if (text.Length == 0) return false;

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
        if (vk is < 1 or > 0xFE) return string.Format(Strings.Core_Hotkey_InvalidKeyCode, vk);
        if (vk is 0x01 or 0x02 or 0x04 or 0x05 or 0x06) return Strings.Core_Hotkey_MouseButton;
        if (vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5))
            return Strings.Core_Hotkey_ModifierOnly;
        if (!hotkey.Ctrl && !hotkey.Alt && !hotkey.Win && !StandsAlone(vk))
            return string.Format(Strings.Core_Hotkey_DisturbsTyping, Format(hotkey));
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
        if (character is null) return string.Format(Strings.Core_Hotkey_AltGrWarning, text);
        var altGr = "AltGr+" + (hotkey.Shift ? Strings.Core_Key_Shift + "+" : "") + KeyName(hotkey.Key);
        return string.Format(Strings.Core_Hotkey_AltGrWarningCharacter, text, altGr, character);
    }

    /// <summary>Nur Kürzel mit Strg und Alt (ohne Win) kollidieren mit AltGr (wo das System AltGr als Strg+Alt meldet).</summary>
    public static bool AltGrApplies(Hotkey hotkey) => hotkey.Ctrl && hotkey.Alt && !hotkey.Win;

    private static bool Has(Hotkey hotkey, Modifier modifier) => modifier switch
    {
        Modifier.Ctrl => hotkey.Ctrl,
        Modifier.Alt => hotkey.Alt,
        Modifier.Shift => hotkey.Shift,
        Modifier.Win => hotkey.Win,
        _ => false,
    };

    private static bool StandsAlone(int vk)
        => vk is (>= VK_F1 and <= VK_F24) or VK_SNAPSHOT or VK_PAUSE or VK_SCROLL or (>= 0xA6 and <= 0xB7);

    /// <summary>Anzeigename einer Taste (Virtual-Key-Code).</summary>
    internal static string KeyName(int vk)
    {
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= VK_F1 and <= VK_F24) return "F" + (vk - VK_F1 + 1).ToString(CultureInfo.InvariantCulture);
        if (vk is >= VK_NUMPAD0 and <= VK_NUMPAD0 + 9)
            return string.Format(Strings.Core_Key_NumPad, (vk - VK_NUMPAD0).ToString(CultureInfo.InvariantCulture));
        if (KeyResource(vk) is { } resource) return Strings.ResourceManager.GetString(resource, Strings.Culture) ?? resource;
        if (vk is >= 0xBA and <= 0xF5 && LayoutChar(vk) is { } c) return c;
        return string.Format(Strings.Core_Key_Unknown, vk.ToString("X2", CultureInfo.InvariantCulture));
    }

    /// <summary>Name des Eintrags in den Sprachdateien für eine Taste aus <see cref="NamedKeys"/>, sonst null.</summary>
    private static string? KeyResource(int vk) => vk switch
    {
        0x08 => nameof(Strings.Core_Key_Backspace),
        0x09 => nameof(Strings.Core_Key_Tab),
        0x0D => nameof(Strings.Core_Key_Enter),
        VK_PAUSE => nameof(Strings.Core_Key_Pause),
        0x14 => nameof(Strings.Core_Key_CapsLock),
        0x1B => nameof(Strings.Core_Key_Esc),
        0x20 => nameof(Strings.Core_Key_Space),
        0x21 => nameof(Strings.Core_Key_PageUp),
        0x22 => nameof(Strings.Core_Key_PageDown),
        0x23 => nameof(Strings.Core_Key_End),
        0x24 => nameof(Strings.Core_Key_Home),
        0x25 => nameof(Strings.Core_Key_Left),
        0x26 => nameof(Strings.Core_Key_Up),
        0x27 => nameof(Strings.Core_Key_Right),
        0x28 => nameof(Strings.Core_Key_Down),
        VK_SNAPSHOT => nameof(Strings.Core_Key_PrintScreen),
        0x2D => nameof(Strings.Core_Key_Insert),
        0x2E => nameof(Strings.Core_Key_Delete),
        0x5D => nameof(Strings.Core_Key_Menu),
        0x6A => nameof(Strings.Core_Key_NumMultiply),
        0x6B => nameof(Strings.Core_Key_NumPlus),
        0x6D => nameof(Strings.Core_Key_NumMinus),
        0x6E => nameof(Strings.Core_Key_NumDecimal),
        0x6F => nameof(Strings.Core_Key_NumDivide),
        0x90 => nameof(Strings.Core_Key_NumLock),
        VK_SCROLL => nameof(Strings.Core_Key_ScrollLock),
        0x5F => nameof(Strings.Core_Key_Sleep),
        0xA6 => nameof(Strings.Core_Key_BrowserBack),
        0xA7 => nameof(Strings.Core_Key_BrowserForward),
        0xA8 => nameof(Strings.Core_Key_BrowserRefresh),
        0xA9 => nameof(Strings.Core_Key_BrowserStop),
        0xAA => nameof(Strings.Core_Key_BrowserSearch),
        0xAB => nameof(Strings.Core_Key_BrowserFavorites),
        0xAC => nameof(Strings.Core_Key_BrowserHome),
        0xAD => nameof(Strings.Core_Key_VolumeMute),
        0xAE => nameof(Strings.Core_Key_VolumeDown),
        0xAF => nameof(Strings.Core_Key_VolumeUp),
        0xB0 => nameof(Strings.Core_Key_NextTrack),
        0xB1 => nameof(Strings.Core_Key_PreviousTrack),
        0xB2 => nameof(Strings.Core_Key_MediaStop),
        0xB3 => nameof(Strings.Core_Key_PlayPause),
        0xB4 => nameof(Strings.Core_Key_Mail),
        0xB5 => nameof(Strings.Core_Key_MediaSelect),
        0xB6 => nameof(Strings.Core_Key_App1),
        0xB7 => nameof(Strings.Core_Key_App2),
        0xBB => nameof(Strings.Core_Key_Plus),
        0xBC => nameof(Strings.Core_Key_Comma),
        0xBD => nameof(Strings.Core_Key_Minus),
        0xBE => nameof(Strings.Core_Key_Period),
        _ => null,
    };

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
        else if (hex.StartsWith("key")) hex = hex[3..];
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
        // Die angezeigten Namen jeder Sprache, damit ein angezeigtes Kürzel immer wieder eingelesen werden kann
        foreach (var (vk, _) in NamedKeys)
        {
            if (KeyResource(vk) is not { } resource) continue;
            foreach (var language in Localization.UiLanguage.Supported)
            {
                if (Strings.ResourceManager.GetString(resource, CultureInfo.GetCultureInfo(language)) is { Length: > 0 } name)
                    aliases.TryAdd(Normalize(name), vk);
            }
        }
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
