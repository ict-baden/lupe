using Lupe.Core.Input;
using Lupe.Core.Model;
using Lupe.Core.Platform;
using Lupe.Core.Screen;
using Lupe.Core.Settings;
using Lupe.Platform.Linux;
using Lupe.Platform.Linux.Camera;
using Lupe.Platform.Linux.Imaging;
using Lupe.Platform.Linux.Input;
using Lupe.Platform.Linux.Screen;
using SkiaSharp;

namespace Lupe.Tests.Platform;

/// <summary>Die reine Logik der Linux-Anbindung (Bildumwandlung, Keysyms, Pfade, .desktop) – läuft auf jedem System.</summary>
public sealed class LinuxLogicTests
{
    // ---------- Kamerabilder ----------

    private static (byte B, byte G, byte R, byte A) Pixel(PixelImage image, int x, int y)
    {
        int o = y * image.Stride + x * 4;
        return (image.Bgra[o], image.Bgra[o + 1], image.Bgra[o + 2], image.Bgra[o + 3]);
    }

    private static void Near(int expected, int actual, int tolerance = 3) =>
        Assert.InRange(actual, expected - tolerance, expected + tolerance);

    [Fact]
    public void Yuyv_wird_nach_BT601_umgerechnet()
    {
        // 4×1: Weiß, Schwarz | Rot, Rot (Y, U, Y, V)
        byte[] yuyv = [235, 128, 16, 128, 81, 90, 81, 240];
        var image = FrameConversion.ToBgra(RawFormat.Yuyv, 4, 1, yuyv)!;

        Assert.Equal((4, 1), (image.Width, image.Height));
        Assert.Equal((255, 255, 255, 255), Pixel(image, 0, 0));
        Assert.Equal((0, 0, 0, 255), Pixel(image, 1, 0));
        var red = Pixel(image, 2, 0);
        Near(0, red.B);
        Near(0, red.G);
        Near(255, red.R);
        Assert.Equal(red, Pixel(image, 3, 0));
    }

    [Fact]
    public void Uyvy_hat_die_Bytes_in_anderer_Reihenfolge()
    {
        byte[] uyvy = [128, 235, 128, 16]; // U, Y0, V, Y1
        var image = FrameConversion.ToBgra(RawFormat.Uyvy, 2, 1, uyvy)!;
        Assert.Equal((255, 255, 255, 255), Pixel(image, 0, 0));
        Assert.Equal((0, 0, 0, 255), Pixel(image, 1, 0));
    }

    [Fact]
    public void Yuyv_mit_ungerader_Breite_und_BMP_Kopf_von_FlashCap()
    {
        // 3×2 Pixel → 2 Paare je Zeile; davor ein 54-Byte-BMP-Kopf wie ihn FlashCap ohne Umwandlung voranstellt
        var header = new byte[54];
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BitConverter.GetBytes(54).CopyTo(header, 10);
        BitConverter.GetBytes(-2).CopyTo(header, 22);
        byte[] pixels = [235, 128, 235, 128, 235, 128, 0, 128, 16, 128, 16, 128, 16, 128, 0, 128];
        var image = FrameConversion.ToBgra(RawFormat.Yuyv, 3, 2, [.. header, .. pixels])!;

        Assert.Equal((255, 255, 255, 255), Pixel(image, 2, 0));
        Assert.Equal((0, 0, 0, 255), Pixel(image, 0, 1));
    }

    [Fact]
    public void Nv12_Ebene_fuer_Helligkeit_und_Farbe()
    {
        // 2×2 Rot: Y-Ebene, dann UV verschachtelt (eine Zeile, ein Paar)
        byte[] nv12 = [81, 81, 81, 81, 90, 240];
        var image = FrameConversion.ToBgra(RawFormat.Nv12, 2, 2, nv12)!;
        var p = Pixel(image, 1, 1);
        Near(255, p.R);
        Near(0, p.G);
        Near(0, p.B);
    }

    [Fact]
    public void Rgb24_von_unten_nach_oben_laut_BMP_Kopf()
    {
        var header = new byte[54];
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BitConverter.GetBytes(54).CopyTo(header, 10);
        BitConverter.GetBytes(2).CopyTo(header, 22); // positive Höhe = unterste Zeile zuerst
        // 1×2, Zeilen auf 4 Byte aufgefüllt: erst Blau (unten), dann Rot (oben)
        byte[] pixels = [255, 0, 0, 0, 0, 0, 255, 0];
        var image = FrameConversion.ToBgra(RawFormat.Rgb24, 1, 2, [.. header, .. pixels])!;
        Assert.Equal((0, 0, 255, 255), Pixel(image, 0, 0));
        Assert.Equal((255, 0, 0, 255), Pixel(image, 0, 1));
    }

