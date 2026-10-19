// SPDX-License-Identifier: MIT
using System.Diagnostics;
using Lupe.Core.Platform;
using Lupe.Platform.MacOS.Native;

namespace Lupe.Platform.MacOS.Input;

/// <summary>
/// Das aktuelle Tastaturlayout über <c>UCKeyTranslate</c>. Die Text-Input-Source-Funktionen (TIS) dürfen unter neueren
/// macOS-Versionen nur auf dem Haupt-Thread laufen; deshalb wird die Tabelle nur dort gelesen (Avalonia-UI-Thread) und
/// sonst die zuletzt gelesene benutzt.
/// </summary>
public sealed class MacOSKeyboardLayout : IKeyboardLayout
{
    private const uint ShiftState = Carbon.shiftKey >> 8 & 0xFF;
    private const uint OptionState = Carbon.optionKey >> 8 & 0xFF;
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();
    private LayoutTable? _table;
    private long _readAt;

    public char? KeyChar(int virtualKey)
    {
        var table = Table();
        if (table is null || KeyCodeFor(virtualKey, table) is not { } code) return null;
        return table.Plain.TryGetValue(code, out var s) && s.Length == 1 ? s[0] : null;
    }

    public int? KeyForChar(char c)
    {
        var table = Table();
        if (table is null) return null;
        foreach (var (code, text) in table.Plain)
        {
            if (text.Length != 1 || text[0] != c) continue;
            // Buchstaben und Ziffern behalten ihren Virtual-Key (= Großbuchstabe/Ziffer), wie unter Windows
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9') return char.ToUpperInvariant(c);
            return MacKeyCodes.ToVirtualKey(code);
        }
        return null;
    }

    /// <summary>macOS meldet die Wahltaste nicht als Strg+Alt – keine AltGr-Kollision.</summary>
    public bool AltGrIsCtrlAlt => false;

    /// <summary>Was Wahltaste (+ Umschalt) + Taste schreibt.</summary>
    public string? AltGrText(int virtualKey, bool shift)
    {
        var table = Table();
        if (table is null || KeyCodeFor(virtualKey, table) is not { } code) return null;
        var map = shift ? table.ShiftOption : table.Option;
        return map.TryGetValue(code, out var s) && s.Length > 0 && !char.IsControl(s[0]) ? s : null;
    }

    /// <summary>
    /// Der macOS-Tastencode für ein Tastenkürzel: Buchstaben und Ziffern über das Layout (auf deutschen Tastaturen liegt
    /// „Z“ dort, wo US-Tastaturen „Y“ haben), alles andere nach Position.
    /// </summary>
    internal ushort? KeyCodeFor(int virtualKey) => KeyCodeFor(virtualKey, Table());

    private static ushort? KeyCodeFor(int vk, LayoutTable? table)
    {
        if (table is not null && vk is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            char wanted = char.ToLowerInvariant((char)vk);
            foreach (var (code, text) in table.Plain)
                if (text.Length == 1 && char.ToLowerInvariant(text[0]) == wanted && MacKeyCodes.ToVirtualKey(code) is not (>= 0x60 and <= 0x6F))
                    return code;
        }
        return MacKeyCodes.FromVirtualKey(vk);
    }

    private LayoutTable? Table()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        lock (_gate)
        {
            bool stale = _table is null || Stopwatch.GetElapsedTime(_readAt) > RefreshAfter;
            if (stale && IsMainThreadSafe())
            {
                var fresh = Read();
                if (fresh is not null) _table = fresh;
                _readAt = Stopwatch.GetTimestamp();
            }
            return _table;
        }
    }

    private static bool IsMainThreadSafe()
    {
        try
        {
            return LibSystem.IsMainThread;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private sealed record LayoutTable(
        IReadOnlyDictionary<ushort, string> Plain, IReadOnlyDictionary<ushort, string> Option, IReadOnlyDictionary<ushort, string> ShiftOption);

    private static unsafe LayoutTable? Read()
    {
        nint source = 0;
        try
        {
            source = Carbon.TISCopyCurrentKeyboardLayoutInputSource();
            if (source == 0) return null;
            nint data = Carbon.TISGetInputSourceProperty(source, Carbon.PropertyUnicodeKeyLayoutData);
            if (data == 0) return null;
            nint layout = CF.CFDataGetBytePtr(data);
            if (layout == 0) return null;
            uint kbdType = Carbon.LMGetKbdType();

            var plain = new Dictionary<ushort, string>();
            var option = new Dictionary<ushort, string>();
            var shiftOption = new Dictionary<ushort, string>();
            char* buffer = stackalloc char[8];
            for (ushort code = 0; code < 0x80; code++)
            {
                if (Translate(layout, code, 0, kbdType, buffer) is { } p) plain[code] = p;
                if (Translate(layout, code, OptionState, kbdType, buffer) is { } o) option[code] = o;
                if (Translate(layout, code, OptionState | ShiftState, kbdType, buffer) is { } so) shiftOption[code] = so;
            }
            return new LayoutTable(plain, option, shiftOption);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Trace.TraceWarning($"Lupe: Tastaturlayout nicht lesbar: {ex.Message}");
            return null;
        }
        finally
        {
            if (source != 0) CF.CFRelease(source);
        }
    }

    private static unsafe string? Translate(nint layout, ushort code, uint modifiers, uint kbdType, char* buffer)
    {
        uint deadKeyState = 0;
        nuint length = 0;
        // kUCKeyActionDisplay liefert bei Tottasten deren Zeichen (z. B. „´“) statt nichts
        int status = CoreServices.UCKeyTranslate(layout, code, CoreServices.kUCKeyActionDisplay, modifiers, kbdType,
            CoreServices.kUCKeyTranslateNoDeadKeysMask, &deadKeyState, 8, &length, buffer);
        if (status != 0 || length == 0) return null;
        var s = new string(buffer, 0, (int)length);
        return s.Length == 1 && char.IsControl(s[0]) ? null : s;
    }
}
