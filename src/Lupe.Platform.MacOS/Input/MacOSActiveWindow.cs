using System.Diagnostics;
using System.Runtime.InteropServices;
using Lupe.Core.Platform;
using Lupe.Core.Resources;
using Lupe.Platform.MacOS.Native;

namespace Lupe.Platform.MacOS.Input;

/// <summary>
/// Das Programm im Vordergrund über <c>NSWorkspace.frontmostApplication</c>. Die Kennung ist die Prozess-ID: macOS gibt
/// den Tastaturfokus an ein Programm (dessen Hauptfenster), nicht an ein einzelnes fremdes Fenster.
/// </summary>
public sealed class MacOSActiveWindow : IActiveWindow
{
    /// <summary>NSApplicationActivateIgnoringOtherApps (ab macOS 14 veraltet, aber weiterhin wirksam, wenn Lupe vorn ist).</summary>
    private const nuint ActivateIgnoringOtherApps = 1 << 1;
    private static readonly TimeSpan FocusTimeout = TimeSpan.FromMilliseconds(1500);

    public bool IsSupported => OperatingSystem.IsMacOS();

    public nint Foreground()
    {
        if (!OperatingSystem.IsMacOS()) return 0;
        try
        {
            return FrontmostPid();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Trace.TraceWarning($"Lupe: Vordergrundprogramm nicht abfragbar: {ex.Message}");
            return 0;
        }
    }

    public bool IsOwnWindow(nint window) => window != 0 && window == Environment.ProcessId;

    public async Task<string?> ActivateAsync(nint window, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsMacOS())
            return Strings.Mac_ActiveWindow_NotHere;
        if (window == 0) return Strings.Mac_ActiveWindow_Unknown;
        try
        {
            if (FrontmostPid() == window) return null;
            if (!Activate((int)window)) return Strings.Mac_ActiveWindow_Gone;
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < FocusTimeout)
            {
                await Task.Delay(30, ct).ConfigureAwait(false);
                if (FrontmostPid() == window) return null;
            }
            return Strings.Mac_ActiveWindow_ActivateFailed;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return Strings.Mac_ActiveWindow_AppKitMissing;
        }
    }

    private static bool EnsureAppKit() => NativeLibrary.TryLoad(Lib.AppKit, out _);

    private static nint FrontmostPid()
    {
        if (!EnsureAppKit()) return 0;
        return ObjC.InPool(() =>
        {
            nint workspace = ObjC.SendPtr(ObjC.objc_getClass("NSWorkspace"), ObjC.sel_registerName("sharedWorkspace"));
            if (workspace == 0) return 0;
            nint app = ObjC.SendPtr(workspace, ObjC.sel_registerName("frontmostApplication"));
            return app == 0 ? 0 : (nint)ObjC.SendInt(app, ObjC.sel_registerName("processIdentifier"));
        });
    }

    private static bool Activate(int pid)
    {
        if (!EnsureAppKit()) return false;
        return ObjC.InPool(() =>
        {
            nint cls = ObjC.objc_getClass("NSRunningApplication");
            nint app = ObjC.SendPtrInt(cls, ObjC.sel_registerName("runningApplicationWithProcessIdentifier:"), pid);
            if (app == 0) return false;
            ObjC.SendByteNUInt(app, ObjC.sel_registerName("activateWithOptions:"), ActivateIgnoringOtherApps);
            return true;
        });
    }
}