    [Fact]
    public void Zu_kurze_Daten_ergeben_kein_Bild()
    {
        Assert.Null(FrameConversion.ToBgra(RawFormat.Yuyv, 640, 480, new byte[100]));
        Assert.Null(FrameConversion.ToBgra(RawFormat.Nv12, 4, 4, new byte[10]));
        Assert.Null(FrameConversion.ToBgra(RawFormat.Mjpeg, 0, 0, [1, 2, 3, 4]));
        Assert.Null(FrameConversion.ToBgra(RawFormat.Mjpeg, 0, 0, [0xFF, 0xD8, 0xFF, 0x00, 1, 2]));
    }

    [Fact]
    public void Mjpeg_wird_ueber_SkiaSharp_dekodiert()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(32, 16, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bitmap.Erase(new SKColor(0, 0, 255));
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 95);
        var image = FrameConversion.ToBgra(RawFormat.Mjpeg, 0, 0, data.ToArray())!;

        Assert.Equal((32, 16), (image.Width, image.Height));
        var p = Pixel(image, 10, 8);
        Near(255, p.B, 6);
        Near(0, p.R, 6);
        Assert.Equal(255, p.A);
    }

    [Theory]
    [InlineData(FlashCap.PixelFormats.YUYV, "YUY2", "Yuyv")]
    [InlineData(FlashCap.PixelFormats.JPEG, "MJPG", "Mjpeg")]
    [InlineData(FlashCap.PixelFormats.NV12, "NV12", "Nv12")]
    [InlineData(FlashCap.PixelFormats.PNG, "PNG", "Unknown")]
    public void Kameraformate_werden_zugeordnet(FlashCap.PixelFormats format, string subtype, string raw)
    {
        Assert.Equal(subtype, LinuxCameraProvider.SubtypeOf(format));
        Assert.Equal(raw, LinuxCameraProvider.RawFormatOf(format).ToString());
    }

    [Theory]
    [InlineData("FlashCap: Couldn't open video device: DevicePath=/dev/video0, Code=13", "Gruppe „video“")]
    [InlineData("FlashCap: Couldn't start: Code=16", "anderen Programm")]
    [InlineData("FlashCap: Couldn't open video device: DevicePath=/dev/video3, Code=2", "nicht angeschlossen")]
    [InlineData("irgendwas", "ließ sich nicht starten")]
    public void Kamerafehler_werden_deutsch_beschrieben(string message, string expected)
    {
        Assert.Contains(expected, FlashCapFrameSource.Describe(new ArgumentException(message), "USB-Kamera"));
    }

    // ---------- Keysyms ----------

    [Theory]
    [InlineData('a', 0x61u)]
    [InlineData('Z', 0x5Au)]
    [InlineData('ä', 0xE4u)]
    [InlineData('ß', 0xDFu)]
    [InlineData('€', 0x20ACu)]
    [InlineData('Ł', 0x1000141u)]
    [InlineData((char)0x2028, 0x1002028u)]
    public void Zeichen_zu_Keysym(char c, uint keysym) => Assert.Equal(keysym, Keysyms.FromCodePoint(c));

    [Fact]
    public void Emoji_ausserhalb_der_BMP()
    {
        int cp = Keysyms.JoinSurrogates('\uD83D', '\uDE00');
        Assert.Equal(0x1F600, cp);
        Assert.Equal(0x0101F600u, Keysyms.FromCodePoint(cp));
        Assert.Equal(Keysyms.NoSymbol, Keysyms.FromCodePoint(0xD800)); // halbes Surrogat
    }

    [Theory]
    [InlineData(0x61u, 'a')]
    [InlineData(0xFCu, 'ü')]
    [InlineData(0xFE52u, '^')]   // dead_circumflex (deutsches Layout, Taste links der 1)
    [InlineData(0xFE51u, '´')]   // dead_acute
    [InlineData(0x20ACu, '€')]
    [InlineData(0x1B3u, 'ł')]
    [InlineData(0x1000141u, 'Ł')]
    public void Keysym_zu_Zeichen(uint keysym, char c) => Assert.Equal(c, Keysyms.ToChar(keysym));

    [Fact]
    public void Steuertasten_haben_kein_Zeichen()
    {
        Assert.Null(Keysyms.ToChar(Keysyms.ShiftL));
        Assert.Null(Keysyms.ToChar(0xFFBE)); // F1
    }

    [Theory]
    [InlineData(0x51, 0x71u)]                // Q → q
    [InlineData(0x31, 0x31u)]                // 1
    [InlineData(0x70, 0xFFBEu)]              // F1
    [InlineData(0x87, 0xFFD5u)]              // F24
    [InlineData(0x0D, Keysyms.Return)]
    [InlineData(0x09, Keysyms.Tab)]
    [InlineData(0xA0, Keysyms.ShiftL)]
    [InlineData(0xA5, Keysyms.AltR)]
    [InlineData(0x5B, Keysyms.SuperL)]
    [InlineData(0x2C, 0xFF61u)]              // Druck
    [InlineData(0xBA, Keysyms.NoSymbol)]     // OEM: hängt vom Layout ab
    public void Virtual_Key_zu_Keysym(int vk, uint keysym) => Assert.Equal(keysym, Keysyms.FromVirtualKey(vk));

    [Fact]
    public void Keysym_zurueck_zur_Virtual_Key()
    {
        Assert.Equal(0x51, Keysyms.ToVirtualKey('q'));
        Assert.Equal(0x51, Keysyms.ToVirtualKey('Q'));
        Assert.Equal(0x78, Keysyms.ToVirtualKey(0xFFC6)); // F9
        Assert.Equal(0xA0, Keysyms.ToVirtualKey(Keysyms.ShiftL));
        Assert.Null(Keysyms.ToVirtualKey(0xE4));
    }

    [Fact]
    public void OEM_Tasten_liegen_auf_eigenen_Tastencodes()
    {
        Assert.Equal(12, Keysyms.OemKeycodes.Count);
        Assert.Equal(Keysyms.OemKeycodes.Count, Keysyms.OemKeycodes.Values.Distinct().Count());
        Assert.Equal(34, Keysyms.OemKeycodes[0xDB]); // Ü im deutschen Layout (AD11)
    }

    // ---------- Tastenkürzel ----------

    [Fact]
    public void Vorschlag_fuer_das_GlobalShortcuts_Portal()
    {
        Assert.Equal("SHIFT+LOGO+q", PortalHotkey.PreferredTrigger(Hotkey.Default));
        Assert.Equal("CTRL+ALT+F9", PortalHotkey.PreferredTrigger(new Hotkey(true, true, false, false, 0x78)));
        Assert.Equal("CTRL+Print", PortalHotkey.PreferredTrigger(new Hotkey(true, false, false, false, 0x2C)));
        Assert.Null(PortalHotkey.PreferredTrigger(new Hotkey(true, false, false, false, 0xBA)));
    }

    [Fact]
    public void X11_Modifikatoren_und_Varianten()
    {
        Assert.Equal(1u | 64u, LinuxHotkeys.Modifiers(Hotkey.Default)); // Shift + Mod4 (Super)
        Assert.Equal(4u | 8u, LinuxHotkeys.Modifiers(new Hotkey(true, true, false, false, 0x51)));
        Assert.Equal(4, LinuxHotkeys.IgnoredVariants.Length);
    }

    // ---------- Pfade, Sitzung, Autostart ----------

    [Fact]
    public void XDG_Pfade_unter_Linux()
    {
        string? Env(string name) => name switch
        {
            "HOME" => "/home/anna",
            "XDG_CONFIG_HOME" => "/tmp/konfig",
            "XDG_DATA_HOME" => "relativ/zaehlt/nicht",
            _ => null,
        };
        Assert.Equal(Path.Combine("/tmp/konfig", "lupe"), AppPaths.Resolve(config: true, windows: false, macOS: false, Env));
        Assert.Equal(Path.Combine("/home/anna", ".local", "share", "lupe"), AppPaths.Resolve(config: false, windows: false, macOS: false, Env));
        Assert.Equal(Path.Combine("/home/anna", ".config", "lupe"),
            AppPaths.Resolve(config: true, windows: false, macOS: false, n => n == "HOME" ? "/home/anna" : null));
        Assert.Equal(Path.Combine("/home/anna", "Library", "Application Support", "Lupe"),
            AppPaths.Resolve(config: true, windows: false, macOS: true, Env));
    }

    [Fact]
    public void Standardpfade_passen_zum_System()
    {
        string settings = SettingsStore.DefaultPath, history = Lupe.Core.History.HistoryStore.DefaultPath;
        if (OperatingSystem.IsLinux())
        {
            Assert.EndsWith(Path.Combine("lupe", "einstellungen.json"), settings);
            Assert.EndsWith(Path.Combine("lupe", "verlauf.json"), history);
        }
        else if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Assert.Equal(Path.Combine(local, "Lupe", "einstellungen.json"), settings, ignoreCase: true);
            Assert.Equal(Path.Combine(local, "Lupe", "verlauf.json"), history, ignoreCase: true);
        }
    }

    [Theory]
    [InlineData(null, ":0", null, "X11")]
    [InlineData("wayland-0", ":0", null, "Wayland")]
    [InlineData(null, null, "wayland", "Wayland")]
    [InlineData(null, null, null, "None")]
    [InlineData(null, null, "tty", "None")]
    public void Sitzung_wird_erkannt(string? wayland, string? display, string? type, string expected)
    {
        string? Env(string name) => name switch
        {
            "WAYLAND_DISPLAY" => wayland,
            "DISPLAY" => display,
            "XDG_SESSION_TYPE" => type,
            _ => null,
        };
        Assert.Equal(expected, LinuxSession.Detect(Env).ToString());
    }

    [Fact]
    public void LUPE_BACKEND_x11_erzwingt_XWayland()
    {
        string? Env(string name) => name switch { "WAYLAND_DISPLAY" => "wayland-0", "DISPLAY" => ":0", "LUPE_BACKEND" => "x11", _ => null };
        Assert.Equal(SessionKind.X11, LinuxSession.Detect(Env));
    }

    [Fact]
    public void Desktop_Datei_fuer_den_Autostart()
    {
        string entry = LinuxAutostart.DesktopEntry(LinuxAutostart.Quote("/opt/Lupe App/lupe"));
        Assert.StartsWith("[Desktop Entry]\n", entry);
        Assert.Contains("Type=Application\n", entry);
        Assert.Contains("Exec=\"/opt/Lupe App/lupe\" --hintergrund\n", entry);
        Assert.Equal("/usr/bin/lupe", LinuxAutostart.Quote("/usr/bin/lupe"));
        Assert.Equal("\"/a/\\$b\\\"c\"", LinuxAutostart.Quote("/a/$b\"c"));
        Assert.Equal("\"/a b/100%%\"", LinuxAutostart.Quote("/a b/100%"));
    }

    // ---------- Bildschirm ----------

    [Fact]
    public void Rechtecke_vereinigen_und_schneiden()
    {
        var monitors = new[]
        {
            new MonitorArea(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 1, true, "A"),
            new MonitorArea(new PixelRect(1920, -200, 1280, 1024), new PixelRect(1920, -200, 1280, 1024), 1, false, "B"),
        };
        Assert.Equal(new PixelRect(0, -200, 3200, 1280), LinuxScreenCapture.Union(monitors));
        Assert.Equal(new PixelRect(10, 10, 5, 5), LinuxScreenCapture.Intersect(new PixelRect(0, 0, 15, 15), new PixelRect(10, 10, 20, 20)));
        Assert.Null(LinuxScreenCapture.Intersect(new PixelRect(0, 0, 10, 10), new PixelRect(10, 0, 5, 5)));

        var scaled = LinuxScreenCapture.ScaleTo(monitors, new PixelRect(0, -200, 3200, 1280), new PixelRect(0, 0, 6400, 2560));
        Assert.Equal(new PixelRect(0, 400, 3840, 2160), scaled[0].Bounds);
        Assert.Equal(2.0, scaled[0].Scale);
    }

    [Fact]
    public void Portal_Foto_wird_auf_den_Ausschnitt_zugeschnitten()
    {
        // 200×100: links Rot, rechts Grün; in doppelter Auflösung gegenüber den Koordinaten (100×50)
        using var shot = new SKBitmap(new SKImageInfo(200, 100, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(shot))
        {
            canvas.Clear(SKColors.Red);
            using var green = new SKPaint { Color = SKColors.Lime };
            canvas.DrawRect(100, 0, 100, 100, green);
        }
        var image = LinuxScreenCapture.Crop(shot, new PixelRect(0, 0, 100, 50), new PixelRect(40, 10, 20, 10));

        Assert.Equal((20, 10), (image.Width, image.Height));
        Assert.Equal((0, 0, 255, 255), Pixel(image, 2, 5));
        Assert.Equal((0, 255, 0, 255), Pixel(image, 17, 5));
    }

    // ---------- Bilddateien ----------

    [Fact]
    public async Task Bilddatei_mit_Transparenz_wird_auf_Weiss_gelegt()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(4, 2, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        bitmap.Erase(SKColors.Transparent);
        bitmap.SetPixel(0, 0, SKColors.Black);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(png.ToArray());

        var image = await new LinuxImageDecoder().DecodeAsync(stream, "test.png", 1_000_000, default);

        Assert.Equal((4, 2), (image.Width, image.Height));
        Assert.Equal((0, 0, 0, 255), Pixel(image, 0, 0));
        Assert.Equal((255, 255, 255, 255), Pixel(image, 3, 1));
        Assert.True(stream.CanRead); // Strom bleibt offen
    }

    [Fact]
    public async Task Grosse_Bilddatei_wird_verkleinert_und_Kaputtes_deutsch_gemeldet()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(400, 300, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bitmap.Erase(SKColors.White);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var image = await new LinuxImageDecoder().DecodeAsync(new MemoryStream(png.ToArray()), "gross.png", 30_000, default);
        Assert.True(image.Width * image.Height <= 30_000);
        Assert.Equal(4.0 / 3, (double)image.Width / image.Height, 1);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new LinuxImageDecoder().DecodeAsync(new MemoryStream([1, 2, 3, 4, 5]), "kaputt.png", 1000, default));
        Assert.Contains("kaputt.png", ex.Message);
    }
}

