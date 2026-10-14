using Lupe.Core.Input;
using Lupe.Core.Model;

namespace Lupe.Tests.Input;

public sealed class HotkeyTextTests
{
    private static Hotkey Key(int vk, bool ctrl = false, bool alt = false, bool shift = false, bool win = false)
        => new(Ctrl: ctrl, Alt: alt, Shift: shift, Win: win, Key: vk);

    [Fact]
    public void Beispiele_aus_der_Beschreibung()
    {
        Assert.Equal("Strg+Alt+Q", HotkeyText.Format(Hotkey.Default));
        Assert.Equal("Strg+Umschalt+F9", HotkeyText.Format(Key(0x78, ctrl: true, shift: true)));
        Assert.Equal("Win+Alt+S", HotkeyText.Format(Key(0x53, alt: true, win: true)));
    }

    [Fact]
    public void Reihenfolge_der_Umschalttasten_ist_Win_Strg_Umschalt_Alt()
        => Assert.Equal("Win+Strg+Umschalt+Alt+A", HotkeyText.Format(Key(0x41, true, true, true, true)));

    [Theory]
    [InlineData(0x2C, "Druck")]
    [InlineData(0x13, "Pause")]
    [InlineData(0x2D, "Einfg")]
    [InlineData(0x2E, "Entf")]
    [InlineData(0x24, "Pos1")]
    [InlineData(0x23, "Ende")]
    [InlineData(0x21, "Bild auf")]
    [InlineData(0x22, "Bild ab")]
    [InlineData(0x20, "Leertaste")]
    [InlineData(0x0D, "Eingabe")]
    [InlineData(0x1B, "Esc")]
    [InlineData(0x08, "Rücktaste")]
    [InlineData(0x09, "Tab")]
    [InlineData(0x25, "Pfeil links")]
    [InlineData(0x91, "Rollen")]
    [InlineData(0x70, "F1")]
    [InlineData(0x87, "F24")]
    [InlineData(0x30, "0")]
    [InlineData(0x39, "9")]
    [InlineData(0x60, "Num 0")]
    [InlineData(0x69, "Num 9")]
    [InlineData(0x6B, "Num Plus")]
    [InlineData(0xBB, "Plus")]
    [InlineData(0xBC, "Komma")]
    [InlineData(0xBD, "Minus")]
    [InlineData(0xBE, "Punkt")]
    [InlineData(0x07, "Taste 0x07")]
    [InlineData(0xFE, "Taste 0xFE")]
    public void Deutsche_Tastennamen(int vk, string name)
        => Assert.Equal("Strg+" + name, HotkeyText.Format(Key(vk, ctrl: true)));

