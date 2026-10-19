// SPDX-License-Identifier: MIT
using Lupe.Core.Input;
using Lupe.Core.Model;
using Lupe.Core.Screen;
using Lupe.Core.Settings;
using Lupe.Core.History;
using Lupe.Platform.MacOS;
using Lupe.Platform.MacOS.Imaging;

namespace Lupe.Tests.Platform;

/// <summary>Nur unter macOS (in CI: macos-latest).</summary>
public sealed class MacOSFactAttribute : FactAttribute
{
    public MacOSFactAttribute()
    {
        if (!OperatingSystem.IsMacOS()) Skip = "Nur unter macOS.";
    }
}

/// <summary>
/// Die macOS-Anbindung gegen das echte System. Tippen, Bildschirmfotos und die Kamera werden nur so weit geprüft, wie es
/// ohne Freigabe geht; die Systemdialoge (Bedienungshilfen, Bildschirmaufnahme) blockieren dabei nicht.
/// </summary>
public sealed class MacOSSystemTests
{
    private const string RedAndTransparentPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAAD0lEQVR4nGP4z8AARAwMAAz8Af9c/RSVAAAAAElFTkSuQmCC";

    [MacOSFact]
    public async Task ImageIO_liest_PNG_und_rechnet_Transparenz_auf_Weiss()
    {
        var decoder = new MacOSImageDecoder();
        var image = await decoder.DecodeAsync(new MemoryStream(Convert.FromBase64String(RedAndTransparentPng)), "test.png", 1000, default);

        Assert.Equal((2, 1), (image.Width, image.Height));
        var p = image.Bgra;
        Assert.True(p[2] > 200 && p[1] < 60 && p[0] < 60, $"links rot erwartet: {p[0]},{p[1]},{p[2]}");
        Assert.True(p[4] > 245 && p[5] > 245 && p[6] > 245, $"rechts weiß erwartet: {p[4]},{p[5]},{p[6]}");
        Assert.Equal(255, p[3]);
        Assert.Equal(255, p[7]);
    }

    [MacOSFact]
    public async Task ImageIO_meldet_Unsinn_als_InvalidData()
    {
        var decoder = new MacOSImageDecoder();
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => decoder.DecodeAsync(new MemoryStream([1, 2, 3, 4]), "kaputt.png", 1000, default));
        Assert.Contains("kaputt.png", ex.Message);
    }

    [MacOSFact]
    public void Einstellungen_und_Verlauf_liegen_unter_Application_Support()
    {
        Assert.StartsWith(MacOSPaths.ApplicationSupport, SettingsStore.DefaultPath);
        Assert.StartsWith(MacOSPaths.ApplicationSupport, HistoryStore.DefaultPath);
    }

    [MacOSFact]
    public void Bildschirme_werden_gemeldet()
    {
        var screen = new MacOSPlatform().Screen;
        var monitors = screen.Monitors();
        // CI-Mac hat einen Bildschirm (virtuell); ohne Anmeldung an der Konsole wäre die Liste leer
        if (monitors.Count == 0) return;
        Assert.Single(monitors, m => m.IsPrimary);
        var primary = monitors.First(m => m.IsPrimary);
        Assert.Equal(0, primary.Bounds.X);
        Assert.Equal(0, primary.Bounds.Y);
        Assert.True(primary.Scale >= 1);
        Assert.NotEqual(default, screen.VirtualScreen);
    }

    [MacOSFact]
    public void Berechtigungen_lassen_sich_ohne_Rueckfrage_pruefen()
    {
        var problems = new MacOSPlatform().Problems.Check();
        Assert.All(problems, p => Assert.False(string.IsNullOrWhiteSpace(p.Message)));
    }

    [MacOSFact]
    public async Task Kameras_auflisten_wirft_nicht()
    {
        var cameras = await new MacOSPlatform().Cameras.ListAsync();
        Assert.All(cameras, c => Assert.False(string.IsNullOrWhiteSpace(c.Name)));
    }

    [MacOSFact]
    public void Tastaturlayout_und_Kuerzeltext_ohne_Absturz()
    {
        var platform = new MacOSPlatform();
        Assert.False(platform.KeyboardLayout.AltGrIsCtrlAlt);
        _ = platform.KeyboardLayout.KeyChar(0xBA);
        _ = platform.KeyboardLayout.KeyForChar('a');
        Assert.False(string.IsNullOrEmpty(HotkeyText.Format(Hotkey.Default)));
    }

    [MacOSFact]
    public void Tippen_ohne_Bedienungshilfen_nennt_die_Einstellung()
    {
        var keyboard = new MacOSPlatform().Keyboard;
        // In CI ist Lupe nicht freigegeben: dann muss der Grund genannt werden (der Systemdialog erscheint dort nicht)
        if (keyboard.ForegroundProblem() is { } problem) Assert.Contains("Bedienungshilfen", problem);
        Assert.False(keyboard.IsDown(0x10)); // niemand hält die Umschalttaste
    }

    [MacOSFact]
    public void Vordergrundprogramm_ist_bekannt()
    {
        var active = new MacOSPlatform().ActiveWindow;
        Assert.True(active.IsSupported);
        Assert.True(active.IsOwnWindow(Environment.ProcessId));
        _ = active.Foreground(); // ohne Fensterserver 0, sonst eine Prozess-ID
    }

    [MacOSFact]
    public void Tastenkuerzel_anmelden_und_abmelden()
    {
        using var hotkey = new MacOSPlatform().Hotkeys.Register(new Hotkey(Ctrl: true, Alt: true, Shift: true, Win: true, Key: 0x7B)); // F12
        Assert.True(hotkey.IsRegistered || hotkey.Error is not null);
    }

    [MacOSFact]
    public void Bildschirmfoto_ohne_Berechtigung_nennt_die_Einstellung()
    {
        var screen = new MacOSPlatform().Screen;
        if (MacOSPermissionsProbe.ScreenAllowed) return; // mit Freigabe würde ein echtes Foto entstehen – hier nicht nötig
        Assert.False(screen.TryCapture(new PixelRect(0, 0, 10, 10), out _, out string? error));
        Assert.Contains("Bildschirm", error);
    }

    private static class MacOSPermissionsProbe
    {
        public static bool ScreenAllowed => !new MacOSPlatform().Problems.Check().Any(p => p.Feature == Lupe.Core.Platform.PlatformFeature.Screen);
    }
}
