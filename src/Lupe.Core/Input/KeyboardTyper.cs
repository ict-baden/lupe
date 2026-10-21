// SPDX-License-Identifier: MIT
using System.Diagnostics;
using Lupe.Core.Platform;
using Lupe.Core.Resources;

namespace Lupe.Core.Input;

/// <summary>Tippt Text als Tastatureingabe in das Vordergrundfenster (Unicode, unabhängig vom Tastaturlayout) – über
/// <see cref="IPlatform.Keyboard"/> der aktiven Plattform; Windows: SendInput mit KEYEVENTF_UNICODE.</summary>
/// <remarks>
/// <para>Jedes Zeichen geht als Unicode-Tastendruck (KEYEVENTF_UNICODE) hinaus, Zeichen außerhalb der BMP (Emoji) als
/// Surrogatpaar in einem Stück. Zeilenumbrüche (\r\n, \n, \r, U+0085, U+2028, U+2029) werden zur Eingabetaste,
/// Tabulatoren zur Tab-Taste; andere Steuerzeichen (z. B. das GS-Trennzeichen in GS1-Codes) lassen sich nicht tippen
/// und werden ausgelassen.</para>
/// <para>Vor dem Tippen wird gewartet, bis keine Taste mehr gedrückt ist – sonst würde aus „a“ bei noch gehaltenem
/// Strg (aus dem Tastenkürzel) ein Strg+A. Hält jemand eine Umschalttaste länger als 1,5 s fest, wird sie für das
/// Tippen künstlich losgelassen. Lange Texte gehen in Paketen hinaus, damit das Zielprogramm mitkommt.</para>
/// <para>Läuft das Vordergrundfenster mit höheren Rechten als Lupe (z. B. als Administrator), verwirft Windows die
/// Eingaben stillschweigend (UIPI); das wird vorher erkannt und gemeldet, statt ins Leere zu tippen.</para>
/// <para>Blockiert, bis alles gesendet ist – also nicht im UI-Thread mit sehr langen Texten aufrufen.</para>
/// </remarks>
public static class KeyboardTyper
{
    /// <summary>Höchstens so viele Ereignisse je Paket (≈ 200 Zeichen).</summary>
    internal const int MaxInputsPerBatch = 400;
    /// <summary>Pause zwischen zwei Paketen, damit das Zielprogramm seine Warteschlange abarbeiten kann.</summary>
    internal const int BatchPauseMs = 15;
    internal const int KeyPollMs = 10;
    /// <summary>So lange wird auf das Loslassen gedrückter Tasten gewartet.</summary>
    internal const int KeyReleaseTimeoutMs = 1500;

    internal const ushort VK_TAB = 0x09;
    internal const ushort VK_RETURN = 0x0D;
    internal const ushort VK_SHIFT = 0x10;
    internal const ushort VK_CONTROL = 0x11;
    internal const ushort VK_MENU = 0x12;
    internal const ushort VK_LWIN = 0x5B;
    internal const ushort VK_RWIN = 0x5C;
    internal const ushort VK_LSHIFT = 0xA0;
    internal const ushort VK_RSHIFT = 0xA1;
    internal const ushort VK_LCONTROL = 0xA2;
    internal const ushort VK_RCONTROL = 0xA3;
    internal const ushort VK_LMENU = 0xA4;
    internal const ushort VK_RMENU = 0xA5;
    internal const ushort VK_PACKET = 0xE7;
    /// <summary>Unbelegter Tastencode: ein Druck darauf verhindert, dass das Loslassen von Alt/Win das Menü bzw. Start öffnet.</summary>
    internal const ushort VK_MASK = 0xE8;

