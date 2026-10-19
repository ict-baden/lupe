// SPDX-License-Identifier: MIT
using Avalonia.Input;

namespace Lupe.UI;

/// <summary>
/// Avalonia-Taste → Windows-Virtual-Key-Code, die gemeinsame Sprache für Tastenkürzel (<see cref="Core.Model.Hotkey.Key"/>);
/// die Plattformen rechnen ihn in ihre Tasten um. Wie bisher unter Windows nach dem Layout (die Z-Taste einer deutschen
/// Tastatur ist Z), so wie Avalonia die Taste meldet.
/// </summary>
internal static class VirtualKeys
{
    public static int? From(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return 0x41 + (key - Key.A);
        if (key is >= Key.D0 and <= Key.D9) return 0x30 + (key - Key.D0);
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return 0x60 + (key - Key.NumPad0);
        if (key is >= Key.F1 and <= Key.F24) return 0x70 + (key - Key.F1);
        return key switch
        {
            Key.Space => 0x20,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.End => 0x23,
            Key.Home => 0x24,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.PrintScreen => 0x2C,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.Pause => 0x13,
            Key.Scroll => 0x91,
            Key.Back => 0x08,
            Key.Enter => 0x0D,
            Key.Multiply => 0x6A,
            Key.Add => 0x6B,
            Key.Subtract => 0x6D,
            Key.Decimal => 0x6E,
            Key.Divide => 0x6F,
            Key.OemSemicolon => 0xBA,
            Key.OemPlus => 0xBB,
            Key.OemComma => 0xBC,
            Key.OemMinus => 0xBD,
            Key.OemPeriod => 0xBE,
            Key.OemQuestion => 0xBF,
            Key.OemTilde => 0xC0,
            Key.OemOpenBrackets => 0xDB,
            Key.OemPipe => 0xDC,
            Key.OemCloseBrackets => 0xDD,
            Key.OemQuotes => 0xDE,
            Key.Oem8 => 0xDF,
            Key.OemBackslash => 0xE2,
            _ => null,
        };
    }

    public static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    /// <summary>Tasten, die ohne Strg/Alt/Win als Kürzel taugen.</summary>
    public static bool IsStandalone(Key key) => key is >= Key.F1 and <= Key.F24 or Key.PrintScreen or Key.Pause;
}
