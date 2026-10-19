// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using Lupe.Core.Resources;

namespace Lupe.Platform.Linux.Native;

// Xlib, XTest und XRandR – nur das, was Lupe braucht. Display*, Window, Atom, KeySym sind unter 64-Bit-Linux 8 Byte.

[StructLayout(LayoutKind.Sequential)]
internal struct XImage
{
    public int width, height, xoffset, format;
    public nint data;
    public int byte_order, bitmap_unit, bitmap_bit_order, bitmap_pad, depth, bytes_per_line, bits_per_pixel;
    public nuint red_mask, green_mask, blue_mask;
    public nint obdata;
    public nint create_image, destroy_image, get_pixel, put_pixel, sub_image, add_pixel;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XRRMonitorInfo
{
    public nint name;
    public int primary, automatic, noutput, x, y, width, height, mwidth, mheight;
    public nint outputs;
}

/// <summary>Ein XEvent (Union aus 24 long).</summary>
[StructLayout(LayoutKind.Explicit, Size = 192)]
internal struct XEvent
{
    [FieldOffset(0)] public int type;
    [FieldOffset(0)] public XKeyEvent key;
    [FieldOffset(0)] public XClientMessageEvent client;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XKeyEvent
{
    public int type;
    public nuint serial;
    public int send_event;
    public nint display, window, root, subwindow;
    public nuint time;
    public int x, y, x_root, y_root;
    public uint state, keycode;
    public int same_screen;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XClientMessageEvent
{
    public int type;
    public nuint serial;
    public int send_event;
    public nint display, window, message_type;
    public int format;
    public nint l0, l1, l2, l3, l4;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XErrorEvent
{
    public int type;
    public nint display, resourceid;
    public nuint serial;
    public byte error_code, request_code, minor_code;
}

internal static unsafe partial class X11
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXtst = "libXtst.so.6";
    private const string LibXrandr = "libXrandr.so.2";

    public const int ZPixmap = 2;
    public const int KeyPress = 2, KeyRelease = 3, ClientMessage = 33, MappingNotify = 34;
    public const long KeyPressMask = 1, KeyReleaseMask = 2, ExposureMask = 1 << 15, StructureNotifyMask = 1 << 17,
        SubstructureRedirectMask = 1 << 20, SubstructureNotifyMask = 1 << 19, FocusChangeMask = 1 << 21;
    public const uint ShiftMask = 1, LockMask = 2, ControlMask = 4, Mod1Mask = 8, Mod2Mask = 16, Mod4Mask = 64;
    public const int GrabModeAsync = 1;
    public const int BadAccess = 10;
    public const nint AnyPropertyType = 0;
    public const nint XA_CARDINAL = 6, XA_WINDOW = 33;
    public const int RevertToParent = 2;
    public const nint CurrentTime = 0;

    [DllImport(LibX11)] public static extern int XInitThreads();
    [DllImport(LibX11)] public static extern nint XOpenDisplay(string? name);
    [DllImport(LibX11)] public static extern int XCloseDisplay(nint display);
    [DllImport(LibX11)] public static extern nint XDefaultRootWindow(nint display);
    [DllImport(LibX11)] public static extern int XDefaultScreen(nint display);
    [DllImport(LibX11)] public static extern int XDisplayWidth(nint display, int screen);
    [DllImport(LibX11)] public static extern int XDisplayHeight(nint display, int screen);
    [DllImport(LibX11)] public static extern int XConnectionNumber(nint display);
    [DllImport(LibX11)] public static extern XImage* XGetImage(nint display, nint drawable, int x, int y, uint width, uint height, nuint planeMask, int format);
    [DllImport(LibX11)] public static extern int XFree(void* data);
    [DllImport(LibX11)] public static extern int XSync(nint display, int discard);
    [DllImport(LibX11)] public static extern int XFlush(nint display);
    [DllImport(LibX11)] public static extern int XPending(nint display);
    [DllImport(LibX11)] public static extern int XNextEvent(nint display, XEvent* ev);
    [DllImport(LibX11)] public static extern int XDisplayKeycodes(nint display, out int min, out int max);
    [DllImport(LibX11)] public static extern nuint* XGetKeyboardMapping(nint display, byte first, int count, out int keysymsPerKeycode);
    [DllImport(LibX11)] public static extern int XChangeKeyboardMapping(nint display, int first, int keysymsPerKeycode, nuint* keysyms, int count);
    [DllImport(LibX11)] public static extern byte XKeysymToKeycode(nint display, nuint keysym);
    [DllImport(LibX11)] public static extern nuint XkbKeycodeToKeysym(nint display, byte keycode, int group, int level);
    [DllImport(LibX11)] public static extern int XQueryKeymap(nint display, byte* keys32);
    [DllImport(LibX11)] public static extern int XGrabKey(nint display, int keycode, uint modifiers, nint window, int ownerEvents, int pointerMode, int keyboardMode);
    [DllImport(LibX11)] public static extern int XUngrabKey(nint display, int keycode, uint modifiers, nint window);
    [DllImport(LibX11)] public static extern int XSelectInput(nint display, nint window, long mask);
    [DllImport(LibX11)] public static extern nint XSetErrorHandler(nint handler);
    [DllImport(LibX11)] public static extern nint XGetAtomName(nint display, nint atom);
    [DllImport(LibX11)] public static extern nint XInternAtom(nint display, string name, int onlyIfExists);
    [DllImport(LibX11)] public static extern int XGetWindowProperty(nint display, nint window, nint property, nint offset, nint length,
        int delete, nint reqType, out nint actualType, out int actualFormat, out nuint items, out nuint bytesAfter, out nint prop);
    [DllImport(LibX11)] public static extern int XSendEvent(nint display, nint window, int propagate, long mask, XEvent* ev);
    [DllImport(LibX11)] public static extern int XGetInputFocus(nint display, out nint focus, out int revertTo);
    [DllImport(LibX11)] public static extern int XSetInputFocus(nint display, nint focus, int revertTo, nint time);
    [DllImport(LibX11)] public static extern int XRaiseWindow(nint display, nint window);
    [DllImport(LibX11)] public static extern int XMapRaised(nint display, nint window);
    [DllImport(LibX11)] public static extern int XQueryTree(nint display, nint window, out nint root, out nint parent, out nint children, out uint count);
    [DllImport(LibX11)] public static extern nint XCreateSimpleWindow(nint display, nint parent, int x, int y, uint w, uint h, uint border, nuint borderPixel, nuint background);
    [DllImport(LibX11)] public static extern int XDestroyWindow(nint display, nint window);
    [DllImport(LibX11)] public static extern int XMapWindow(nint display, nint window);
    [DllImport(LibX11)] public static extern int XLookupString(XKeyEvent* ev, byte* buffer, int length, out nuint keysym, nint status);
    [DllImport(LibX11)] public static extern int XRefreshKeyboardMapping(XEvent* ev);

    [DllImport(LibXtst)] public static extern int XTestQueryExtension(nint display, out int eventBase, out int errorBase, out int major, out int minor);
    [DllImport(LibXtst)] public static extern int XTestFakeKeyEvent(nint display, uint keycode, int isPress, nuint delay);

    [DllImport(LibXrandr)] public static extern XRRMonitorInfo* XRRGetMonitors(nint display, nint window, int getActive, out int count);
    [DllImport(LibXrandr)] public static extern void XRRFreeMonitors(XRRMonitorInfo* monitors);

    private static readonly Lazy<bool> s_threads = new(() =>
    {
        try { return XInitThreads() != 0; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    });

    /// <summary>
    /// Öffnet eine eigene Verbindung zum X-Server (DISPLAY); 0, wenn es keinen gibt oder libX11 fehlt. Jede Lupe-Komponente
    /// nutzt ihre eigene Verbindung und sperrt sie selbst.
    /// </summary>
    public static nint TryOpen(out string? error)
    {
        error = null;
        string? name = LinuxSession.Environment("DISPLAY");
        if (string.IsNullOrWhiteSpace(name))
        {
            error = Strings.Linux_X11_NoDisplay;
            return 0;
        }
        try
        {
            _ = s_threads.Value;
            nint display = XOpenDisplay(name);
            if (display == 0) error = string.Format(Strings.Linux_X11_ConnectFailed, name);
            return display;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            error = Strings.Linux_X11_LibraryMissing;
            return 0;
        }
    }

    /// <summary>Hat der Server die XTest-Erweiterung (und ist libXtst vorhanden)?</summary>
    public static bool HasXTest(nint display)
    {
        try { return XTestQueryExtension(display, out _, out _, out _, out _) != 0; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    /// <summary>Liest eine Fenster-Eigenschaft mit 32-Bit-Werten (als long geliefert); leer, wenn es sie nicht gibt.</summary>
    public static nint[] GetLongProperty(nint display, nint window, string property, nint type)
    {
        nint atom = XInternAtom(display, property, 0);
        nint[] result = [];
        X11Errors.Trap(display, () =>
        {
            if (XGetWindowProperty(display, window, atom, 0, 1024, 0, type, out _, out int format, out nuint items, out _, out nint prop) != 0
                || prop == 0) return;
            try
            {
                if (format == 32 && items > 0)
                {
                    result = new nint[(int)items];
                    for (int i = 0; i < result.Length; i++) result[i] = ((nint*)prop)[i];
                }
            }
            finally
            {
                XFree((void*)prop);
            }
        });
        return result;
    }
}

/// <summary>
/// Fängt X-Fehler (BadAccess beim Anmelden eines belegten Kürzels, BadWindow bei einem inzwischen geschlossenen Fenster)
/// ab, statt den Prozess über den Standard-Handler von Xlib zu beenden. Der vorherige Handler (z. B. der von Avalonia)
/// wird danach wieder eingesetzt.
/// </summary>
internal static unsafe class X11Errors
{
    private static readonly Lock Gate = new();
    private static nint s_display;
    private static int s_error;

    /// <summary>Führt die Aufrufe aus, wartet auf die Antwort des Servers und liefert den ersten Fehlercode (0 = keiner).</summary>
    public static int Trap(nint display, Action action)
    {
        lock (Gate)
        {
            s_display = display;
            s_error = 0;
            nint previous = X11.XSetErrorHandler((nint)(delegate* unmanaged<nint, XErrorEvent*, int>)&Handler);
            try
            {
                action();
                X11.XSync(display, 0);
                return s_error;
            }
            finally
            {
                X11.XSetErrorHandler(previous);
                s_display = 0;
            }
        }
    }

    [UnmanagedCallersOnly]
    private static int Handler(nint display, XErrorEvent* error)
    {
        if (display == s_display && s_error == 0) s_error = error->error_code;
        return 0;
    }
}
