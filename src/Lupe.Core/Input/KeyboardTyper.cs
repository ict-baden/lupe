using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lupe.Core.Input;

/// <summary>Tippt Text als Tastatureingabe in das Vordergrundfenster (SendInput mit Unicode, unabhängig vom Tastaturlayout).</summary>
/// <remarks>
/// <para>Jedes Zeichen geht als Unicode-Tastendruck (KEYEVENTF_UNICODE) hinaus, Zeichen außerhalb der BMP (Emoji) als
/// Surrogatpaar in einem Stück. Zeilenumbrüche (\r\n, \n, \r, U+0085, U+2028, U+2029) werden zur Eingabetaste,
/// Tabulatoren zur Tab-Taste; andere Steuerzeichen (z. B. das GS-Trennzeichen in GS1-Codes) lassen sich nicht tippen
/// und werden ausgelassen.</para>
/// <para>Vor dem Tippen wird gewartet, bis keine Taste mehr gedrückt ist – sonst würde aus „a“ bei noch gehaltenem
/// Strg (aus dem Tastenkürzel) ein Strg+A. Hält jemand eine Umschalttaste länger als 1,5 s fest, wird sie für das
/// Tippen künstlich losgelassen. Lange Texte gehen in Paketen hinaus, damit das Zielprogramm mitkommt.</para>
/// <para>Blockiert, bis alles gesendet ist – also nicht im UI-Thread mit sehr langen Texten aufrufen.</para>
/// </remarks>
public static class KeyboardTyper
{
    /// <summary>Höchstens so viele Ereignisse je SendInput (≈ 200 Zeichen).</summary>
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
    private const char NextLine = (char)0x0085;
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    private static readonly Lock TypingGate = new();

    /// <param name="text">Der zu tippende Text.</param>
    /// <param name="suffix">"" (nichts), "Enter" oder "Tab" nach dem Text (Groß/Klein egal; Unbekanntes wird ignoriert).</param>
    /// <param name="ct">Bricht zwischen zwei Paketen ab (<see cref="OperationCanceledException"/>).</param>
    /// <remarks>Lässt Windows keine Eingabe zu (gesperrter Desktop, Anmeldedialog), passiert nichts – den Grund liefert <see cref="TryType"/>.</remarks>
    public static void Type(string text, string suffix = "", CancellationToken ct = default)
    {
        if (!TryType(text, suffix, out string? error, ct)) Debug.WriteLine($"Lupe: {error}");
    }

    /// <summary>Wie <see cref="Type"/>, meldet aber, ob alles angekommen ist; sonst steht der Grund auf Deutsch in <paramref name="error"/>.</summary>
    public static bool TryType(string text, string suffix, out string? error, CancellationToken ct = default)
    {
        // Zwei gleichzeitige Aufrufe würden ihre Pakete ineinander verschränken – nacheinander tippen.
        lock (TypingGate) return TypeCore(text, suffix, SystemKeyboard.Instance, out error, ct);
    }

    internal static bool TypeCore(string text, string? suffix, IKeyboard keyboard, out string? error, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        ct.ThrowIfCancellationRequested();
        error = null;

        var inputs = BuildInputs(text, suffix);
        if (inputs.Length == 0) return true;

        var stillDown = WaitForKeysReleased(keyboard, ct);
        var release = BuildModifierRelease(stillDown);
        if (release.Length > 0 && !Send(keyboard, release, typedBefore: false, out error)) return false;

        bool typed = false;
        foreach (var (offset, count) in Batches(inputs, MaxInputsPerBatch))
        {
            ct.ThrowIfCancellationRequested();
            if (typed) keyboard.Pause(BatchPauseMs, ct);
            ct.ThrowIfCancellationRequested();
            if (!Send(keyboard, inputs.AsSpan(offset, count), typed, out error)) return false;
            typed = true;
        }
        return true;
    }

