// SPDX-License-Identifier: MIT
using Lupe.Core.Input;
using Lupe.Core.Platform;

namespace Lupe.Tests.Input;

/// <summary>Tastatur-Attrappe: zeichnet gesendete Pakete und Pausen auf, Tastenzustand und Annahme frei einstellbar.</summary>
internal sealed class FakeKeyboard : IKeyboardOutput
{
    /// <summary>Jeder Send-Aufruf als eigenes Paket.</summary>
    public List<KeyEvent[]> Sent { get; } = [];
    public List<int> Pauses { get; } = [];
    public int KeyQueries { get; private set; }

    /// <summary>Welche Tasten gerade gedrückt sind – abhängig von der Zahl bisheriger Pausen (= Zeit).</summary>
    public Func<int, int, bool> IsDownAfterPauses { get; set; } = (_, _) => false;

    /// <summary>Wie viele Ereignisse die Plattform vom n-ten Paket annimmt (Standard: alle) und welcher Fehler dann gemeldet wird.</summary>
    public Func<int, int, (int Accepted, int Error)> Accept { get; set; } = (_, length) => (length, 0);

    /// <summary>Wird nach jedem angenommenen Paket aufgerufen (z. B. um abzubrechen).</summary>
    public Action<int>? AfterSend { get; set; }

    /// <summary>Was die Prüfung des Vordergrundfensters meldet (Standard: nichts, Tippen geht).</summary>
    public Func<string?> Foreground { get; set; } = () => null;

    /// <summary>Meldung, wenn nichts angenommen wurde. Unter Windows standardmäßig die echten Windows-Texte.</summary>
    public Func<int, string> Rejection { get; set; } =
#if WINDOWS
        Lupe.Platform.Windows.Input.WindowsKeyboard.Instance.DescribeRejection;
#else
        code => $"Eintippen nicht möglich (Fehler {code}).";
#endif

    /// <summary>Wie oft das Vordergrundfenster geprüft wurde und wie viele Pausen (= Zeit) bis dahin vergangen waren.</summary>
    public List<int> ForegroundChecks { get; } = [];

    public string? ForegroundProblem()
    {
        ForegroundChecks.Add(Pauses.Count);
        return Foreground();
    }

    public string DescribeRejection(int errorCode) => Rejection(errorCode);

    public int Send(ReadOnlySpan<KeyEvent> events, out int errorCode)
    {
        int index = Sent.Count;
        Sent.Add(events.ToArray());
        var (accepted, error) = Accept(index, events.Length);
        errorCode = accepted >= events.Length ? 0 : error;
        AfterSend?.Invoke(index);
        return Math.Min(accepted, events.Length);
    }

    public bool IsDown(int virtualKey)
    {
        KeyQueries++;
        return IsDownAfterPauses(virtualKey, Pauses.Count);
    }

    public void Pause(int milliseconds, CancellationToken ct) => Pauses.Add(milliseconds);
}

/// <summary>Lesbare Form von Ereignisfolgen für Vergleiche: „↓U+0041“, „↑VK0D“ …</summary>
internal static class Keys
{
    public static string Describe(KeyEvent e)
    {
        string direction = e.IsUp ? "↑" : "↓";
        if (e.IsUnicode) return $"{direction}U+{e.Scan:X4}";
        string ext = e.IsExtended ? "e" : "";
        return $"{direction}VK{e.VirtualKey:X2}{ext}";
    }

    public static string[] Describe(IEnumerable<KeyEvent> events) => events.Select(Describe).ToArray();

    /// <summary>Unicode-Druck und -Loslassen für jedes Zeichen von <paramref name="text"/> (nur BMP).</summary>
    public static IEnumerable<string> Chars(string text) => text.SelectMany(c => new[] { $"↓U+{(int)c:X4}", $"↑U+{(int)c:X4}" });

    public static readonly string[] Enter = ["↓VK0D", "↑VK0D"];
    public static readonly string[] Tab = ["↓VK09", "↑VK09"];
}