    private const ushort ScanTab = 0x0F;
    private const ushort ScanReturn = 0x1C;
    private const char ReplacementCharacter = (char)0xFFFD;
    /// <summary>
    /// Für gelesene Codes: Zeilenumbrüche (CR, LF, CRLF, NEL, U+2028, U+2029) als je ein Leerzeichen. Lupe tippt wie ein
    /// Handscanner in das Programm vorn – ein mehrzeiliger Code (vor der Kamera oder auf einer Webseite im Bildschirm-Scan)
    /// darf dort nicht selbst Enter drücken (etwa Befehle in ein Terminal). Die eingestellte Taste nach dem Tippen kommt weiter.
    /// </summary>
    public static string WithoutLineBreaks(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.IndexOfAny(['\r', '\n', NextLine, LineSeparator, ParagraphSeparator]) < 0) return text;
        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            sb.Append(c is '\r' or '\n' or NextLine or LineSeparator or ParagraphSeparator ? ' ' : c);
        }
        return sb.ToString();
    }

    private const char NextLine = (char)0x0085;
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    private static readonly Lock TypingGate = new();

    /// <param name="text">Der zu tippende Text.</param>
    /// <param name="suffix">"" (nichts), "Enter" oder "Tab" nach dem Text (Groß/Klein egal; Unbekanntes wird ignoriert).</param>
    /// <param name="ct">Bricht zwischen zwei Paketen ab (<see cref="OperationCanceledException"/>).</param>
    /// <remarks>Lässt Windows keine Eingabe zu (gesperrter Desktop, Anmeldedialog, Zielfenster mit Administratorrechten), passiert
    /// nichts – den Grund liefert <see cref="TryType"/>.</remarks>
    public static void Type(string text, string suffix = "", CancellationToken ct = default)
    {
        if (!TryType(text, suffix, out string? error, ct)) Debug.WriteLine($"Lupe: {error}");
    }

    /// <summary>Wie <see cref="Type"/>, meldet aber, ob alles angekommen ist; sonst steht der Grund auf Deutsch in <paramref name="error"/>.</summary>
    public static bool TryType(string text, string suffix, out string? error, CancellationToken ct = default) =>
        TryType(text, suffix, 0, out error, ct);

    /// <param name="delayMs">
    /// Pause zwischen zwei Zeichen (0 = alles in Paketen auf einmal). Manche Programme – allen voran Notepad unter
    /// Windows 11 – verarbeiten Unicode-Eingaben verzögert und lesen das Zeichen erst dann; kommen die Zeichen schneller,
    /// bekommen sie nur noch das zuletzt gesendete. Mit Pause geht jedes Zeichen einzeln hinaus, wie bei einem Handscanner.
    /// </param>
    /// <param name="stillOnTarget">
    /// Vor jedem Paket gefragt (bei Zeichenpause also vor jedem Zeichen): Ist das Zielfenster noch vorn? Wenn nicht, bricht
    /// das Tippen ab – sonst landete der Rest in dem Fenster, das inzwischen vorn ist. null = nicht prüfen.
    /// </param>
    public static bool TryType(string text, string suffix, int delayMs, out string? error, CancellationToken ct = default, Func<bool>? stillOnTarget = null)
    {
        // Zwei gleichzeitige Aufrufe würden ihre Pakete ineinander verschränken – nacheinander tippen.
        lock (TypingGate) return TypeCore(text, suffix, LupePlatform.Current.Keyboard, out error, ct, delayMs, stillOnTarget);
    }

    internal static bool TypeCore(string text, string? suffix, IKeyboardOutput keyboard, out string? error, CancellationToken ct, int delayMs = 0,
        Func<bool>? stillOnTarget = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ct.ThrowIfCancellationRequested();
        error = null;

        var inputs = BuildInputs(text, suffix);
        if (inputs.Length == 0) return true;

        var stillDown = WaitForKeysReleased(keyboard, ct);
        // Erst nach dem Warten prüfen – bis dahin kann ein anderes Fenster nach vorn gekommen sein.
        if (keyboard.ForegroundProblem() is { } problem)
        {
            error = problem;
            return false;
        }
        var release = BuildModifierRelease(stillDown);
        if (release.Length > 0 && !Send(keyboard, release, typedBefore: false, out error)) return false;

        bool typed = false;
        // Mit Zeichenpause geht jede Taste (bei Emoji das Surrogatpaar) als eigenes Paket hinaus
        int perBatch = delayMs > 0 ? 1 : MaxInputsPerBatch, pause = delayMs > 0 ? delayMs : BatchPauseMs;
        foreach (var (offset, count) in Batches(inputs, perBatch))
        {
            ct.ThrowIfCancellationRequested();
            if (typed) keyboard.Pause(pause, ct);
            ct.ThrowIfCancellationRequested();
            if (stillOnTarget is not null && !stillOnTarget())
            {
                error = typed ? Strings.Core_Typing_TargetChangedPartly : Strings.Core_Typing_TargetChanged;
                return false;
            }
            if (!Send(keyboard, inputs.AsSpan(offset, count), typed, out error)) return false;
            typed = true;
        }
        return true;
    }

    /// <summary>Die vollständige Ereignisfolge für Text und Suffix (je Taste Drücken und Loslassen).</summary>
    internal static KeyEvent[] BuildInputs(string text, string? suffix)
    {
        ArgumentNullException.ThrowIfNull(text);
        var list = new List<KeyEvent>(text.Length * 2 + 2);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            switch (c)
            {
                case '\r':
                    AddKey(list, VK_RETURN, ScanReturn);
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    break;
                case '\n' or NextLine or LineSeparator or ParagraphSeparator:
                    AddKey(list, VK_RETURN, ScanReturn);
                    break;
                case '\t':
                    AddKey(list, VK_TAB, ScanTab);
                    break;
                default:
                    if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        // Beide Hälften drücken, dann beide loslassen: das Ziel bekommt zwei WM_CHAR direkt hintereinander.
                        char low = text[++i];
                        list.Add(Unicode(c, up: false));
                        list.Add(Unicode(low, up: false));
                        list.Add(Unicode(c, up: true));
                        list.Add(Unicode(low, up: true));
                    }
                    else if (char.IsSurrogate(c))
                    {
                        AddUnicode(list, ReplacementCharacter); // halbes Surrogatpaar – kaputter Text
                    }
                    else if (!char.IsControl(c))
                    {
                        AddUnicode(list, c);
                    }
                    break;
            }
        }
        if (SuffixKey(suffix) is { } key) AddKey(list, key.Vk, key.Scan);
        return [.. list];
    }

    /// <summary>"Enter"/"Eingabe"/"Return" → Eingabetaste, "Tab"/"Tabulator" → Tab, sonst nichts.</summary>
    internal static (ushort Vk, ushort Scan)? SuffixKey(string? suffix) => suffix?.Trim().ToLowerInvariant() switch
    {
        "enter" or "eingabe" or "eingabetaste" or "return" => (VK_RETURN, ScanReturn),
        "tab" or "tabulator" => (VK_TAB, ScanTab),
        _ => null,
    };

    /// <summary>Teilt die Folge in Pakete von höchstens <paramref name="max"/> Ereignissen – nur an Stellen, an denen keine Taste gedrückt ist.</summary>
    internal static List<(int Offset, int Count)> Batches(KeyEvent[] inputs, int max)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(max);
        var batches = new List<(int, int)>();
        int start = 0, safeEnd = 0, depth = 0;
        for (int i = 0; i < inputs.Length; i++)
        {
            depth += inputs[i].IsUp ? -1 : 1;
            if (depth == 0) safeEnd = i + 1;
            if (i + 1 - start >= max && safeEnd > start)
            {
                batches.Add((start, safeEnd - start));
                start = safeEnd;
            }
        }
        if (start < inputs.Length) batches.Add((start, inputs.Length - start));
        return batches;
    }

    /// <summary>Wartet, bis keine Taste mehr gedrückt ist; liefert die nach Ablauf der Frist noch gedrückten Tasten.</summary>
    internal static List<int> WaitForKeysReleased(IKeyboardOutput keyboard, CancellationToken ct)
    {
        int maxPolls = KeyReleaseTimeoutMs / KeyPollMs;
        for (int poll = 0; ; poll++)
        {
            var down = PressedKeys(keyboard);
            if (down.Count == 0 || poll >= maxPolls) return down;
            keyboard.Pause(KeyPollMs, ct);
            ct.ThrowIfCancellationRequested();
        }
    }

    private static List<int> PressedKeys(IKeyboardOutput keyboard)
    {
        var down = new List<int>();
        // Ab 0x08: Maustasten (0x01–0x06) zählen nicht; VK_PACKET ist kein echter Tastendruck.
        for (int vk = 0x08; vk <= 0xFE; vk++)
            if (vk != VK_PACKET && keyboard.IsDown(vk)) down.Add(vk);
        return down;
    }

    /// <summary>
    /// Ereignisse, die noch gedrückte Umschalttasten (Strg, Alt, Umschalt, Win – links wie rechts) loslassen. Ist Alt oder
    /// Win dabei, kommt vorher ein Druck auf eine unbelegte Taste, damit Windows kein Menü bzw. Startmenü öffnet.
    /// </summary>
    internal static KeyEvent[] BuildModifierRelease(IReadOnlyCollection<int> pressed)
    {
        var set = new HashSet<int>(pressed);
        // Meldet Windows nur die allgemeine Taste, gilt die linke.
        if (set.Contains(VK_CONTROL) && !set.Contains(VK_LCONTROL) && !set.Contains(VK_RCONTROL)) set.Add(VK_LCONTROL);
        if (set.Contains(VK_MENU) && !set.Contains(VK_LMENU) && !set.Contains(VK_RMENU)) set.Add(VK_LMENU);
        if (set.Contains(VK_SHIFT) && !set.Contains(VK_LSHIFT) && !set.Contains(VK_RSHIFT)) set.Add(VK_LSHIFT);

        ReadOnlySpan<(ushort Vk, ushort Scan, bool Extended)> modifiers =
        [
            (VK_LCONTROL, 0x1D, false), (VK_RCONTROL, 0x1D, true),
            (VK_LMENU, 0x38, false), (VK_RMENU, 0x38, true),
            (VK_LSHIFT, 0x2A, false), (VK_RSHIFT, 0x36, false),
            (VK_LWIN, 0x5B, true), (VK_RWIN, 0x5C, true),
        ];
        var list = new List<KeyEvent>();
        if (set.Contains(VK_LMENU) || set.Contains(VK_RMENU) || set.Contains(VK_LWIN) || set.Contains(VK_RWIN))
        {
            list.Add(Key(VK_MASK, 0, up: false, extended: false));
            list.Add(Key(VK_MASK, 0, up: true, extended: false));
        }
        foreach (var m in modifiers)
            if (set.Contains(m.Vk)) list.Add(Key(m.Vk, m.Scan, up: true, m.Extended));
        return [.. list];
    }

    private static bool Send(IKeyboardOutput keyboard, ReadOnlySpan<KeyEvent> inputs, bool typedBefore, out string? error)
    {
        int sent = keyboard.Send(inputs, out int code);
        if (sent >= inputs.Length)
        {
            error = null;
            return true;
        }
        if (sent > 0)
        {
            // Nichts gedrückt hängen lassen (z. B. eine halbe Eingabetaste).
            var dangling = DanglingKeyUps(inputs[..sent]);
            if (dangling.Length > 0) keyboard.Send(dangling, out _);
        }
        error = sent > 0 || typedBefore
            ? Strings.Core_Typing_Interrupted
            : keyboard.DescribeRejection(code);
        return false;
    }

    /// <summary>Loslass-Ereignisse für alle Tasten, die in <paramref name="sent"/> gedrückt, aber nicht losgelassen wurden.</summary>
    internal static KeyEvent[] DanglingKeyUps(ReadOnlySpan<KeyEvent> sent)
    {
        var open = new List<KeyEvent>();
        foreach (var e in sent)
        {
            int index = open.FindIndex(k => k.VirtualKey == e.VirtualKey && k.Scan == e.Scan && k.IsUnicode == e.IsUnicode);
            if (!e.IsUp) open.Add(e);
            else if (index >= 0) open.RemoveAt(index);
        }
        var result = new KeyEvent[open.Count];
        for (int i = 0; i < open.Count; i++) result[i] = open[i] with { Flags = open[i].Flags | KeyFlags.Up };
        return result;
    }

    private static void AddKey(List<KeyEvent> list, ushort vk, ushort scan)
    {
        list.Add(Key(vk, scan, up: false, extended: false));
        list.Add(Key(vk, scan, up: true, extended: false));
    }

    private static void AddUnicode(List<KeyEvent> list, char c)
    {
        list.Add(Unicode(c, up: false));
        list.Add(Unicode(c, up: true));
    }

    private static KeyEvent Key(ushort vk, ushort scan, bool up, bool extended) =>
        new(vk, scan, (up ? KeyFlags.Up : 0) | (extended ? KeyFlags.Extended : 0));

    internal static KeyEvent Unicode(char c, bool up) => new(0, c, KeyFlags.Unicode | (up ? KeyFlags.Up : 0));
}

