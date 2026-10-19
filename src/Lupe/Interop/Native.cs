// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Lupe.Interop;

/// <summary>
/// Die wenigen Windows-Aufrufe der Oberfläche: Fenster ohne Ein-/Ausblend-Animation und ohne Aktivierung (DWM, Stile),
/// Vordergrund-Recht für die zweite Instanz, Ton aus dem Speicher, Konsole für die Befehlszeile. Nur unter Windows
/// aufgerufen; Kamera, Bildschirm, Tastatur und Tastenkürzel liegen in Lupe.Platform.Windows.
/// </summary>
internal static partial class Native
{
    // ---------- DWM ----------
    public const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>Wartet, bis DWM das nächste Bild zusammengesetzt hat (ausgeblendetes Fenster ist dann wirklich weg).</summary>
    [LibraryImport("dwmapi.dll")]
    public static partial int DwmFlush();

    // ---------- Fenster ----------
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const int ASFW_ANY = -1;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(int processId);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    // ---------- Ton ----------
    public const uint SND_ASYNC = 0x0001;
    public const uint SND_NODEFAULT = 0x0002;
    public const uint SND_MEMORY = 0x0004;

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PlaySound(nint sound, nint module, uint flags);

    // ---------- Konsole ----------
    // Lupe.exe ist unter Windows ein Fensterprogramm (WinExe). Für die Befehlszeile hängt es sich an die Konsole
    // des Aufrufers, statt eine eigene zu öffnen – ein Doppelklick zeigt so nie ein schwarzes Fenster.
    public const int ATTACH_PARENT_PROCESS = -1;
    public const int STD_OUTPUT_HANDLE = -11;
    public const int STD_ERROR_HANDLE = -12;
    public const uint FILE_TYPE_DISK = 0x0001;
    public const uint FILE_TYPE_CHAR = 0x0002;
    public const uint FILE_TYPE_PIPE = 0x0003;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint GetStdHandle(int stdHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetStdHandle(int stdHandle, nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint GetFileType(nint handle);

    /// <summary>Codepage der angehängten Konsole (0 ohne Konsole) – mit ihr dekodiert PowerShell, was ein Programm in die Pipe schreibt.</summary>
    [LibraryImport("kernel32.dll")]
    public static partial uint GetConsoleOutputCP();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetFileSizeEx(nint handle, out long size);

    // ---------- Hilfen ----------

    private static nint Hwnd(TopLevel window) =>
        window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle ? handle.Handle : 0;

    /// <summary>Ohne Ein- und Ausblend-Animation (Bildschirm-Scan: das Fenster muss sofort weg sein, sonst steht es halb durchsichtig im Standbild).</summary>
    public static void DisableTransitions(TopLevel window)
    {
        if (!OperatingSystem.IsWindows() || Hwnd(window) is not (not 0 and var hwnd)) return;
        int disable = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref disable, sizeof(int));
    }

    /// <summary>Fenster, das nie aktiviert wird (Ergebnis-Hinweis: der Fokus bleibt, wo getippt wurde).</summary>
    public static void MakeNoActivate(TopLevel window)
    {
        if (!OperatingSystem.IsWindows() || Hwnd(window) is not (not 0 and var hwnd)) return;
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)((long)style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
    }

    /// <summary>Unter Windows: warten, bis DWM ein ausgeblendetes Fenster wirklich entfernt hat; sonst kurz warten.</summary>
    public static async Task WaitForHiddenAsync()
    {
        await Task.Delay(40);
        if (OperatingSystem.IsWindows()) DwmFlush();
        else await Task.Delay(160); // Compositor (X11/Wayland/macOS) braucht ein, zwei Bilder
        await Task.Delay(40);
    }
}