    /// <summary>Die vollständige INPUT-Folge für Text und Suffix (je Taste Drücken und Loslassen).</summary>
    internal static Native.INPUT[] BuildInputs(string text, string? suffix)
    {
        ArgumentNullException.ThrowIfNull(text);
        var list = new List<Native.INPUT>(text.Length * 2 + 2);
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
    internal static List<(int Offset, int Count)> Batches(Native.INPUT[] inputs, int max)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(max);
        var batches = new List<(int, int)>();
        int start = 0, safeEnd = 0, depth = 0;
        for (int i = 0; i < inputs.Length; i++)
        {
            depth += (inputs[i].u.ki.dwFlags & Native.KEYEVENTF_KEYUP) != 0 ? -1 : 1;
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
    internal static List<int> WaitForKeysReleased(IKeyboard keyboard, CancellationToken ct)
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

    private static List<int> PressedKeys(IKeyboard keyboard)
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
    internal static Native.INPUT[] BuildModifierRelease(IReadOnlyCollection<int> pressed)
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
        var list = new List<Native.INPUT>();
        if (set.Contains(VK_LMENU) || set.Contains(VK_RMENU) || set.Contains(VK_LWIN) || set.Contains(VK_RWIN))
        {
            list.Add(Key(VK_MASK, 0, up: false, extended: false));
            list.Add(Key(VK_MASK, 0, up: true, extended: false));
        }
        foreach (var m in modifiers)
            if (set.Contains(m.Vk)) list.Add(Key(m.Vk, m.Scan, up: true, m.Extended));
        return [.. list];
    }

    private static bool Send(IKeyboard keyboard, ReadOnlySpan<Native.INPUT> inputs, bool typedBefore, out string? error)
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
            ? "Eintippen wurde unterbrochen – der Text kam nur teilweise an."
            : code == Native.ERROR_ACCESS_DENIED
                ? "Eintippen nicht möglich – Windows nimmt gerade keine Tastatureingaben an (gesperrter Bildschirm, Anmeldedialog oder keine sichtbare Sitzung)."
                : $"Eintippen nicht möglich (Windows-Fehler {code}).";
        return false;
    }

    /// <summary>Loslass-Ereignisse für alle Tasten, die in <paramref name="sent"/> gedrückt, aber nicht losgelassen wurden.</summary>
    internal static Native.INPUT[] DanglingKeyUps(ReadOnlySpan<Native.INPUT> sent)
    {
        var open = new List<Native.KEYBDINPUT>();
        foreach (var input in sent)
        {
            var ki = input.u.ki;
            bool up = (ki.dwFlags & Native.KEYEVENTF_KEYUP) != 0;
            int index = open.FindIndex(k => k.wVk == ki.wVk && k.wScan == ki.wScan
                && (k.dwFlags & Native.KEYEVENTF_UNICODE) == (ki.dwFlags & Native.KEYEVENTF_UNICODE));
            if (!up) open.Add(ki);
            else if (index >= 0) open.RemoveAt(index);
        }
        var result = new Native.INPUT[open.Count];
        for (int i = 0; i < open.Count; i++)
        {
            var k = open[i];
            result[i] = new Native.INPUT
            {
                type = Native.INPUT_KEYBOARD,
                u = new Native.INPUTUNION { ki = k with { dwFlags = k.dwFlags | Native.KEYEVENTF_KEYUP } },
            };
        }
        return result;
    }

    private static void AddKey(List<Native.INPUT> list, ushort vk, ushort scan)
    {
        list.Add(Key(vk, scan, up: false, extended: false));
        list.Add(Key(vk, scan, up: true, extended: false));
    }

    private static void AddUnicode(List<Native.INPUT> list, char c)
    {
        list.Add(Unicode(c, up: false));
        list.Add(Unicode(c, up: true));
    }

    private static Native.INPUT Key(ushort vk, ushort scan, bool up, bool extended) => new()
    {
        type = Native.INPUT_KEYBOARD,
        u = new Native.INPUTUNION
        {
            ki = new Native.KEYBDINPUT
            {
                wVk = vk,
                wScan = scan,
                dwFlags = (up ? Native.KEYEVENTF_KEYUP : 0) | (extended ? Native.KEYEVENTF_EXTENDEDKEY : 0),
            },
        },
    };

    internal static Native.INPUT Unicode(char c, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        u = new Native.INPUTUNION
        {
            ki = new Native.KEYBDINPUT
            {
                wVk = 0,
                wScan = c,
                dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0),
            },
        },
    };
}

/// <summary>Zugriff auf die echte Tastatur – austauschbar, damit sich Warten, Pakete und Fehler ohne Desktop testen lassen.</summary>
internal interface IKeyboard
{
    /// <summary>Sendet die Ereignisse; liefert, wie viele Windows angenommen hat (bei weniger: Win32-Fehler).</summary>
    int Send(ReadOnlySpan<Native.INPUT> inputs, out int win32Error);
    bool IsDown(int virtualKey);
    void Pause(int milliseconds, CancellationToken ct);
}

internal sealed unsafe class SystemKeyboard : IKeyboard
{
    public static SystemKeyboard Instance { get; } = new();

    private SystemKeyboard() { }

    public int Send(ReadOnlySpan<Native.INPUT> inputs, out int win32Error)
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

    public bool IsDown(int virtualKey) => Native.GetAsyncKeyState(virtualKey) < 0;

    public void Pause(int milliseconds, CancellationToken ct) => ct.WaitHandle.WaitOne(milliseconds);
}
