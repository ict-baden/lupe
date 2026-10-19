// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using Lupe.Core.Input;
using Lupe.Tests.Screen;

using Lupe.Platform.Windows.Input;

namespace Lupe.Tests.Input;

/// <summary>
/// Echtes Tippen in ein eigenes Win32-Textfeld (EDIT) im selben Prozess – nur auf einem sichtbaren Desktop. Bevor
/// getippt wird, muss das Testfenster nachweislich im Vordergrund sein; sonst bricht der Test ab, statt in fremde
/// Fenster zu schreiben.
/// </summary>
[Collection(DesktopCollection.Name)]
public sealed class KeyboardTyperDesktopTests
{
    private const uint ES_MULTILINE = 0x0004;
    private const uint ES_AUTOVSCROLL = 0x0040;
    private const uint ES_WANTRETURN = 0x1000;
    private const uint WM_GETTEXT = 0x000D;
    private const uint WM_GETTEXTLENGTH = 0x000E;

    [InteractiveFact]
    public void Text_mit_Emoji_Umlaut_Tab_und_Zeilenumbruch_kommt_genau_so_an()
    {
        const string text = "Grüße aus Baden 😀 €5\r\nZeile\t2 – Ω";

        string typed = TypeInto(text, "");

        Assert.Equal(text, typed);
    }

    [InteractiveFact]
    public void Suffix_Enter_und_einzelne_LF_werden_zu_Zeilenumbruechen()
    {
        string typed = TypeInto("a\nb", "Enter");
        Assert.Equal("a\r\nb\r\n", typed);
    }

    [InteractiveFact]
    public void Gehaltenes_Strg_wird_losgelassen_statt_Strg_A_zu_senden()
    {
        // Strg künstlich gedrückt lassen – wie nach einem Tastenkürzel, das noch gehalten wird
        try
        {
            string typed = TypeInto("abc", "", before: () => Send(GlobalHotkeySystemTests.KeyInput(KeyboardTyper.VK_LCONTROL, up: false)));
            Assert.Equal("abc", typed);
            Assert.False(Native.GetAsyncKeyState(KeyboardTyper.VK_CONTROL) < 0, "Strg ist hinterher noch gedrückt");
        }
        finally
        {
            if (Native.GetAsyncKeyState(KeyboardTyper.VK_LCONTROL) < 0)
                Send(GlobalHotkeySystemTests.KeyInput(KeyboardTyper.VK_LCONTROL, up: true));
        }
    }

    [InteractiveFact]
    public void Langer_Text_in_mehreren_Paketen_kommt_vollstaendig_an()
    {
        string text = string.Concat(Enumerable.Range(0, 120).Select(i => $"Zeile {i:000} äöü 😀\r\n"));
        string typed = TypeInto(text, "");
        Assert.Equal(text, typed);
    }

    private static string TypeInto(string text, string suffix, Action? before = null)
    {
        using var ui = new UiThread();
        var (x, y) = DesktopProbe.Spot(60);
        nint edit = ui.CreateWindow(0x00000008 /* WS_EX_TOPMOST */, "EDIT",
            TestNative.WS_POPUP | TestNative.WS_VISIBLE | TestNative.WS_BORDER | ES_MULTILINE | ES_AUTOVSCROLL | ES_WANTRETURN,
            x, y, 500, 300);
        ui.Invoke(() => SendMessageW(edit, 0x00C5 /* EM_LIMITTEXT */, 0, 0)); // kein 30000-Zeichen-Limit
        BringToFront(ui, edit);
        Assert.True(GetForegroundWindow() == edit, "Das Testfenster ist nicht im Vordergrund – Tippen abgebrochen, um nichts in fremde Fenster zu schreiben.");

        before?.Invoke();
        Assert.True(KeyboardTyper.TryType(text, suffix, out string? error), error);

        // Warten, bis alles verarbeitet ist (der Text kommt über die Nachrichtenschleife des Testfensters)
        string current = "";
        var deadline = DateTime.UtcNow.AddSeconds(10);
        int lastLength = -1, stableRounds = 0;
        while (DateTime.UtcNow < deadline && stableRounds < 5)
        {
            Thread.Sleep(50);
            current = ReadText(edit);
            stableRounds = current.Length == lastLength ? stableRounds + 1 : 0;
            lastLength = current.Length;
        }
        return current;
    }

    private static void BringToFront(UiThread ui, nint hwnd)
    {
        for (int attempt = 0; attempt < 5 && GetForegroundWindow() != hwnd; attempt++)
        {
            ui.Invoke(() =>
            {
                nint foreground = GetForegroundWindow();
                uint foregroundThread = foreground != 0 ? GetWindowThreadProcessId(foreground, out _) : 0;
                bool attached = foregroundThread != 0 && foregroundThread != ui.ThreadId
                    && AttachThreadInput(ui.ThreadId, foregroundThread, true);
                try
                {
                    BringWindowToTop(hwnd);
                    SetForegroundWindow(hwnd);
                    SetFocus(hwnd);
                }
                finally
                {
                    if (attached) AttachThreadInput(ui.ThreadId, foregroundThread, false);
                }
            });
            Thread.Sleep(100);
        }
        ui.Invoke(() => SetFocus(hwnd));
    }

    private static string ReadText(nint hwnd)
    {
        int length = (int)SendMessageW(hwnd, WM_GETTEXTLENGTH, 0, 0);
        var buffer = new char[length + 1];
        int copied = (int)SendMessageW(hwnd, WM_GETTEXT, buffer.Length, buffer);
        return new string(buffer, 0, copied);
    }

    private static void Send(Native.INPUT input)
        => Assert.Equal(1, WindowsKeyboard.Send([input], out _));

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageW(nint hwnd, uint msg, nint wParam, [Out] char[] lParam);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);
}
