using Lupe.Core.Model;

namespace Lupe.Core.Input;

/// <summary>Systemweites Tastenkürzel (RegisterHotKey) auf eigenem Thread mit Nachrichtenfenster.</summary>
public sealed class GlobalHotkey : IDisposable
{
    public GlobalHotkey(Hotkey hotkey) => throw new NotImplementedException();

    /// <summary>Wird auf einem Hintergrund-Thread ausgelöst.</summary>
    public event EventHandler? Pressed;

    public bool IsRegistered => throw new NotImplementedException();
    /// <summary>Warum die Anmeldung scheiterte (z. B. Kürzel von einem anderen Programm belegt), sonst null.</summary>
    public string? Error => throw new NotImplementedException();

    public void Dispose() => throw new NotImplementedException();
}

/// <summary>Anzeige und Eingabe von Tastenkürzeln wie „Strg+Alt+Q“.</summary>
public static class HotkeyText
{
    public static string Format(Hotkey hotkey) => throw new NotImplementedException();
    public static bool TryParse(string text, out Hotkey hotkey) => throw new NotImplementedException();
}