/// <summary>Art eines Tastatur-Ereignisses; gleiche Werte wie KEYEVENTF_* in Windows.</summary>
[Flags]
public enum KeyFlags : uint
{
    None = 0,
    /// <summary>Erweiterte Taste (rechte Strg/Alt, Win).</summary>
    Extended = 0x1,
    /// <summary>Loslassen (sonst Drücken).</summary>
    Up = 0x2,
    /// <summary>Unicode-Zeichen in <see cref="KeyEvent.Scan"/> statt einer Taste.</summary>
    Unicode = 0x4,
}

/// <summary>
/// Ein Tastendruck oder Loslassen, plattformneutral. Tasten heißen wie in Windows (Virtual-Key-Codes: 0x0D Eingabe,
/// 0x09 Tab, 0xA0–0xA5 Umschalt/Strg/Alt links/rechts, 0x5B/0x5C Win, 0xE8 unbelegt); andere Plattformen setzen sie
/// in ihre Tastencodes um. Bei <see cref="KeyFlags.Unicode"/> steht das Zeichen (UTF-16-Einheit) in <see cref="Scan"/>
/// – Zeichen außerhalb der BMP kommen als Surrogatpaar: beide Hälften drücken, dann beide loslassen.
/// </summary>
public readonly record struct KeyEvent(ushort VirtualKey, ushort Scan, KeyFlags Flags)
{
    public bool IsUp => (Flags & KeyFlags.Up) != 0;
    public bool IsUnicode => (Flags & KeyFlags.Unicode) != 0;
    public bool IsExtended => (Flags & KeyFlags.Extended) != 0;
    /// <summary>Das Zeichen bei einem Unicode-Ereignis.</summary>
    public char Character => (char)Scan;
}
