using System.Runtime.InteropServices;

namespace Lupe.Core.Input;

/// <summary>Win32-Aufrufe für Tastatureingabe (SendInput, Tastaturlayout) und globale Tastenkürzel (RegisterHotKey, Nachrichtenfenster).</summary>
internal static unsafe partial class Native
{
    // ---------- Fehlercodes ----------
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_INVALID_PARAMETER = 87;
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
    public const int ERROR_REQUIRES_INTERACTIVE_WINDOWSTATION = 1459;

    // ---------- SendInput ----------
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    /// <summary>Die Union in INPUT – MOUSEINPUT ist das größte Mitglied und bestimmt die Größe (x64: 32 Byte).</summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint SendInput(uint cInputs, INPUT* pInputs, int cbSize);

    /// <summary>Höchstes Bit gesetzt (Wert &lt; 0) = Taste ist gerade gedrückt.</summary>
    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int vKey);

    // ---------- Tastaturlayout ----------
    public const uint MAPVK_VK_TO_VSC = 0;
    public const uint MAPVK_VK_TO_CHAR = 2;

    [LibraryImport("user32.dll")]
    public static partial uint MapVirtualKeyW(uint uCode, uint uMapType);

    [LibraryImport("user32.dll")]
    public static partial uint MapVirtualKeyExW(uint uCode, uint uMapType, nint dwhkl);

    /// <summary>Niedriges Byte: Virtual-Key, hohes Byte: nötige Umschalttasten (1 Umschalt, 2 Strg, 4 Alt); -1 = kein Treffer.</summary>
    [LibraryImport("user32.dll")]
    public static partial short VkKeyScanW(ushort ch);

    /// <summary>Flag für ToUnicodeEx: Tastaturzustand des Kernels nicht verändern (ab Windows 10 1607), damit Tottasten erhalten bleiben.</summary>
    public const uint TOUNICODE_NO_STATE_CHANGE = 0x4;

    [LibraryImport("user32.dll")]
    public static partial int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte* lpKeyState, char* pwszBuff, int cchBuff, uint wFlags, nint dwhkl);

    [LibraryImport("user32.dll")]
    public static partial nint GetKeyboardLayout(uint idThread);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    // ---------- Rechte des Zielprozesses (UIPI) ----------
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint TOKEN_QUERY = 0x0008;
    /// <summary>TOKEN_INFORMATION_CLASS: TokenIntegrityLevel (TOKEN_MANDATORY_LABEL).</summary>
    public const int TokenIntegrityLevel = 25;
    /// <summary>TOKEN_INFORMATION_CLASS: TokenUIAccess (DWORD, ≠ 0 = darf UIPI umgehen).</summary>
    public const int TokenUIAccess = 26;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    /// <summary>Pseudo-Handle (-1) des eigenen Prozesses, muss nicht geschlossen werden.</summary>
    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTokenInformation(nint tokenHandle, int tokenInformationClass, void* tokenInformation,
        uint tokenInformationLength, out uint returnLength);

    [LibraryImport("advapi32.dll")]
    public static partial byte* GetSidSubAuthorityCount(nint pSid);

    [LibraryImport("advapi32.dll")]
    public static partial uint* GetSidSubAuthority(nint pSid, uint nSubAuthority);

    // ---------- Tastenkürzel und Nachrichtenfenster ----------
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_APP = 0x8000;
    public const uint PM_NOREMOVE = 0x0000;

    /// <summary>Elternfenster für reine Nachrichtenfenster (unsichtbar, nicht aufzählbar, ohne Broadcasts).</summary>
    public const nint HWND_MESSAGE = -3;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hWnd, int id);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hWnd);

    /// <summary>&gt; 0: Nachricht, 0: WM_QUIT, -1: Fehler.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial int GetMessageW(MSG* lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PeekMessageW(MSG* lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(MSG* lpMsg);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(MSG* lpMsg);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessageW(uint idThread, uint msg, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();
}
