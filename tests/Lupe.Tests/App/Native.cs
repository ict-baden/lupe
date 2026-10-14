using System.Runtime.InteropServices;
using System.Text;

namespace Lupe.Tests.App;

/// <summary>Fenster der gestarteten Lupe.exe finden und ihr Nachrichten schicken (wie ein Klick auf „Schließen“).</summary>
internal static class Native
{
    public const uint WM_CLOSE = 0x0010;
    private const int GWL_STYLE = -16;
    private const long WS_VISIBLE = 0x10000000;

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(nint hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    /// <summary>Oberstes Fenster des Prozesses mit genau diesem Titel, sonst 0.</summary>
    public static nint FindWindow(int processId, string title)
    {
        nint found = 0;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != (uint)processId) return true;
            var text = new StringBuilder(256);
            GetWindowText(hwnd, text, text.Capacity);
            if (text.ToString() != title) return true;
            found = hwnd;
            return false;
        }, 0);
        return found;
    }

    /// <summary>Hat das Fenster den Stil WS_VISIBLE (auch auf einem unsichtbaren Desktop aussagekräftig)?</summary>
    public static bool HasVisibleStyle(nint hwnd) => (GetWindowLongPtr(hwnd, GWL_STYLE) & WS_VISIBLE) != 0;
}
