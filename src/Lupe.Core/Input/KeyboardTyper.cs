namespace Lupe.Core.Input;

/// <summary>Tippt Text als Tastatureingabe in das Vordergrundfenster (SendInput mit Unicode, unabhängig vom Tastaturlayout).</summary>
public static class KeyboardTyper
{
    /// <param name="suffix">"" (nichts), "Enter" oder "Tab" nach dem Text.</param>
    public static void Type(string text, string suffix = "", CancellationToken ct = default) => throw new NotImplementedException();
}
