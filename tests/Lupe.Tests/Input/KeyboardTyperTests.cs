// SPDX-License-Identifier: MIT
using System.Runtime.CompilerServices;
using Lupe.Core.Input;

namespace Lupe.Tests.Input;

/// <summary>Die INPUT-Folge, die KeyboardTyper an SendInput gibt.</summary>
public sealed class KeyboardTyperInputTests
{
    [Fact]
    public void Einfache_Zeichen_als_Unicode_Druck_und_Loslassen()
    {
        var inputs = KeyboardTyper.BuildInputs("Ab1", "");

        Assert.Equal(Keys.Chars("Ab1"), Keys.Describe(inputs));
        Assert.All(inputs, i => Assert.Equal(0, i.VirtualKey)); // bei KEYEVENTF_UNICODE muss der Tastencode 0 sein
#if WINDOWS
        Assert.All(inputs, e =>
        {
            var i = Lupe.Platform.Windows.Input.WindowsKeyboard.ToInput(e);
            Assert.Equal(Lupe.Platform.Windows.Input.Native.INPUT_KEYBOARD, i.type);
            Assert.Equal(0, i.u.ki.wVk);
            Assert.Equal(0u, i.u.ki.time);
            Assert.Equal(0, i.u.ki.dwExtraInfo);
        });
#endif
    }

    [Fact]
    public void Umlaute_Euro_und_Akzente_gehen_unabhaengig_vom_Tastaturlayout()
    {
        var inputs = KeyboardTyper.BuildInputs("Grüße €5 ñ Ω", "");
        Assert.Equal(Keys.Chars("Grüße €5 ñ Ω"), Keys.Describe(inputs));
    }

    [Fact]
    public void Emoji_als_Surrogatpaar_beide_Haelften_druecken_dann_beide_loslassen()
    {
        var inputs = KeyboardTyper.BuildInputs("a😀b", "");

        Assert.Equal(
            [.. Keys.Chars("a"), "↓U+D83D", "↓U+DE00", "↑U+D83D", "↑U+DE00", .. Keys.Chars("b")],
            Keys.Describe(inputs));
    }

    [Fact]
    public void Text_mit_Emoji_Umlaut_und_Zeilenumbruch()
    {
        var inputs = KeyboardTyper.BuildInputs("Hä 👍🏽\r\nZeile\t2", "Enter");

        Assert.Equal(
        [
            .. Keys.Chars("Hä "),
            "↓U+D83D", "↓U+DC4D", "↑U+D83D", "↑U+DC4D", // 👍
            "↓U+D83C", "↓U+DFFD", "↑U+D83C", "↑U+DFFD", // Hautfarbe als eigenes Zeichen
            .. Keys.Enter,
            .. Keys.Chars("Zeile"),
            .. Keys.Tab,
            .. Keys.Chars("2"),
            .. Keys.Enter, // Suffix
        ], Keys.Describe(inputs));
    }

    [Theory]
    [InlineData("a\r\nb")]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    [InlineData("a\u0085b")]
    [InlineData("a\u2028b")]
    [InlineData("a\u2029b")]
    public void Jede_Art_Zeilenumbruch_wird_genau_einmal_Enter(string text)
        => Assert.Equal([.. Keys.Chars("a"), .. Keys.Enter, .. Keys.Chars("b")], Keys.Describe(KeyboardTyper.BuildInputs(text, "")));

    [Fact]
    public void Leerzeilen_bleiben_erhalten()
        => Assert.Equal([.. Keys.Enter, .. Keys.Enter, .. Keys.Enter], Keys.Describe(KeyboardTyper.BuildInputs("\r\n\n\r", "")));

    [Fact]
    public void Enter_und_Tab_mit_Scancode_ohne_Unicode_Flag()
    {
        var inputs = KeyboardTyper.BuildInputs("\n\t", "");

        Assert.Equal(KeyboardTyper.VK_RETURN, inputs[0].VirtualKey);
        Assert.Equal(0x1C, inputs[0].Scan);
        Assert.Equal(KeyFlags.None, inputs[0].Flags);
        Assert.Equal(KeyFlags.Up, inputs[1].Flags);
        Assert.Equal(KeyboardTyper.VK_TAB, inputs[2].VirtualKey);
        Assert.Equal(0x0F, inputs[2].Scan);
    }

