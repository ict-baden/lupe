// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Lupe.Core.Input;
using Lupe.Core.Model;
using Lupe.Core.Platform;
using Lupe.Core.Resources;
using Lupe.Platform.MacOS.Native;

namespace Lupe.Platform.MacOS.Input;

/// <summary>
/// Globale Tastenkürzel über Carbon <c>RegisterEventHotKey</c> – braucht keine Bedienungshilfen-Berechtigung. Win wird
/// zur Befehlstaste (⌘), Alt zur Wahltaste (⌥). Die Ereignisse kommen über die Ereignisschleife der App (Avalonia/Cocoa
/// auf dem Haupt-Thread); ohne laufende Schleife (Befehlszeile, Tests) kommt kein <see cref="IGlobalHotkey.Pressed"/>.
/// </summary>
public sealed class MacOSHotkeys : IGlobalHotkeyProvider
{
    /// <summary>Kennung der Kürzel von Lupe ('LUPE').</summary>
    internal const uint Signature = 0x4C555045;

    private static readonly ConcurrentDictionary<uint, MacHotkey> Registered = new();
    private static readonly Lock InstallGate = new();
    private static bool s_handlerInstalled;
    private static int s_nextId;

    private readonly MacOSKeyboardLayout _layout;

    public MacOSHotkeys() : this(new MacOSKeyboardLayout()) { }

    internal MacOSHotkeys(MacOSKeyboardLayout layout) => _layout = layout;

    public string? UnsupportedReason => OperatingSystem.IsMacOS() ? null : Strings.Mac_NotHere_Hotkeys;

    public IGlobalHotkey Register(Hotkey hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        if (UnsupportedReason is { } reason) return new FailedHotkey(reason);
        if (_layout.KeyCodeFor(hotkey.Key) is not { } keyCode)
            return new FailedHotkey(string.Format(Strings.Mac_Hotkey_KeyMissing, HotkeyText.Format(hotkey)));
        if (MacKeyCodes.CarbonModifiers(hotkey) == 0)
            return new FailedHotkey(Strings.Mac_Hotkey_NeedsModifier);
        try
        {
            if (!EnsureHandler(out string? handlerError)) return new FailedHotkey(handlerError!);
            uint id = (uint)Interlocked.Increment(ref s_nextId);
            var registration = new MacHotkey(id);
            Registered[id] = registration;
            nint hotKeyRef;
            int status;
            unsafe
            {
                status = Carbon.RegisterEventHotKey(keyCode, MacKeyCodes.CarbonModifiers(hotkey),
                    new EventHotKeyID { Signature = Signature, Id = id }, Carbon.GetApplicationEventTarget(), 0, &hotKeyRef);
            }
            if (status != Carbon.noErr)
            {
                Registered.TryRemove(id, out _);
                return new FailedHotkey(DescribeStatus(status, hotkey));
            }
            registration.Attach(hotKeyRef);
            return registration;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Trace.TraceWarning($"Lupe: Carbon nicht verfügbar: {ex.Message}");
            return new FailedHotkey(Strings.Mac_Hotkey_CarbonMissing);
        }
    }

    internal static string DescribeStatus(int status, Hotkey hotkey) => status switch
    {
        Carbon.eventHotKeyExistsErr =>
            string.Format(Strings.Mac_Hotkey_AlreadyRegistered, HotkeyText.Format(hotkey)),
        Carbon.eventHotKeyInvalidErr => string.Format(Strings.Mac_Hotkey_Invalid, HotkeyText.Format(hotkey)),
        _ => string.Format(Strings.Mac_Hotkey_RegisterFailed, HotkeyText.Format(hotkey), status),
    };

    private static unsafe bool EnsureHandler(out string? error)
    {
        error = null;
        lock (InstallGate)
        {
            if (s_handlerInstalled) return true;
            var spec = new EventTypeSpec { EventClass = Carbon.kEventClassKeyboard, EventKind = Carbon.kEventHotKeyPressed };
            nint handlerRef;
            int status = Carbon.InstallEventHandler(Carbon.GetApplicationEventTarget(), &OnHotKeyEvent, 1, &spec, 0, &handlerRef);
            if (status != Carbon.noErr)
            {
                error = string.Format(Strings.Mac_Hotkey_HandlerFailed, status);
                return false;
            }
            // Der Handler bleibt für die Laufzeit des Programms installiert
            s_handlerInstalled = true;
            return true;
        }
    }

    [UnmanagedCallersOnly]
    private static unsafe int OnHotKeyEvent(nint callRef, nint evt, nint userData)
    {
        try
        {
            EventHotKeyID id;
            int status = Carbon.GetEventParameter(evt, Carbon.kEventParamDirectObject, Carbon.typeEventHotKeyID, null,
                (nuint)sizeof(EventHotKeyID), null, &id);
            if (status == Carbon.noErr && id.Signature == Signature && Registered.TryGetValue(id.Id, out var hotkey))
            {
                // Nicht im Ereignis-Thread der App arbeiten (UI), sondern im Hintergrund melden
                ThreadPool.QueueUserWorkItem(static h => h.RaisePressed(), hotkey, preferLocal: false);
                return Carbon.noErr;
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Lupe: Fehler im Tastenkürzel-Handler: {ex}");
        }
        return -9874; // eventNotHandledErr: an andere Handler weiterreichen
    }

    private sealed class MacHotkey(uint id) : IGlobalHotkey
    {
        private nint _ref;
        private volatile bool _disposed;

        public event EventHandler? Pressed;
        public bool IsRegistered => _ref != 0 && !_disposed;
        public string? Error => null;

        public void Attach(nint hotKeyRef) => _ref = hotKeyRef;

        public void RaisePressed()
        {
            if (_disposed) return;
            var handler = Pressed;
            if (handler is null) return;
            foreach (var single in handler.GetInvocationList())
            {
                try
                {
                    ((EventHandler)single)(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"Lupe: Fehler im Empfänger des Tastenkürzels: {ex}");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Registered.TryRemove(id, out _);
            nint r = Interlocked.Exchange(ref _ref, 0);
            if (r == 0) return;
            try
            {
                Carbon.UnregisterEventHotKey(r);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
            }
        }
    }
}
