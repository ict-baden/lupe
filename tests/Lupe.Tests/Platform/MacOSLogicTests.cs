using Lupe.Core.Input;
using Lupe.Core.Model;
using Lupe.Core.Platform;
using Lupe.Core.Screen;
using Lupe.Platform.MacOS;
using Lupe.Platform.MacOS.Camera;
using Lupe.Platform.MacOS.Imaging;
using Lupe.Platform.MacOS.Input;
using Lupe.Platform.MacOS.Screen;

namespace Lupe.Tests.Platform;

/// <summary>Die reine Logik der macOS-Anbindung – läuft auf jedem System.</summary>
public sealed class MacOSLogicTests
{
    // --- Tastencodes ---

    [Fact]
    public void Tastencodes_sind_in_beide_Richtungen_eindeutig()
    {
        var all = MacKeyCodes.All;
        Assert.Equal(all.Count, all.Select(p => p.Vk).Distinct().Count());
        Assert.Equal(all.Count, all.Select(p => p.Mac).Distinct().Count());
        foreach (var (vk, mac) in all)
        {
            Assert.Equal(mac, MacKeyCodes.FromVirtualKey(vk));
            Assert.Equal(vk, MacKeyCodes.ToVirtualKey(mac));
        }
    }

    [Theory]
    [InlineData('Q', 0x0C)]
    [InlineData('A', 0x00)]
    [InlineData('Z', 0x06)]
    [InlineData('0', 0x1D)]
    [InlineData(0x0D, 0x24)] // Eingabe
    [InlineData(0x09, 0x30)] // Tab
    [InlineData(0xA0, 0x38)] // linke Umschalttaste
    [InlineData(0x5B, 0x37)] // Win → Befehlstaste
    [InlineData(0x70, 0x7A)] // F1
    [InlineData(0x7B, 0x6F)] // F12
    public void Bekannte_Tasten(int vk, int mac) => Assert.Equal((ushort)mac, MacKeyCodes.FromVirtualKey(vk));

    [Fact]
    public void Unbekannte_Taste_hat_keinen_Code()
    {
        Assert.Null(MacKeyCodes.FromVirtualKey(0xE8));
        Assert.Null(MacKeyCodes.FromVirtualKey(0x2C)); // Druck – gibt es auf dem Mac nicht
        Assert.Null(MacKeyCodes.ToVirtualKey(0x7F));
    }

    [Fact]
    public void Allgemeine_Umschalttasten_pruefen_links_und_rechts()
    {
        Assert.Equal([MacKeyCodes.Shift, MacKeyCodes.RightShift], MacKeyCodes.ForKeyState(0x10).ToArray());
        Assert.Equal([MacKeyCodes.Control, MacKeyCodes.RightControl], MacKeyCodes.ForKeyState(0x11).ToArray());
        Assert.Equal([MacKeyCodes.Option, MacKeyCodes.RightOption], MacKeyCodes.ForKeyState(0x12).ToArray());
        Assert.Equal([MacKeyCodes.Command], MacKeyCodes.ForKeyState(0x5B).ToArray());
        Assert.Equal([(ushort)0x0C], MacKeyCodes.ForKeyState('Q').ToArray());
        Assert.Empty(MacKeyCodes.ForKeyState(0xFF).ToArray());
    }

    [Fact]
    public void Kuerzel_Win_Umschalt_Q_wird_Befehl_Umschalt_Q()
    {
        Assert.Equal(0x0100u | 0x0200u, MacKeyCodes.CarbonModifiers(Hotkey.Default));
        Assert.Equal(0x0800u | 0x1000u, MacKeyCodes.CarbonModifiers(new Hotkey(Ctrl: true, Alt: true, Shift: false, Win: false, Key: 'Q')));
    }

    // --- Ereignisfolge → CGEvents ---

    [Fact]
    public void Text_wird_zu_Unicode_Ereignissen_mit_Eingabe_am_Ende()
    {
        var events = KeyboardTyper.BuildInputs("aé", "Enter");
        var strokes = MacKeyPlan.Build(events);

        Assert.Equal(
            [("a", true), ("a", false), ("é", true), ("é", false), (null, true), (null, false)],
            strokes.Select(s => (s.Text, s.Down)).ToArray());
        Assert.All(strokes.Where(s => s.Text is not null), s => Assert.Equal(0, s.KeyCode));
        Assert.Equal(MacKeyCodes.Return, strokes[^1].KeyCode);
        Assert.Equal(events.Length, strokes[^1].Consumed);
    }