    [Theory]
    [InlineData("Strg+Alt+Q", true, true, false, false, 0x51)]
    [InlineData("ctrl+alt+q", true, true, false, false, 0x51)]
    [InlineData("  Strg +  Alt + Q ", true, true, false, false, 0x51)]
    [InlineData("STRG+ALT+Q", true, true, false, false, 0x51)]
    [InlineData("Control+Alt+Q", true, true, false, false, 0x51)]
    [InlineData("Shift+Ctrl+F9", true, false, true, false, 0x78)]
    [InlineData("Umschalt+Strg+f9", true, false, true, false, 0x78)]
    [InlineData("Win+Alt+S", false, true, false, true, 0x53)]
    [InlineData("Windows+Alt+s", false, true, false, true, 0x53)]
    [InlineData("Ctrl+PrintScreen", true, false, false, false, 0x2C)]
    [InlineData("Strg+Druck", true, false, false, false, 0x2C)]
    [InlineData("Druck", false, false, false, false, 0x2C)]
    [InlineData("PrtScn", false, false, false, false, 0x2C)]
    [InlineData("Strg+Pause", true, false, false, false, 0x13)]
    [InlineData("Ctrl+Insert", true, false, false, false, 0x2D)]
    [InlineData("Strg+Einfg", true, false, false, false, 0x2D)]
    [InlineData("Strg+Entf", true, false, false, false, 0x2E)]
    [InlineData("Ctrl+Del", true, false, false, false, 0x2E)]
    [InlineData("Strg+Alt+Bild auf", true, true, false, false, 0x21)]
    [InlineData("Strg+Alt+bildauf", true, true, false, false, 0x21)]
    [InlineData("Ctrl+PgUp", true, false, false, false, 0x21)]
    [InlineData("Strg+Pos1", true, false, false, false, 0x24)]
    [InlineData("Ctrl+Home", true, false, false, false, 0x24)]
    [InlineData("Strg+Leertaste", true, false, false, false, 0x20)]
    [InlineData("Ctrl+Space", true, false, false, false, 0x20)]
    [InlineData("Strg+F24", true, false, false, false, 0x87)]
    [InlineData("F13", false, false, false, false, 0x7C)]
    [InlineData("Strg+7", true, false, false, false, 0x37)]
    [InlineData("Strg++", true, false, false, false, 0xBB)]
    [InlineData("Strg + +", true, false, false, false, 0xBB)]
    [InlineData("Strg+Plus", true, false, false, false, 0xBB)]
    [InlineData("Strg+-", true, false, false, false, 0xBD)]
    [InlineData("Strg+,", true, false, false, false, 0xBC)]
    [InlineData("Strg+Num 5", true, false, false, false, 0x65)]
    [InlineData("Strg+Numpad5", true, false, false, false, 0x65)]
    [InlineData("Strg+Num +", true, false, false, false, 0x6B)]
    [InlineData("Strg+Num Plus", true, false, false, false, 0x6B)]
    [InlineData("Strg+Num*", true, false, false, false, 0x6A)]
    [InlineData("Strg+Taste 0xBA", true, false, false, false, 0xBA)]
    [InlineData("Strg+0x41", true, false, false, false, 0x41)]
    [InlineData("AltGr+E", true, true, false, false, 0x45)]
    [InlineData("Strg+Strg+Q", true, false, false, false, 0x51)]
    [InlineData("Q+Strg", true, false, false, false, 0x51)]
    public void TryParse_ist_tolerant(string text, bool ctrl, bool alt, bool shift, bool win, int vk)
    {
        Assert.True(HotkeyText.TryParse(text, out var hotkey), text);
        Assert.Equal(new Hotkey(ctrl, alt, shift, win, vk), hotkey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Strg+Alt")]
    [InlineData("Strg+")]
    [InlineData("+Q")]
    [InlineData("Strg++Q")]
    [InlineData("Strg+Q+W")]
    [InlineData("Strg+Hallo")]
    [InlineData("Strg+F0")]
    [InlineData("Strg+F25")]
    [InlineData("Strg+Taste 0x00")]
    [InlineData("Strg+Taste 0xFF")]
    [InlineData("Strg+0x1FF")]
    [InlineData("Strg+Taste 0xZZ")]
    [InlineData("Strg Alt Q")]
    public void TryParse_lehnt_Unsinn_ab(string text)
    {
        Assert.False(HotkeyText.TryParse(text, out var hotkey), text);
        Assert.Null(hotkey);
    }

    [Fact]
    public void TryParse_mit_null_ist_false()
        => Assert.False(HotkeyText.TryParse(null!, out _));

    [Fact]
    public void Rundlauf_fuer_jeden_Tastencode_und_alle_Umschalttasten()
    {
        for (int vk = 1; vk <= 0xFE; vk++)
        {
            for (int mods = 0; mods < 16; mods++)
            {
                var hotkey = new Hotkey((mods & 1) != 0, (mods & 2) != 0, (mods & 4) != 0, (mods & 8) != 0, vk);
                string text = HotkeyText.Format(hotkey);
                Assert.True(HotkeyText.TryParse(text, out var parsed), $"„{text}“ (0x{vk:X2}) lässt sich nicht einlesen");
                Assert.Equal(hotkey, parsed);
            }
        }
    }

    [Fact]
    public void OEM_Tasten_zeigen_das_Zeichen_des_Tastaturlayouts_und_kommen_wieder_zurueck()
    {
        foreach (int vk in new[] { 0xBA, 0xBF, 0xC0, 0xDB, 0xDC, 0xDD, 0xDE, 0xDF, 0xE2 })
        {
            string name = HotkeyText.KeyName(vk);
            Assert.DoesNotContain("+", name);
            Assert.True(HotkeyText.TryParse("Strg+" + name, out var parsed), name);
            Assert.Equal(vk, parsed.Key);
        }
    }

    [GermanKeyboardFact]
    public void Deutsches_Layout_zeigt_Umlaute_und_Sonderzeichen()
    {
        Assert.Equal("Strg+Ü", HotkeyText.Format(Key(0xBA, ctrl: true)));
        Assert.Equal("Strg+Ö", HotkeyText.Format(Key(0xC0, ctrl: true)));
        Assert.Equal("Strg+Ä", HotkeyText.Format(Key(0xDE, ctrl: true)));
        Assert.Equal("Strg+#", HotkeyText.Format(Key(0xBF, ctrl: true)));
        Assert.Equal("Strg+ß", HotkeyText.Format(Key(0xDB, ctrl: true)));
        Assert.True(HotkeyText.TryParse("strg+ü", out var hotkey));
        Assert.Equal(0xBA, hotkey.Key);
    }

    [Fact]
    public void Format_mit_null_ist_ein_Programmierfehler()
        => Assert.Throws<ArgumentNullException>(() => HotkeyText.Format(null!));

    [Theory]
    [InlineData(true, true, false, false, 0x51)]   // Strg+Alt+Q
    [InlineData(false, false, false, true, 0x53)]  // Win+S
    [InlineData(false, true, false, false, 0x20)]  // Alt+Leertaste
    [InlineData(false, false, false, false, 0x78)] // F9 allein
    [InlineData(false, false, true, false, 0x78)]  // Umschalt+F9
    [InlineData(false, false, false, false, 0x2C)] // Druck allein
    [InlineData(false, false, false, false, 0x13)] // Pause allein
    [InlineData(false, false, false, false, 0xB3)] // Medientaste Wiedergabe
    public void Geeignete_Kuerzel(bool ctrl, bool alt, bool shift, bool win, int vk)
        => Assert.Null(HotkeyText.Problem(new Hotkey(ctrl, alt, shift, win, vk)));

    [Theory]
    [InlineData(false, false, false, 0x51, "würde das normale Tippen stören")] // Q
    [InlineData(false, true, false, 0x51, "würde das normale Tippen stören")]  // Umschalt+Q
    [InlineData(false, false, false, 0x20, "würde das normale Tippen stören")] // Leertaste
    [InlineData(false, false, false, 0x2E, "würde das normale Tippen stören")] // Entf
    [InlineData(true, false, false, 0x11, "allein gehen nicht")]               // Strg+Strg
    [InlineData(true, false, false, 0xA4, "allein gehen nicht")]               // Strg+linke Alt
    [InlineData(true, false, false, 0x5B, "allein gehen nicht")]               // Strg+Win
    [InlineData(true, false, false, 0x01, "Maustasten")]
    [InlineData(true, false, false, 0, "Kein gültiges")]
    [InlineData(true, false, false, 0xFF, "Kein gültiges")]
    [InlineData(true, false, false, 300, "Kein gültiges")]
    public void Ungeeignete_Kuerzel_mit_deutscher_Begruendung(bool ctrl, bool shift, bool win, int vk, string part)
        => Assert.Contains(part, HotkeyText.Problem(new Hotkey(ctrl, false, shift, win, vk)));
}
