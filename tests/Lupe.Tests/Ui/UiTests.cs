using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Lupe.Core.Model;
using Lupe.Services;
using Lupe.UI;
using Lupe.UI.Pages;
using Lupe.UI.Theme;

namespace Lupe.Tests.Ui;

/// <summary>Oberfläche ohne Bildschirm (Avalonia.Headless): Navigation, Meldungen, Tastenkürzel-Aufnahme, Auswahlfenster, Verlauf.</summary>
public sealed class UiTests
{
    // Seitenwechsel: unter macOS mit ⌘, sonst mit Strg (wie MainWindow)
    private static RawInputModifiers Command => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static AppState Demo() => AppState.OpenDemo(strict: false, new DateTimeOffset(2026, 10, 14, 10, 42, 0, TimeSpan.Zero));

    [Fact]
    public void Strg_Ziffer_wechselt_die_Seite()
    {
        UiSession.Run(() =>
        {
            using var state = Demo();
            var window = new MainWindow(state);
            window.Show();
            try
            {
                Assert.Equal("Kamera", window.PageTitle.Text);
                window.KeyPressQwerty(PhysicalKey.Digit4, Command);
                Assert.Equal("Verlauf", window.PageTitle.Text);
                Assert.True(window.NavHistory.IsChecked);
                window.KeyPressQwerty(PhysicalKey.Digit6, Command);
                Assert.Equal("Info", window.PageTitle.Text);
                window.ShowPage("einstellungen");
                Assert.Equal("Einstellungen", window.PageTitle.Text);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Abzeichen_und_Tastenkuerzel_in_der_Demo()
    {
        UiSession.Run(() =>
        {
            using var state = Demo();
            var window = new MainWindow(state);
            window.Show();
            try
            {
                Assert.Equal("12", window.NavHistory.Badge);
                Assert.Equal("in der Demo aus", window.StatusDetail.Text);
                Assert.Equal("Lupe – Demo", window.Title);

                // Belegtes Kürzel: Warnung am Bildschirm-Eintrag (außerhalb der Demo)
                using var real = AppState.OpenDemo(strict: true, DateTimeOffset.Now);
                var other = new MainWindow(real);
                real.SetHotkeyStatus(false, "Belegt von einem anderen Programm.");
                Assert.Equal("!", other.NavScreen.Badge);
                Assert.True(other.NavScreen.BadgeWarning);
                real.SetHotkeyStatus(true, null);
                Assert.Null(other.NavScreen.Badge);
                other.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Meldung_erscheint_unten_und_laesst_sich_schliessen()
    {
        UiSession.Run(() =>
        {
            using var state = Demo();
            var window = new MainWindow(state);
            window.Show();
            try
            {
                Assert.False(window.InfoBar.IsVisible);
                state.Report(NoticeKind.Warning, "Achtung, Test.");
                Assert.True(window.InfoBar.IsVisible);
                Assert.Equal("Achtung, Test.", window.InfoText.Text);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Tastenkuerzel_wird_im_Feld_aufgenommen()
    {
        UiSession.Run(() =>
        {
            using var state = Demo();
            var page = new SettingsPage(state);
            var window = new Window { Content = page, Width = 1000, Height = 700 };
            window.Show();
            try
            {
                page.OnShown();
                page.HotkeyBox.Focus();
                Assert.True(state.HotkeySuspended, "Während der Aufnahme ist das globale Kürzel abgemeldet.");

                // Ohne Strg/Alt/Win: abgelehnt, Aufnahme läuft weiter
                window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.None);
                Assert.Equal(Hotkey.PlatformDefault, state.Settings.ScreenHotkey);

                window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control | RawInputModifiers.Alt);
                Assert.Equal(new Hotkey(true, true, false, false, 0x4B), state.Settings.ScreenHotkey);
                Assert.False(state.HotkeySuspended, "Nach der Aufnahme ist das Kürzel wieder angemeldet.");
                Assert.Contains("K", page.HotkeyBox.Text);

                // F-Tasten gehen auch allein
                page.LimitBox.Focus();
                page.HotkeyBox.Focus();
                window.KeyPressQwerty(PhysicalKey.F9, RawInputModifiers.None);
                Assert.Equal(new Hotkey(false, false, false, false, 0x78), state.Settings.ScreenHotkey);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Auswahlfenster_nimmt_den_Code_unter_der_Zifferntaste()
    {
        UiSession.Run(async () =>
        {
            var time = DateTimeOffset.Now;
            var codes = new[] { "eins", "zwei", "drei" }.Select(t => new DecodedCode { Text = t, Format = "QR_CODE" }).ToList();
            var items = codes.Select((c, i) => new CodeItem(c, ScanSource.Bildschirm, time, null, i + 1)).ToList();
            var window = new CodeChoiceWindow(items, "kopiert", anchor: null);
            var choice = window.ChooseAsync();
            window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.None);
            var (outcome, code) = await choice;
            Assert.Equal(CodeChoiceWindow.Outcome.Chosen, outcome);
            Assert.Same(codes[1], code);

            var cancelled = new CodeChoiceWindow(items, "", anchor: null);
            var second = cancelled.ChooseAsync();
            cancelled.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Equal(CodeChoiceWindow.Outcome.Cancelled, (await second).Outcome);
        });
    }

    [Fact]
    public void Verlauf_zeigt_und_sortiert_die_Eintraege()
    {
        UiSession.Run(() =>
        {
            using var state = Demo();
            var page = new HistoryPage(state);
            var window = new Window { Content = page, Width = 1000, Height = 700 };
            window.Show();
            try
            {
                page.OnShown();
                var rows = page.HistoryList.ItemsSource!.Cast<HistoryRow>().ToList();
                Assert.Equal(12, rows.Count);
                Assert.True(rows[0].Time > rows[^1].Time, "Neueste zuerst");
                Assert.Equal("12 Einträge", page.CountText.Text);

                // Klick auf „Zeit“: aufsteigend, zweiter Klick: absteigend
                page.SortTime.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                rows = page.HistoryList.ItemsSource!.Cast<HistoryRow>().ToList();
                Assert.True(rows[0].Time < rows[^1].Time);
                page.SortTime.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                rows = page.HistoryList.ItemsSource!.Cast<HistoryRow>().ToList();
                Assert.True(rows[0].Time > rows[^1].Time);

                // Auswahl füllt die Leiste unten
                page.HistoryList.SelectedIndex = 0;
                Assert.True(page.DetailBar.IsEnabled);
                Assert.Equal(rows[0].OneLine, page.DetailTitle.Text);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Dunkles_Theme_nimmt_den_hellen_Akzent()
    {
        UiSession.Run(() =>
        {
            var app = Application.Current!;
            try
            {
                ThemeManager.Force(dark: true);
                Assert.True(app.TryGetResource("AccentFillColorDefaultBrush", app.ActualThemeVariant, out var dark));
                Assert.Equal(Color.Parse("#60CDFF"), ((ISolidColorBrush)dark!).Color);
                ThemeManager.Force(dark: false);
                Assert.True(app.TryGetResource("AccentFillColorDefaultBrush", app.ActualThemeVariant, out var light));
                Assert.Equal(Color.Parse("#0067C0"), ((ISolidColorBrush)light!).Color);
            }
            finally
            {
                ThemeManager.Force(dark: false);
            }
        });
    }

    [Theory]
    [InlineData(Key.A, 0x41)]
    [InlineData(Key.Q, 0x51)]
    [InlineData(Key.D7, 0x37)]
    [InlineData(Key.F5, 0x74)]
    [InlineData(Key.NumPad3, 0x63)]
    [InlineData(Key.OemPlus, 0xBB)]
    [InlineData(Key.PrintScreen, 0x2C)]
    public void Tasten_werden_zu_Windows_Tastencodes(Key key, int expected)
    {
        Assert.Equal(expected, VirtualKeys.From(key));
    }

    [Fact]
    public void Modifizierer_sind_keine_Kuerzeltasten()
    {
        Assert.True(VirtualKeys.IsModifier(Key.LeftCtrl));
        Assert.True(VirtualKeys.IsStandalone(Key.F12));
        Assert.False(VirtualKeys.IsStandalone(Key.Q));
        Assert.Null(VirtualKeys.From(Key.LeftShift));
    }
}
