using Lupe.Core.Input;

namespace Lupe.Tests.Input;

/// <summary>Tastatur-Attrappe: zeichnet gesendete Pakete und Pausen auf, Tastenzustand und Annahme frei einstellbar.</summary>
internal sealed class FakeKeyboard : IKeyboard
{
    /// <summary>Jeder SendInput-Aufruf als eigenes Paket.</summary>
    public List<Native.INPUT[]> Sent { get; } = [];
    public List<int> Pauses { get; } = [];
    public int KeyQueries { get; private set; }

    /// <summary>Welche Tasten gerade gedrückt sind – abhängig von der Zahl bisheriger Pausen (= Zeit).</summary>
    public Func<int, int, bool> IsDownAfterPauses { get; set; } = (_, _) => false;

    /// <summary>Wie viele Ereignisse Windows vom n-ten Paket annimmt (Standard: alle) und welcher Fehler dann gemeldet wird.</summary>
    public Func<int, int, (int Accepted, int Error)> Accept { get; set; } = (_, length) => (length, 0);

    /// <summary>Wird nach jedem angenommenen Paket aufgerufen (z. B. um abzubrechen).</summary>
    public Action<int>? AfterSend { get; set; }

    /// <summary>Was die Prüfung des Vordergrundfensters meldet (Standard: nichts, Tippen geht).</summary>
    public Func<string?> Foreground { get; set; } = () => null;

    /// <summary>Wie oft das Vordergrundfenster geprüft wurde und wie viele Pausen (= Zeit) bis dahin vergangen waren.</summary>
    public List<int> ForegroundChecks { get; } = [];

    public string? ForegroundProblem()
    {
        ForegroundChecks.Add(Pauses.Count);
        return Foreground();
    }

    public int Send(ReadOnlySpan<Native.INPUT> inputs, out int win32Error)
    {
        int index = Sent.Count;
        Sent.Add(inputs.ToArray());
        var (accepted, error) = Accept(index, inputs.Length);
        win32Error = accepted >= inputs.Length ? 0 : error;
        AfterSend?.Invoke(index);
        return Math.Min(accepted, inputs.Length);
    }

    public bool IsDown(int virtualKey)
    {
        KeyQueries++;
        return IsDownAfterPauses(virtualKey, Pauses.Count);
    }

    public void Pause(int milliseconds, CancellationToken ct) => Pauses.Add(milliseconds);
}

/// <summary>Lesbare Form von INPUT-Folgen für Vergleiche: „↓U+0041“, „↑VK0D“ …</summary>
internal static class Keys
{
    public static string Describe(Native.INPUT input)
    {
        var ki = input.u.ki;
        string direction = (ki.dwFlags & Native.KEYEVENTF_KEYUP) != 0 ? "↑" : "↓";
        if ((ki.dwFlags & Native.KEYEVENTF_UNICODE) != 0) return $"{direction}U+{ki.wScan:X4}";
        string ext = (ki.dwFlags & Native.KEYEVENTF_EXTENDEDKEY) != 0 ? "e" : "";
        return $"{direction}VK{ki.wVk:X2}{ext}";
    }

    public static string[] Describe(IEnumerable<Native.INPUT> inputs) => inputs.Select(Describe).ToArray();

    /// <summary>Unicode-Druck und -Loslassen für jedes Zeichen von <paramref name="text"/> (nur BMP).</summary>
    public static IEnumerable<string> Chars(string text) => text.SelectMany(c => new[] { $"↓U+{(int)c:X4}", $"↑U+{(int)c:X4}" });

    public static readonly string[] Enter = ["↓VK0D", "↑VK0D"];
    public static readonly string[] Tab = ["↓VK09", "↑VK09"];
}