    [Fact]
    public void Nicht_tippbare_Steuerzeichen_werden_ausgelassen()
    {
        // GS (0x1D) trennt Felder in GS1-Codes, dazu NUL, BEL, ESC, DEL und C1-Steuerzeichen
        var inputs = KeyboardTyper.BuildInputs("01\u001D10\0A\u0007B\u001BC\u007FD\u0090E", "");
        Assert.Equal(Keys.Chars("0110ABCDE"), Keys.Describe(inputs));
    }

    [Fact]
    public void Halbe_Surrogatpaare_werden_zum_Ersatzzeichen()
    {
        var inputs = KeyboardTyper.BuildInputs("a\uD83Db\uDE00", "");
        Assert.Equal(Keys.Chars("a" + (char)0xFFFD + "b" + (char)0xFFFD), Keys.Describe(inputs));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    [InlineData("Enter", 0x0D)]
    [InlineData("enter", 0x0D)]
    [InlineData(" Eingabe ", 0x0D)]
    [InlineData("RETURN", 0x0D)]
    [InlineData("Tab", 0x09)]
    [InlineData("tabulator", 0x09)]
    [InlineData("Leertaste", 0)] // unbekannt: nichts
    [InlineData("Enter\n", 0x0D)]
    public void Suffix(string? suffix, int expectedVk)
    {
        var inputs = KeyboardTyper.BuildInputs("x", suffix);

        if (expectedVk == 0)
        {
            Assert.Equal(Keys.Chars("x"), Keys.Describe(inputs));
        }
        else
        {
            Assert.Equal(4, inputs.Length);
            Assert.Equal(expectedVk, inputs[2].VirtualKey);
            Assert.Equal(expectedVk, inputs[3].VirtualKey);
        }
    }

    [Fact]
    public void Nur_Suffix_ohne_Text()
        => Assert.Equal(Keys.Tab, Keys.Describe(KeyboardTyper.BuildInputs("", "Tab")));

    [Fact]
    public void Pakete_trennen_nie_ein_Surrogatpaar_und_ergeben_zusammen_die_ganze_Folge()
    {
        var text = string.Concat(Enumerable.Range(0, 700).Select(i => i % 3 == 0 ? "😀" : i % 7 == 0 ? "\r\n" : "ä"));
        var inputs = KeyboardTyper.BuildInputs(text, "Enter");

        var batches = KeyboardTyper.Batches(inputs, KeyboardTyper.MaxInputsPerBatch);

        Assert.True(batches.Count > 1);
        int expectedOffset = 0;
        foreach (var (offset, count) in batches)
        {
            Assert.Equal(expectedOffset, offset);
            Assert.InRange(count, 1, KeyboardTyper.MaxInputsPerBatch);
            int depth = 0;
            for (int i = offset; i < offset + count; i++)
            {
                depth += inputs[i].IsUp ? -1 : 1;
                Assert.True(depth >= 0);
            }
            Assert.Equal(0, depth); // nach jedem Paket ist keine Taste mehr gedrückt
            expectedOffset += count;
        }
        Assert.Equal(inputs.Length, expectedOffset);
    }

    [Fact]
    public void Kleine_Pakete_an_der_Grenze_eines_Surrogatpaars()
    {
        var inputs = KeyboardTyper.BuildInputs("a😀b", "");
        Assert.Equal([(0, 2), (2, 4), (6, 2)], KeyboardTyper.Batches(inputs, 4));
    }

    [Fact]
    public void Kurzer_Text_ist_ein_Paket()
    {
        var inputs = KeyboardTyper.BuildInputs("Hallo Welt", "Tab");
        Assert.Equal([(0, inputs.Length)], KeyboardTyper.Batches(inputs, KeyboardTyper.MaxInputsPerBatch));
    }

    [Fact]
    public void Loslassen_von_Strg_und_Alt_mit_Maske_gegen_das_Menue()
    {
        var release = KeyboardTyper.BuildModifierRelease([KeyboardTyper.VK_CONTROL, KeyboardTyper.VK_LCONTROL, KeyboardTyper.VK_MENU, KeyboardTyper.VK_LMENU]);

        Assert.Equal(["↓VKE8", "↑VKE8", "↑VKA2", "↑VKA4"], Keys.Describe(release));
        Assert.Equal(0x1D, release[2].Scan);
        Assert.Equal(0x38, release[3].Scan);
    }

    [Fact]
    public void Rechte_Tasten_und_Win_sind_erweiterte_Tasten()
    {
        var release = KeyboardTyper.BuildModifierRelease([KeyboardTyper.VK_RCONTROL, KeyboardTyper.VK_RMENU, KeyboardTyper.VK_LWIN, KeyboardTyper.VK_RSHIFT]);

        Assert.Equal(["↓VKE8", "↑VKE8", "↑VKA3e", "↑VKA5e", "↑VKA1", "↑VK5Be"], Keys.Describe(release));
    }

    [Fact]
    public void Nur_Umschalt_braucht_keine_Maske_und_die_allgemeine_Taste_gilt_als_linke()
        => Assert.Equal(["↑VKA0"], Keys.Describe(KeyboardTyper.BuildModifierRelease([KeyboardTyper.VK_SHIFT])));

    [Fact]
    public void Andere_gedrueckte_Tasten_werden_nicht_losgelassen()
        => Assert.Empty(KeyboardTyper.BuildModifierRelease([0x51, 0x20, 0x0D]));

    [Fact]
    public void Nach_teilweisem_Senden_werden_haengende_Tasten_losgelassen()
    {
        var inputs = KeyboardTyper.BuildInputs("a\n😀", "");
        // a↓ a↑ Enter↓ | Enter↑ … – abgeschnitten nach Enter↓
        Assert.Equal(["↑VK0D"], Keys.Describe(KeyboardTyper.DanglingKeyUps(inputs.AsSpan(0, 3))));
        // … mitten im Surrogatpaar: beide Hälften gedrückt
        Assert.Equal(["↑U+D83D", "↑U+DE00"], Keys.Describe(KeyboardTyper.DanglingKeyUps(inputs.AsSpan(0, 6))));
        Assert.Empty(KeyboardTyper.DanglingKeyUps(inputs));
    }

#if WINDOWS
    [Fact]
    public void INPUT_hat_die_Groesse_aus_dem_Windows_SDK()
        => Assert.Equal(IntPtr.Size == 8 ? 40 : 28, Unsafe.SizeOf<Lupe.Platform.Windows.Input.Native.INPUT>());

    [Fact]
    public void MSG_hat_die_Groesse_aus_dem_Windows_SDK()
        => Assert.Equal(IntPtr.Size == 8 ? 48 : 28, Unsafe.SizeOf<Lupe.Platform.Windows.Input.Native.MSG>());

    [Fact]
    public void Flags_haben_die_Werte_von_KEYEVENTF()
    {
        Assert.Equal(Lupe.Platform.Windows.Input.Native.KEYEVENTF_EXTENDEDKEY, (uint)KeyFlags.Extended);
        Assert.Equal(Lupe.Platform.Windows.Input.Native.KEYEVENTF_KEYUP, (uint)KeyFlags.Up);
        Assert.Equal(Lupe.Platform.Windows.Input.Native.KEYEVENTF_UNICODE, (uint)KeyFlags.Unicode);
    }
#endif
}

/// <summary>Ablauf von KeyboardTyper mit einer Tastatur-Attrappe: Warten, Loslassen, Pakete, Fehler, Abbruch.</summary>
public sealed class KeyboardTyperFlowTests
{
#if WINDOWS
    private static readonly string BlockedText = Lupe.Platform.Windows.Input.Elevation.BlockedText;
#else
    private const string BlockedText = "Eintippen nicht möglich – das Fenster im Vordergrund läuft mit höheren Rechten als Lupe.";
#endif

