// SPDX-License-Identifier: MIT
using Lupe.Platform.MacOS.Native;

namespace Lupe.Platform.MacOS;

/// <summary>
/// Fenster, die dort erscheinen müssen, wo man gerade arbeitet: Auswahl des Bildschirm-Scans, Code-Auswahl und
/// Ergebnis-Hinweis. Ohne Angabe legt macOS ein neues Fenster auf den Schreibtisch (Space), zu dem die App gehört, und
/// lässt es nie in den Bereich einer App im Vollbild. Dann sah man auf einem zweiten Schreibtisch oder über Chrome im
/// Vollbild nach ⌥⌘Q nichts; die Auswahl wartete auf dem Schreibtisch des Lupe-Fensters.
/// </summary>
public static class MacOSWindows
{
    // NSWindowCollectionBehavior (AppKit, NSWindow.h). Aus jeder Gruppe höchstens ein Wert, sonst wirft AppKit eine
    // Ausnahme: CanJoinAllSpaces/MoveToActiveSpace, Default/Managed/Transient/Stationary, FullScreenPrimary/Auxiliary/None.
    internal const nuint CanJoinAllSpaces = 1 << 0;
    internal const nuint IgnoresCycle = 1 << 6;
    internal const nuint FullScreenAuxiliary = 1 << 8;

    /// <summary>Auf jedem Schreibtisch und über Apps im Vollbild, nicht in ⌘` (Fensterwechsel).</summary>
    internal const nuint OnEverySpace = CanJoinAllSpaces | FullScreenAuxiliary | IgnoresCycle;

    /// <summary>
    /// Lässt das Fenster auf dem gerade sichtbaren Schreibtisch erscheinen, auch über einer App im Vollbild. Vor dem
    /// ersten Zeigen und nach dem Öffnen aufrufen (Avalonia setzt beim Zeigen eigene Werte). Gibt zurück, ob der Wert gilt.
    /// </summary>
    /// <param name="nsWindow">Das <c>NSWindow</c> (Avalonia: <c>IMacOSTopLevelPlatformHandle.NSWindow</c>).</param>
    public static bool ShowOnEverySpace(nint nsWindow)
    {
        if (!OperatingSystem.IsMacOS() || nsWindow == 0) return false;
        ObjC.SendVoidNUInt(nsWindow, ObjC.sel_registerName("setCollectionBehavior:"), OnEverySpace);
        return ObjC.SendNUInt(nsWindow, ObjC.sel_registerName("collectionBehavior")) == OnEverySpace;
    }

    // NSApplicationActivationPolicy: Regular = mit Dock-Symbol, Accessory = Hilfs-App ohne Dock-Symbol
    private const nuint PolicyRegular = 0;
    private const nuint PolicyAccessory = 1;
    private static int _overOtherApps;

    /// <summary>
    /// Bis zum passenden <see cref="EndOverOtherApps"/> läuft Lupe als Hilfs-App ohne Dock-Symbol. Nur dann lässt macOS
    /// ihre Fenster in den Bereich einer fremden App im Vollbild; die Markierung aus <see cref="ShowOnEverySpace"/> allein
    /// reicht für eine App mit Dock-Symbol nicht. Aufrufe zählen mit (Scan und Hinweis überlappen); nur im UI-Thread.
    /// </summary>
    public static void BeginOverOtherApps()
    {
        if (!OperatingSystem.IsMacOS()) return;
        if (_overOtherApps++ == 0) SetActivationPolicy(PolicyAccessory);
    }

    /// <summary>Gegenstück zu <see cref="BeginOverOtherApps"/>: Nach dem letzten kommt das Dock-Symbol zurück.</summary>
    public static void EndOverOtherApps()
    {
        if (!OperatingSystem.IsMacOS() || _overOtherApps == 0) return;
        if (--_overOtherApps == 0) SetActivationPolicy(PolicyRegular);
    }

    private static void SetActivationPolicy(nuint policy)
    {
        var app = ObjC.SendPtr(ObjC.objc_getClass("NSApplication"), ObjC.sel_registerName("sharedApplication"));
        if (app != 0) ObjC.SendByteNUInt(app, ObjC.sel_registerName("setActivationPolicy:"), policy);
    }
}