    [Fact]
    public void Surrogatpaar_wird_ein_Zeichen()
    {
        var events = KeyboardTyper.BuildInputs("😀", null);
        var strokes = MacKeyPlan.Build(events);

        Assert.Equal([("😀", true), ("😀", false)], strokes.Select(s => (s.Text, s.Down)).ToArray());
        Assert.Equal(events.Length, strokes[^1].Consumed);
    }

    [Fact]
    public void Einzelnes_Surrogat_wird_Ersatzzeichen()
    {
        var events = new[] { KeyboardTyper.Unicode('\uD83D', up: false), KeyboardTyper.Unicode('x', up: false) };
        var strokes = MacKeyPlan.Build(events);
        Assert.Equal(new[] { ((char)0xFFFD).ToString(), "x" }, strokes.Select(s => s.Text!).ToArray());
    }

    [Fact]
    public void Loslassen_der_Umschalttasten_ohne_Maskentaste()
    {
        var release = KeyboardTyper.BuildModifierRelease([0x5B, 0xA0]); // Win und Umschalt gedrückt
        var strokes = MacKeyPlan.Build(release);

        Assert.All(strokes, s => Assert.False(s.Down));
        Assert.Equal(new ushort[] { MacKeyCodes.Shift, MacKeyCodes.Command }.Order(), strokes.Select(s => s.KeyCode).Order());
        Assert.Equal(release.Length, strokes[^1].Consumed);
    }

    [Fact]
    public void Tastatur_ausserhalb_von_macOS_meldet_nicht_verfuegbar()
    {
        if (OperatingSystem.IsMacOS()) return;
        var keyboard = new MacOSKeyboard();
        Assert.Equal(0, keyboard.Send(KeyboardTyper.BuildInputs("a", null), out int code));
        Assert.Contains("nicht verfügbar", keyboard.DescribeRejection(code));
        Assert.False(keyboard.IsDown(0x10));
    }

    // --- Kamera (libLupeCamera) ---

    [Fact]
    public void Kamerabild_mit_Zeilenabstand_wird_dicht_und_deckend()
    {
        // 2×2 BGRA, 12 Bytes je Zeile (4 Bytes Füllung), Alpha 0 in der Quelle
        byte[] source =
        [
            1, 2, 3, 0, 4, 5, 6, 0, 99, 99, 99, 99,
            7, 8, 9, 0, 10, 11, 12, 0, 99, 99, 99, 99,
        ];
        var image = AVFoundationFrameSource.Copy(source, 2, 2, 12);

        Assert.Equal((2, 2, 8), (image.Width, image.Height, image.Stride));
        Assert.Equal(new byte[] { 1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255, 10, 11, 12, 255 }, image.Bgra);
    }

    [Fact]
    public void Kameraliste_aus_Kennung_und_Name()
    {
        var list = NativeCamera.ParseList("0x1420000005ac8600\tFaceTime HD Camera\n\nkaputt\nABC\t  \n");
        Assert.Equal([("0x1420000005ac8600", "FaceTime HD Camera"), ("ABC", "ABC")], list);
        Assert.Empty(NativeCamera.ParseList(""));
    }

    [Fact]
    public void Kamerafehler_haben_deutsche_Texte()
    {
        Assert.Equal(MacOSPermissions.CameraDenied, NativeCamera.Describe(NativeCamera.Denied, "X"));
        Assert.Contains("nicht angeschlossen", NativeCamera.Describe(NativeCamera.Disconnected, "X"));
        Assert.Contains("„X“", NativeCamera.Describe(NativeCamera.OpenFailed, "X"));
    }

    // --- Bildschirme ---

    private static RawDisplay Display(int index, double x, double y, double w, double h, int pw, int ph, bool main = false) =>
        new((uint)(index + 1), index, x, y, w, h, pw, ph, main);