    private static readonly int PollsUntilTimeout = KeyboardTyper.KeyReleaseTimeoutMs / KeyboardTyper.KeyPollMs;

    [Fact]
    public void Ohne_gedrueckte_Tasten_wird_sofort_getippt()
    {
        var keyboard = new FakeKeyboard();

        Assert.True(KeyboardTyper.TypeCore("Hallo", "Enter", keyboard, out string? error, default));

        Assert.Null(error);
        Assert.Empty(keyboard.Pauses);
        Assert.Single(keyboard.Sent);
        Assert.Equal(Keys.Describe(KeyboardTyper.BuildInputs("Hallo", "Enter")), Keys.Describe(keyboard.Sent[0]));
    }

    [Fact]
    public void Wartet_bis_Strg_und_Alt_aus_dem_Tastenkuerzel_losgelassen_sind()
    {
        var keyboard = new FakeKeyboard
        {
            // Strg+Alt noch 5 Abfragen lang gedrückt
            IsDownAfterPauses = (vk, pauses) => pauses < 5 && vk is KeyboardTyper.VK_CONTROL or KeyboardTyper.VK_LCONTROL or KeyboardTyper.VK_MENU or KeyboardTyper.VK_LMENU,
        };

        Assert.True(KeyboardTyper.TypeCore("a", "", keyboard, out _, default));

        Assert.Equal(Enumerable.Repeat(KeyboardTyper.KeyPollMs, 5), keyboard.Pauses);
        Assert.Single(keyboard.Sent); // kein künstliches Loslassen nötig
        Assert.Equal(Keys.Chars("a"), Keys.Describe(keyboard.Sent[0]));
    }

