using Lupe.Core.Model;
using Lupe.Core.Platform;

namespace Lupe.Platform.Windows.Input;

/// <summary>Tastaturlayout des aufrufenden Threads (MapVirtualKey, VkKeyScan, ToUnicodeEx).</summary>
public sealed class WindowsKeyboardLayout : IKeyboardLayout
{
    public char? KeyChar(int virtualKey)
    {
        uint mapped = Native.MapVirtualKeyW((uint)virtualKey, Native.MAPVK_VK_TO_CHAR);
        char c = (char)(mapped & 0xFFFF); // oberstes Bit = Tottaste, das Zeichen zählt trotzdem
        return c == 0 ? null : c;
    }

    public int? KeyForChar(char c)
    {
        short result = Native.VkKeyScanW(c);
        if (result == -1) return null;
        int vk = result & 0xFF, shiftState = (result >> 8) & 0xFF;
        return shiftState == 0 ? vk : null;
    }

    /// <summary>Windows meldet AltGr als Strg+Alt.</summary>
    public bool AltGrIsCtrlAlt => true;

    public string? AltGrText(int virtualKey, bool shift) => AltGr.Text(virtualKey, shift, Native.GetKeyboardLayout(0));
}

/// <summary>Globale Tastenkürzel über RegisterHotKey (siehe <see cref="GlobalHotkey"/>).</summary>
public sealed class WindowsHotkeys : IGlobalHotkeyProvider
{
    public string? UnsupportedReason => null;
    public IGlobalHotkey Register(Hotkey hotkey) => new GlobalHotkey(hotkey);
}

/// <summary>Vordergrundfenster als HWND.</summary>
public sealed class WindowsActiveWindow : IActiveWindow
{
    public bool IsSupported => true;

    public nint Foreground() => Native.GetForegroundWindow();

    public bool IsOwnWindow(nint window)
    {
        if (window == 0) return false;
        Native.GetWindowThreadProcessId(window, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>Wiederherstellen, nach vorn holen und bis zu ~0,5 s auf den Fokus warten (wie bisher in der App).</summary>
    public async Task<string?> ActivateAsync(nint window, CancellationToken ct = default)
    {
        if (window == 0 || !Native.IsWindow(window)) return "Das Fenster von vorher gibt es nicht mehr.";
        if (Native.IsIconic(window)) Native.ShowWindow(window, Native.SW_RESTORE);
        Native.SetForegroundWindow(window);
        for (int i = 0; i < 20 && Native.GetForegroundWindow() != window; i++)
        {
            await Task.Delay(25, ct).ConfigureAwait(false);
            if (i == 8) Native.SetForegroundWindow(window);
        }
        return Native.GetForegroundWindow() != window ? "Das Fenster von vorher ließ sich nicht aktivieren – nichts eingetippt." : null;
    }
}