    [Fact]
    public void Retina_und_externer_Bildschirm_im_gemeinsamen_Pixelraum()
    {
        var monitors = DisplayLayout.Build(
        [
            Display(0, 0, 0, 1440, 900, 2880, 1800, main: true),
            Display(1, 1440, -180, 1920, 1080, 1920, 1080),
        ]);

        Assert.Equal(2, monitors.Count);
        var main = monitors[0].Area;
        Assert.True(main.IsPrimary);
        Assert.Equal(new PixelRect(0, 0, 2880, 1800), main.Bounds);
        Assert.Equal(2, main.Scale);
        Assert.Equal(new PixelRect(2880, -360, 3840, 2160), monitors[1].Area.Bounds);
        Assert.Equal(new PixelRect(0, -360, 6720, 2160), DisplayLayout.Union(monitors.Select(m => m.Area.Bounds)));
        Assert.Equal("Bildschirm 1 (Hauptbildschirm)", main.Name);
    }

    [Fact]
    public void Bildschirme_von_links_nach_rechts()
    {
        var monitors = DisplayLayout.Build(
        [
            Display(0, 0, 0, 1920, 1080, 1920, 1080, main: true),
            Display(1, -1280, 0, 1280, 1024, 1280, 1024),
        ]);
        Assert.Equal([1, 0], monitors.Select(m => m.Display.Index));
        Assert.Equal(new PixelRect(-1280, 0, 1280, 1024), monitors[0].Area.Bounds);
        Assert.Empty(DisplayLayout.Build([]));
        Assert.Equal(default, DisplayLayout.Union([]));
    }

    [Fact]
    public void Ausschnitt_wird_kopiert_und_bei_kleinerem_Foto_hochgerechnet()
    {
        // Foto 2×1 (rot, blau) eines Bildschirms, der im Pixelraum 4×2 groß ist (Faktor 2)
        var shot = new PixelImage(2, 1, [0, 0, 255, 255, 255, 0, 0, 255]);
        var area = new PixelRect(1, 0, 3, 2);
        var target = new byte[3 * 2 * 4];
        DisplayLayout.Blit(shot, new PixelRect(0, 0, 4, 2), area, target);

        // Spalten 1, 2, 3 → Foto-Spalten 0, 1, 1
        byte[] row = [0, 0, 255, 255, 255, 0, 0, 255, 255, 0, 0, 255];
        Assert.Equal(row.Concat(row).ToArray(), target);
    }

    [Fact]
    public void Ausschnitt_gleicher_Groesse_wird_zeilenweise_kopiert()
    {
        var bgra = Enumerable.Range(0, 4 * 2 * 4).Select(i => (byte)i).ToArray();
        var shot = new PixelImage(4, 2, bgra);
        var target = new byte[2 * 1 * 4];
        DisplayLayout.Blit(shot, new PixelRect(10, 10, 4, 2), new PixelRect(11, 11, 2, 1), target);
        Assert.Equal(bgra.AsSpan(4 * 4 + 4, 8).ToArray(), target);
    }

    [Fact]
    public void Screencapture_Argumente()
    {
        var args = MacOSScreenCapture.Arguments(Display(1, 0, 0, 1, 1, 1, 1), "/tmp/x.png");
        Assert.Equal(["-x", "-t", "png", "-D", "2", "/tmp/x.png"], args);
    }

    // --- Bilddateien ---

    [Theory]
    [InlineData(100, 50, 10_000, 100, 50)]
    [InlineData(4000, 3000, 1_200_000, 1264, 948)]
    [InlineData(1, 100_000, 10, 1, 10)]
    [InlineData(10, 10, 0, 10, 10)]
    public void Verkleinern_auf_hoechstens_maxPixels(int w, int h, int max, int ew, int eh)
    {
        var (rw, rh) = ImageIOReader.FitInto(w, h, max);
        Assert.Equal((ew, eh), (rw, rh));
        if (max > 0) Assert.True((long)rw * rh <= max);
    }