    [Fact]
    public void Wartet_auch_auf_die_Haupttaste_des_Kuerzels()
    {
        var keyboard = new FakeKeyboard { IsDownAfterPauses = (vk, pauses) => pauses < 3 && vk == 0x51 }; // Q

        Assert.True(KeyboardTyper.TypeCore("a", "", keyboard, out _, default));

        Assert.Equal(3, keyboard.Pauses.Count);
    }

    [Fact]
    public void Dauerhaft_gehaltene_Umschalttasten_werden_nach_der_Frist_kuenstlich_losgelassen()
    {
        var keyboard = new FakeKeyboard
        {
            IsDownAfterPauses = (vk, _) => vk is KeyboardTyper.VK_CONTROL or KeyboardTyper.VK_LCONTROL or KeyboardTyper.VK_MENU or KeyboardTyper.VK_RMENU,
        };

        Assert.True(KeyboardTyper.TypeCore("a", "", keyboard, out _, default));

        Assert.Equal(PollsUntilTimeout, keyboard.Pauses.Count);
        Assert.Equal(2, keyboard.Sent.Count);
        Assert.Equal(["↓VKE8", "↑VKE8", "↑VKA2", "↑VKA5e"], Keys.Describe(keyboard.Sent[0]));
        Assert.Equal(Keys.Chars("a"), Keys.Describe(keyboard.Sent[1]));
    }

    [Fact]
    public void Haengende_normale_Taste_haelt_das_Tippen_nur_bis_zur_Frist_auf()
    {
        var keyboard = new FakeKeyboard { IsDownAfterPauses = (vk, _) => vk == 0x41 };

        Assert.True(KeyboardTyper.TypeCore("b", "", keyboard, out _, default));

        Assert.Equal(PollsUntilTimeout, keyboard.Pauses.Count);
        Assert.Single(keyboard.Sent);
    }

    [Fact]
    public void Mausknoepfe_und_VK_PACKET_zaehlen_nicht_als_gedrueckte_Tasten()
    {
        var keyboard = new FakeKeyboard { IsDownAfterPauses = (vk, _) => vk is 0x01 or 0x02 or 0x04 or KeyboardTyper.VK_PACKET };

        Assert.True(KeyboardTyper.TypeCore("b", "", keyboard, out _, default));

        Assert.Empty(keyboard.Pauses);
    }

    [Fact]
    public void Lange_Texte_gehen_in_Paketen_mit_Pausen_dazwischen()
    {
        string text = string.Concat(Enumerable.Repeat("Lupe 😀 ", 300));
        var keyboard = new FakeKeyboard();

        Assert.True(KeyboardTyper.TypeCore(text, "", keyboard, out _, default));

        Assert.True(keyboard.Sent.Count > 3);
        Assert.All(keyboard.Sent, batch => Assert.InRange(batch.Length, 1, KeyboardTyper.MaxInputsPerBatch));
        Assert.Equal(Enumerable.Repeat(KeyboardTyper.BatchPauseMs, keyboard.Sent.Count - 1), keyboard.Pauses);
        Assert.Equal(Keys.Describe(KeyboardTyper.BuildInputs(text, "")), Keys.Describe(keyboard.Sent.SelectMany(b => b)));
    }

