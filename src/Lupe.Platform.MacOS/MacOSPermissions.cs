// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Runtime.InteropServices;
using Lupe.Core.Platform;
using Lupe.Core.Resources;
using Lupe.Platform.MacOS.Native;

namespace Lupe.Platform.MacOS;

/// <summary>Zustand einer Kamera-Berechtigung (Werte wie <c>AVAuthorizationStatus</c>).</summary>
internal enum CameraAuthorization
{
    NotDetermined = 0,
    Restricted = 1,
    Denied = 2,
    Authorized = 3,
    /// <summary>Konnte nicht abgefragt werden (kein macOS, AVFoundation fehlt).</summary>
    Unknown = -1,
}

/// <summary>
/// Die drei Berechtigungen, die Lupe unter macOS braucht: Kamera, Bildschirmaufnahme, Bedienungshilfen (zum Tippen).
/// Abfragen fragen nie nach; nur die ausdrücklichen <c>Request…</c>-Methoden zeigen den Systemdialog.
/// </summary>
internal static class MacOSPermissions
{
    public const string CameraSettings = "x-apple.systempreferences:com.apple.preference.security?Privacy_Camera";
    public const string ScreenSettings = "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture";
    public const string AccessibilitySettings = "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility";

    public static string CameraDenied => Strings.Mac_Camera_Denied;
    public static string CameraRestricted => Strings.Mac_Camera_Restricted;
    public static string ScreenDenied => Strings.Mac_Screen_Denied;
    public static string AccessibilityDenied => Strings.Mac_Typing_AccessibilityDenied;

    private static int s_screenRequested, s_accessibilityRequested;

    /// <summary><c>+[AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeVideo]</c>; fragt nie nach.</summary>
    public static CameraAuthorization Camera()
    {
        if (!OperatingSystem.IsMacOS()) return CameraAuthorization.Unknown;
        try
        {
            if (!NativeLibrary.TryLoad(Lib.AVFoundation, out _)) return CameraAuthorization.Unknown;
            nint mediaType = Lib.ReadPointerExport(Lib.AVFoundation, "AVMediaTypeVideo");
            nint cls = ObjC.objc_getClass("AVCaptureDevice");
            if (mediaType == 0 || cls == 0) return CameraAuthorization.Unknown;
            nint status = ObjC.SendPtrPtr(cls, ObjC.sel_registerName("authorizationStatusForMediaType:"), mediaType);
            return status is >= 0 and <= 3 ? (CameraAuthorization)(int)status : CameraAuthorization.Unknown;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Trace.TraceWarning($"Lupe: Kamera-Berechtigung nicht abfragbar: {ex.Message}");
            return CameraAuthorization.Unknown;
        }
    }

    /// <summary><c>CGPreflightScreenCaptureAccess</c>; fragt nie nach. Unbekannt → true (dann scheitert ggf. die Aufnahme selbst).</summary>
    public static bool ScreenCapture()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        try
        {
            return CG.CGPreflightScreenCaptureAccess() != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return true; // vor macOS 10.15 gab es die Berechtigung nicht
        }
    }

    /// <summary>Zeigt einmal je Programmlauf den Systemdialog für die Bildschirmaufnahme.</summary>
    public static void RequestScreenCapture()
    {
        if (!OperatingSystem.IsMacOS() || Interlocked.Exchange(ref s_screenRequested, 1) != 0) return;
        try
        {
            CG.CGRequestScreenCaptureAccess();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary><c>AXIsProcessTrusted</c>: darf Lupe Tastatur-Ereignisse an andere Programme senden? Fragt nie nach.</summary>
    public static bool Accessibility()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        try
        {
            return AX.AXIsProcessTrusted() != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Zeigt einmal je Programmlauf den Systemdialog „Bedienungshilfen“ (<c>AXIsProcessTrustedWithOptions</c> mit Prompt).</summary>
    public static void RequestAccessibility()
    {
        if (!OperatingSystem.IsMacOS() || Interlocked.Exchange(ref s_accessibilityRequested, 1) != 0) return;
        try
        {
            nint key = AX.TrustedCheckOptionPrompt, yes = CF.True;
            if (key == 0 || yes == 0) return;
            nint options = CF.Dictionary([key], [yes]);
            try
            {
                AX.AXIsProcessTrustedWithOptions(options);
            }
            finally
            {
                if (options != 0) CF.CFRelease(options);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>Meldung zur Kamera-Berechtigung (null = nichts zu melden).</summary>
    public static string? CameraMessage(CameraAuthorization status) => status switch
    {
        CameraAuthorization.Denied => CameraDenied,
        CameraAuthorization.Restricted => CameraRestricted,
        _ => null,
    };
}

/// <summary>Berechtigungen unter macOS: Kamera, Bildschirmaufnahme, Bedienungshilfen.</summary>
public sealed class MacOSProblems : IPlatformProblems
{
    public IReadOnlyList<PlatformProblem> Check()
    {
        if (!OperatingSystem.IsMacOS()) return [];
        return Collect(MacOSPermissions.Camera(), MacOSPermissions.ScreenCapture(), MacOSPermissions.Accessibility());
    }

    /// <summary>Aus den Einzelzuständen die Liste der Hindernisse (testbar ohne Mac).</summary>
    internal static IReadOnlyList<PlatformProblem> Collect(CameraAuthorization camera, bool screen, bool accessibility)
    {
        var list = new List<PlatformProblem>();
        if (MacOSPermissions.CameraMessage(camera) is { } cameraMessage)
            list.Add(new PlatformProblem(PlatformFeature.Camera, cameraMessage, MacOSPermissions.CameraSettings));
        if (!screen)
            list.Add(new PlatformProblem(PlatformFeature.Screen, MacOSPermissions.ScreenDenied, MacOSPermissions.ScreenSettings));
        if (!accessibility)
            list.Add(new PlatformProblem(PlatformFeature.Typing, MacOSPermissions.AccessibilityDenied, MacOSPermissions.AccessibilitySettings));
        return list;
    }
}