    [Fact]
    public async Task Bilddatei_ausserhalb_von_macOS_meldet_nicht_verfuegbar()
    {
        if (OperatingSystem.IsMacOS()) return;
        var decoder = new MacOSImageDecoder();
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => decoder.DecodeAsync(new MemoryStream([1]), "a.png", 100, default));
        Assert.Contains("a.png", ex.Message);
        Assert.Contains(".heic", decoder.Extensions);
    }

    // --- Berechtigungen ---

    [Fact]
    public void Fehlende_Berechtigungen_mit_Link_in_die_Systemeinstellungen()
    {
        var problems = MacOSProblems.Collect(CameraAuthorization.Denied, screen: false, accessibility: false);
        Assert.Equal([PlatformFeature.Camera, PlatformFeature.Screen, PlatformFeature.Typing], problems.Select(p => p.Feature));
        Assert.All(problems, p => Assert.StartsWith("x-apple.systempreferences:", p.SettingsLink));
        Assert.Contains("Kamera", problems[0].Message);
        Assert.Contains("Bildschirm", problems[1].Message);
        Assert.Contains("Bedienungshilfen", problems[2].Message);
    }

    [Fact]
    public void Erlaubt_oder_noch_nicht_gefragt_ist_kein_Problem()
    {
        Assert.Empty(MacOSProblems.Collect(CameraAuthorization.Authorized, true, true));
        Assert.Empty(MacOSProblems.Collect(CameraAuthorization.NotDetermined, true, true));
        Assert.Empty(MacOSProblems.Collect(CameraAuthorization.Unknown, true, true));
        Assert.Contains("gesperrt", Assert.Single(MacOSProblems.Collect(CameraAuthorization.Restricted, true, true)).Message);
    }

    // --- Tastenkürzel ---

    [Fact]
    public void Belegtes_Kuerzel_wird_verstaendlich_gemeldet()
    {
        Assert.Contains("schon von einem anderen Programm belegt", MacOSHotkeys.DescribeStatus(-9878, Hotkey.Default));
        Assert.Contains("-50", MacOSHotkeys.DescribeStatus(-50, Hotkey.Default));
    }

    // --- Autostart ---

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lupe-mac-" + Guid.NewGuid().ToString("N"));
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    [Fact]
    public void LaunchAgent_ein_und_aus()
    {
        using var dir = new TempDir();
        var autostart = new MacOSAutostart(dir.Path, () => "/Applications/Lupe & Co.app/Contents/MacOS/Lupe", supported: true);
        Assert.True(autostart.IsSupported);
        Assert.False(autostart.IsEnabled);

        Assert.Null(autostart.Set(true));
        Assert.True(autostart.IsEnabled);
        var plist = File.ReadAllText(autostart.PlistPath);
        Assert.EndsWith("de.ict-baden.lupe.plist", autostart.PlistPath);
        Assert.Contains("<string>/Applications/Lupe &amp; Co.app/Contents/MacOS/Lupe</string>", plist);
        Assert.Contains("<string>--hintergrund</string>", plist);
        Assert.Contains("<key>RunAtLoad</key>", plist);
        Assert.StartsWith("<?xml", plist);

        Assert.Null(autostart.Set(true)); // nochmal: überschreibt
        Assert.Null(autostart.Set(false));
        Assert.False(autostart.IsEnabled);
        Assert.Null(autostart.Set(false)); // schon aus
    }

    [Fact]
    public void LaunchAgent_nicht_fuer_dotnet_Host()
    {
        using var dir = new TempDir();
        var autostart = new MacOSAutostart(dir.Path, () => "/usr/local/share/dotnet/dotnet", supported: true);
        Assert.Contains("dotnet", autostart.Set(true));
        Assert.False(autostart.IsEnabled);
    }

    [Fact]
    public void LaunchAgent_ausserhalb_von_macOS()
    {
        using var dir = new TempDir();
        var autostart = new MacOSAutostart(dir.Path, () => "/x/Lupe", supported: false);
        Assert.False(autostart.IsSupported);
        Assert.Contains("nicht verfügbar", autostart.Set(true));
        Assert.Null(autostart.Set(false));
        Assert.False(Directory.Exists(dir.Path));
    }

    [Fact]
    public void Application_Support_Pfad()
    {
        Assert.EndsWith(System.IO.Path.Combine("Library", "Application Support", "Lupe"), MacOSPaths.ApplicationSupport);
        Assert.EndsWith(System.IO.Path.Combine("Library", "LaunchAgents"), MacOSPaths.LaunchAgents);
    }
}