    [Fact]
    public void Leerer_Text_ohne_Suffix_tut_nichts()
    {
        var keyboard = new FakeKeyboard();

        Assert.True(KeyboardTyper.TypeCore("", "", keyboard, out string? error, default));

        Assert.Null(error);
        Assert.Empty(keyboard.Sent);
        Assert.Equal(0, keyboard.KeyQueries); // nicht einmal auf Tasten gewartet
    }

#if WINDOWS // Windows-Texte für SendInput-Fehler
    [Fact]
    public void Gesperrter_Desktop_meldet_den_Grund_auf_Deutsch()
    {
        var keyboard = new FakeKeyboard { Accept = (_, _) => (0, 5) };

        Assert.False(KeyboardTyper.TypeCore(string.Concat(Enumerable.Repeat("x", 500)), "", keyboard, out string? error, default));

        Assert.Single(keyboard.Sent); // nach dem ersten Fehlschlag kein weiteres Paket
        Assert.Contains("nimmt gerade keine Tastatureingaben an", error);
    }
#endif

    [Fact]
    public void Zielfenster_mit_hoeheren_Rechten_wird_gemeldet_statt_ins_Leere_zu_tippen()
    {
        // UIPI: SendInput würde hier „Erfolg“ melden, obwohl Windows alles verwirft.
        var keyboard = new FakeKeyboard
        {
            IsDownAfterPauses = (vk, pauses) => pauses < 4 && vk == KeyboardTyper.VK_LCONTROL,
            Foreground = () => BlockedText,
        };

        Assert.False(KeyboardTyper.TypeCore("geheim", "Enter", keyboard, out string? error, default));

        Assert.Equal(BlockedText, error);
        Assert.Contains("höheren Rechten", error);
        Assert.Empty(keyboard.Sent);
        Assert.Equal([4], keyboard.ForegroundChecks); // einmal, und erst nach dem Warten auf losgelassene Tasten
    }

    [Fact]
    public void Ohne_Hindernis_wird_das_Vordergrundfenster_genau_einmal_geprueft()
    {
        var keyboard = new FakeKeyboard();

        Assert.True(KeyboardTyper.TypeCore(string.Concat(Enumerable.Repeat("x", 900)), "", keyboard, out _, default));

        Assert.Single(keyboard.ForegroundChecks);
        Assert.True(keyboard.Sent.Count > 1);
    }

#if WINDOWS // Windows-Texte für SendInput-Fehler
    [Fact]
    public void SendInput_ohne_Fehlercode_bekommt_eine_verstaendliche_Meldung()
    {
        var keyboard = new FakeKeyboard { Accept = (_, _) => (0, 0) };

        Assert.False(KeyboardTyper.TypeCore("x", "", keyboard, out string? error, default));

        Assert.StartsWith("Eintippen nicht möglich – Windows hat die Tastatureingabe nicht angenommen", error);
        Assert.DoesNotContain("Fehler 0", error);
    }
#endif

#if WINDOWS // Windows-Texte für SendInput-Fehler
    [Fact]
    public void Anderer_Fehler_nennt_die_Windows_Nummer()
    {
        var keyboard = new FakeKeyboard { Accept = (_, _) => (0, 87) };

        Assert.False(KeyboardTyper.TypeCore("x", "", keyboard, out string? error, default));

        Assert.Equal("Eintippen nicht möglich (Windows-Fehler 87).", error);
    }
#endif

    [Fact]
    public void Teilweise_angenommener_Text_wird_gemeldet_und_haengende_Tasten_losgelassen()
    {
        string text = string.Concat(Enumerable.Repeat("ab\n", 200));
        var keyboard = new FakeKeyboard
        {
            // zweites Paket: Windows nimmt nur 5 Ereignisse an (endet mitten in einem Tastendruck)
            Accept = (index, length) => index == 1 ? (5, 5) : (length, 0),
        };

        Assert.False(KeyboardTyper.TypeCore(text, "", keyboard, out string? error, default));

        Assert.Equal("Eintippen wurde unterbrochen – der Text kam nur teilweise an.", error);
        Assert.Equal(3, keyboard.Sent.Count); // Paket 1, Paket 2 (teilweise), Loslassen
        var accepted = keyboard.Sent[1].Take(5).ToArray();
        Assert.Equal(Keys.Describe(KeyboardTyper.DanglingKeyUps(accepted)), Keys.Describe(keyboard.Sent[2]));
        Assert.NotEmpty(keyboard.Sent[2]);
    }