/// <summary>Setzt <see cref="LinuxSession.Environment"/> um oder spricht mit dem X-Server – nie parallel.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LinuxSessionCollection
{
    public const string Name = "Linux-Sitzung";
}

[Collection(LinuxSessionCollection.Name)]
public sealed class LinuxNoSessionTests
{
    [Fact]
    public async Task Ohne_grafische_Sitzung_meldet_alles_einen_deutschen_Grund()
    {
        var before = LinuxSession.Environment;
        LinuxSession.Environment = name => name == "HOME" ? Path.GetTempPath() : null;
        try
        {
            var platform = new LinuxPlatform();
            Assert.False(platform.Screen.TryCapture(new PixelRect(0, 0, 10, 10), out var image, out string? screenError));
            Assert.Null(image);
            Assert.Contains("DISPLAY", screenError);

            Assert.False(KeyboardTyper.TypeCore("abc", "Enter", platform.Keyboard, out string? typeError, default));
            Assert.Contains("keine grafische Sitzung", typeError);

            Assert.NotNull(platform.Hotkeys.UnsupportedReason);
            using (var hotkey = platform.Hotkeys.Register(Hotkey.Default))
            {
                Assert.False(hotkey.IsRegistered);
                Assert.NotNull(hotkey.Error);
            }
            Assert.False(platform.ActiveWindow.IsSupported);
            Assert.Equal(0, platform.ActiveWindow.Foreground());
            Assert.Contains(platform.Problems.Check(), p => p.Feature == PlatformFeature.Screen);
            Assert.Null(platform.KeyboardLayout.KeyChar(0xBA));
            Assert.False(platform.KeyboardLayout.AltGrIsCtrlAlt);
            if (!OperatingSystem.IsLinux()) Assert.Empty(await platform.Cameras.ListAsync());
        }
        finally
        {
            LinuxSession.Environment = before;
        }
    }

    [Fact]
    public void Autostart_schreibt_und_entfernt_die_Desktop_Datei()
    {
        var before = LinuxSession.Environment;
        string home = Path.Combine(Path.GetTempPath(), "lupe-autostart-" + Guid.NewGuid().ToString("N"));
        LinuxSession.Environment = name => name == "XDG_CONFIG_HOME" ? home : null;
        try
        {
            var autostart = new LinuxAutostart { ExecutablePath = () => "/usr/bin/lupe" };
            Assert.False(autostart.IsEnabled);
            Assert.Null(autostart.Set(true));
            Assert.True(autostart.IsEnabled);
            string file = Path.Combine(home, "autostart", "lupe.desktop");
            Assert.Contains("Exec=/usr/bin/lupe --hintergrund", File.ReadAllText(file));
            Assert.Null(autostart.Set(false));
            Assert.False(File.Exists(file));
            Assert.Null(autostart.Set(false)); // schon aus: kein Fehler

            Assert.NotNull(new LinuxAutostart { ExecutablePath = () => null }.Set(true));
        }
        finally
        {
            LinuxSession.Environment = before;
            if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
        }
    }
}
