using Lupe.Core.Camera;
using Lupe.Core.Decoding;
using Lupe.Core.Input;
using Lupe.Core.Model;
using Lupe.Core.Platform;
using Lupe.Core.Screen;

namespace Lupe.Tests.Platform;

/// <summary>Plattform-Auswahl und die Gerüste ohne Systemfunktionen.</summary>
public sealed class LupePlatformTests
{
    [Fact]
    public void Ohne_Vorgabe_passt_die_Plattform_zum_laufenden_System()
    {
        var current = LupePlatform.Current;
#if WINDOWS
        Assert.IsType<Lupe.Platform.Windows.WindowsPlatform>(current);
#else
        if (OperatingSystem.IsLinux()) Assert.IsType<Lupe.Platform.Linux.LinuxPlatform>(current);
        else if (OperatingSystem.IsMacOS()) Assert.IsType<Lupe.Platform.MacOS.MacOSPlatform>(current);
        else Assert.IsType<UnsupportedPlatform>(current); // net10.0-Ziel unter Windows: keine Windows-Anbindung geladen
#endif
    }

    [Fact]
    public void Assemblyname_je_System()
    {
        string? expected = OperatingSystem.IsWindows() ? "Lupe.Platform.Windows"
            : OperatingSystem.IsLinux() ? "Lupe.Platform.Linux"
            : OperatingSystem.IsMacOS() ? "Lupe.Platform.MacOS" : null;
        Assert.Equal(expected, LupePlatform.AssemblyNameForThisOs());
    }

    // Linux ist kein Gerüst mehr – siehe LinuxPlatformTests
    public static TheoryData<string> Geruest_Plattformen => ["unsupported", "macos"];

    private static IPlatform Create(string kind) => kind switch
    {
        "macos" => new Lupe.Platform.MacOS.MacOSPlatform(),
        _ => new UnsupportedPlatform("Testsystem"),
    };

    [Theory]
    [MemberData(nameof(Geruest_Plattformen))]
    public async Task Geruest_meldet_nicht_verfuegbar_statt_abzustuerzen(string kind)
    {
        if (kind == "macos" && OperatingSystem.IsMacOS()) return; // dort echt – siehe MacOSSystemTests
        var platform = Create(kind);

        Assert.Empty(await platform.Cameras.ListAsync());
        Assert.Empty(platform.Screen.Monitors());
        Assert.False(platform.Screen.TryCapture(new PixelRect(0, 0, 10, 10), out var image, out string? screenError));
        Assert.Null(image);
        Assert.Contains("nicht verfügbar", screenError);

        // Tippen: der Core gibt den Grund weiter, statt ins Leere zu tippen
        Assert.False(KeyboardTyper.TypeCore("abc", "Enter", platform.Keyboard, out string? typeError, default));
        Assert.Contains("nicht verfügbar", typeError);

        Assert.NotNull(platform.Hotkeys.UnsupportedReason);
        using (var hotkey = platform.Hotkeys.Register(Hotkey.Default))
        {
            Assert.False(hotkey.IsRegistered);
            Assert.Contains("nicht verfügbar", hotkey.Error);
        }

        Assert.False(platform.ActiveWindow.IsSupported);
        Assert.Equal(0, platform.ActiveWindow.Foreground());
        Assert.NotNull(await platform.ActiveWindow.ActivateAsync(1));
        Assert.False(platform.Autostart.IsEnabled);
        Assert.NotNull(platform.Autostart.Set(true));
        Assert.Null(platform.Autostart.Set(false));
        Assert.NotEmpty(platform.Images.Extensions);
        Assert.Empty(platform.Problems.Check());
        Assert.False(platform.KeyboardLayout.AltGrIsCtrlAlt);
    }

}

/// <summary>Setzt die Plattform um – läuft deshalb allein, nicht parallel zu anderen Tests.</summary>
[CollectionDefinition(nameof(PlatformSwitchCollection), DisableParallelization = true)]
public sealed class PlatformSwitchCollection;

[Collection(nameof(PlatformSwitchCollection))]
public sealed class LupePlatformUseTests
{
    [Fact]
    public async Task Use_setzt_die_Plattform_fuer_die_Fassaden()
    {
        var before = LupePlatform.Current;
        try
        {
            LupePlatform.Use(new UnsupportedPlatform("Testsystem"));
            Assert.Equal("Testsystem", LupePlatform.Current.Name);
            Assert.Empty(await Cameras.ListAsync());
            Assert.False(ScreenCapture.TryCapture(new PixelRect(0, 0, 4, 4), out _, out string? error));
            Assert.Contains("Testsystem", error);
            Assert.Null(HotkeyText.AltGrWarning(new Hotkey(true, true, false, false, 0x51))); // kein Strg+Alt = AltGr
            var ex = await Assert.ThrowsAsync<InvalidDataException>(() => ImageFile.LoadAsync(new MemoryStream([1, 2, 3]), "x.png"));
            Assert.Contains("x.png", ex.Message);
        }
        finally
        {
            LupePlatform.Use(before);
        }
    }
}