    [Fact]
    public void Fehler_nach_bereits_getippten_Paketen_heisst_teilweise()
    {
        string text = string.Concat(Enumerable.Repeat("x", 600));
        var keyboard = new FakeKeyboard { Accept = (index, length) => index == 0 ? (length, 0) : (0, 5) };

        Assert.False(KeyboardTyper.TypeCore(text, "", keyboard, out string? error, default));

        Assert.Contains("nur teilweise", error);
    }

    [Fact]
    public void Abbruch_zwischen_zwei_Paketen()
    {
        using var cts = new CancellationTokenSource();
        string text = string.Concat(Enumerable.Repeat("x", 1000));
        var keyboard = new FakeKeyboard { AfterSend = _ => cts.Cancel() };

        Assert.Throws<OperationCanceledException>(() => KeyboardTyper.TypeCore(text, "", keyboard, out _, cts.Token));

        Assert.Single(keyboard.Sent);
    }

    [Fact]
    public void Abbruch_waehrend_des_Wartens_auf_losgelassene_Tasten()
    {
        using var cts = new CancellationTokenSource();
        var keyboard = new FakeKeyboard
        {
            IsDownAfterPauses = (vk, pauses) =>
            {
                if (pauses == 3) cts.Cancel();
                return vk == KeyboardTyper.VK_LCONTROL;
            },
        };

        Assert.Throws<OperationCanceledException>(() => KeyboardTyper.TypeCore("x", "", keyboard, out _, cts.Token));

        Assert.Empty(keyboard.Sent);
    }

    [Fact]
    public void Schon_abgebrochenes_Token_tippt_nichts()
    {
        var keyboard = new FakeKeyboard();
        Assert.Throws<OperationCanceledException>(() => KeyboardTyper.TypeCore("x", "", keyboard, out _, new CancellationToken(true)));
        Assert.Empty(keyboard.Sent);
        Assert.Equal(0, keyboard.KeyQueries);
    }

    [Fact]
    public void Null_als_Text_ist_ein_Programmierfehler()
    {
        Assert.Throws<ArgumentNullException>(() => KeyboardTyper.Type(null!));
        Assert.Throws<ArgumentNullException>(() => KeyboardTyper.TryType(null!, "", out _));
    }

#if WINDOWS
    [NonInteractiveFact]
    public void Ohne_sichtbaren_Desktop_meldet_TryType_den_Grund_und_Type_wirft_nicht()
    {
        Assert.False(KeyboardTyper.TryType("Lupe", "Enter", out string? error));
        Assert.Contains("keine Tastatureingaben", error);

        KeyboardTyper.Type("Lupe", "Enter"); // keine Ausnahme
    }
#endif

    [Fact]
    public void Mit_Zeichenpause_geht_jedes_Zeichen_einzeln_hinaus()
    {
        // Notepad unter Windows 11 verlor Zeichen, wenn ein ganzer Code in einem SendInput kam
        var keyboard = new FakeKeyboard();

        Assert.True(KeyboardTyper.TypeCore("Ab\uD83D\uDE00c", "Enter", keyboard, out _, default, delayMs: 20));

        // A, b, Emoji (Surrogatpaar in einem Paket), c, Enter – je Paket genau eine Taste gedrückt und losgelassen
        Assert.Equal([2, 2, 4, 2, 2], keyboard.Sent.Select(p => p.Length));
        Assert.Equal([20, 20, 20, 20], keyboard.Pauses);
    }

    [Fact]
    public void Ohne_Zeichenpause_bleibt_es_bei_grossen_Paketen()
    {
        var keyboard = new FakeKeyboard();

        Assert.True(KeyboardTyper.TypeCore("Hallo Welt", "", keyboard, out _, default, delayMs: 0));

        Assert.Single(keyboard.Sent);
        Assert.Empty(keyboard.Pauses);
    }
}
