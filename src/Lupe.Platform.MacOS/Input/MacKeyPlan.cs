using Lupe.Core.Input;

namespace Lupe.Platform.MacOS.Input;

/// <summary>Ein CGEvent, wie es gesendet wird: Unicode-Text (auf Taste 0) oder eine echte Taste.</summary>
/// <param name="Text">Bei Unicode: ein Zeichen (ein UTF-16-Wert oder ein Surrogatpaar); sonst null.</param>
/// <param name="KeyCode">macOS-Tastencode (bei Unicode 0).</param>
/// <param name="Consumed">Wie viele <see cref="KeyEvent"/>s bis einschließlich dieses Ereignisses verbraucht sind.</param>
internal readonly record struct MacStroke(string? Text, ushort KeyCode, bool Down, int Consumed);

/// <summary>
/// Übersetzt die Ereignisfolge von <see cref="KeyboardTyper"/> (Windows-Vokabular) in CGEvents: Surrogatpaare werden zu
/// einem Zeichen zusammengefügt, die Maskentaste 0xE8 entfällt, Tasten ohne macOS-Entsprechung ebenso.
/// </summary>
internal static class MacKeyPlan
{
    private const ushort VK_MASK = 0xE8;
    private static readonly string Replacement = ((char)0xFFFD).ToString();

    public static List<MacStroke> Build(ReadOnlySpan<KeyEvent> events)
    {
        var strokes = new List<MacStroke>(events.Length);
        char pendingHighDown = '\0', pendingHighUp = '\0';
        for (int i = 0; i < events.Length; i++)
        {
            var e = events[i];
            int consumed = i + 1;
            if (e.IsUnicode)
            {
                char c = (char)e.Scan;
                ref char pending = ref e.IsUp ? ref pendingHighUp : ref pendingHighDown;
                if (char.IsHighSurrogate(c))
                {
                    // Vorheriges hohes Surrogat ohne Partner: als Ersatzzeichen senden
                    if (pending != '\0') strokes.Add(new MacStroke(Replacement, 0, !e.IsUp, i));
                    pending = c;
                    continue;
                }
                string text;
                if (char.IsLowSurrogate(c) && pending != '\0') text = new string([pending, c]);
                else if (char.IsSurrogate(c)) text = Replacement;
                else
                {
                    if (pending != '\0') strokes.Add(new MacStroke(Replacement, 0, !e.IsUp, i));
                    text = c.ToString();
                }
                pending = '\0';
                strokes.Add(new MacStroke(text, 0, !e.IsUp, consumed));
                continue;
            }
            if (e.VirtualKey == VK_MASK) continue;
            if (MacKeyCodes.FromVirtualKey(e.VirtualKey) is { } mac)
                strokes.Add(new MacStroke(null, mac, !e.IsUp, consumed));
        }
        return strokes;
    }
}
