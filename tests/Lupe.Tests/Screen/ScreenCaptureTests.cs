using System.Runtime.CompilerServices;
using Lupe.Core.Model;
using Lupe.Core.Screen;
using Lupe.Tests.TestSupport;
using CoreNative = Lupe.Platform.Windows.Screen.Native;

using Lupe.Platform.Windows.Screen;

namespace Lupe.Tests.Screen;

/// <summary>Monitore, virtueller Desktop und Argumente – läuft überall, auch in Sitzung 0.</summary>
public sealed class ScreenCaptureTests
{
    [Fact]
    public void Monitore_sind_plausibel()
    {
        var monitors = ScreenCapture.Monitors();

        Assert.NotEmpty(monitors);
        Assert.Single(monitors, m => m.IsPrimary);
        var primary = monitors.Single(m => m.IsPrimary);
        Assert.Equal(0, primary.Bounds.X); // der Hauptmonitor beginnt immer bei 0,0
        Assert.Equal(0, primary.Bounds.Y);
        foreach (var m in monitors)
        {
            Assert.True(m.Bounds.Width > 0 && m.Bounds.Height > 0, $"{m.Name}: leere Fläche");
            Assert.True(Contains(m.Bounds, m.WorkArea), $"{m.Name}: Arbeitsbereich {m.WorkArea} liegt nicht in {m.Bounds}");
            Assert.InRange(m.Scale, 0.5, 5.0);
            Assert.StartsWith("Bildschirm ", m.Name);
        }
        Assert.Equal(monitors.Count, monitors.Select(m => m.Name).Distinct().Count());
        // von links nach rechts sortiert
        Assert.Equal(monitors.OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).Select(m => m.Bounds), monitors.Select(m => m.Bounds));
    }

    [Fact]
    public void Monitore_ueberlappen_sich_nicht()
    {
        var monitors = ScreenCapture.Monitors();
        for (int i = 0; i < monitors.Count; i++)
            for (int j = i + 1; j < monitors.Count; j++)
                Assert.False(Overlaps(monitors[i].Bounds, monitors[j].Bounds), $"{monitors[i].Name} überlappt {monitors[j].Name}");
    }

    [Fact]
    public void VirtualScreen_ist_genau_die_Huelle_aller_Monitore()
    {
        var monitors = ScreenCapture.Monitors();
        var virtualScreen = ScreenCapture.VirtualScreen;

        int left = monitors.Min(m => m.Bounds.X), top = monitors.Min(m => m.Bounds.Y);
        int right = monitors.Max(m => m.Bounds.X + m.Bounds.Width), bottom = monitors.Max(m => m.Bounds.Y + m.Bounds.Height);
        Assert.Equal(new PixelRect(left, top, right - left, bottom - top), virtualScreen);
        Assert.All(monitors, m => Assert.True(Contains(virtualScreen, m.Bounds)));
    }

    [Fact]
    public void VirtualScreen_stimmt_mit_den_Systemmetriken_in_physischen_Pixeln_ueberein()
    {
        var expected = OnThread(TestNative.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2, () => new PixelRect(
            TestNative.GetSystemMetrics(76), TestNative.GetSystemMetrics(77), TestNative.GetSystemMetrics(78), TestNative.GetSystemMetrics(79)));

        Assert.Equal(expected, ScreenCapture.VirtualScreen);
    }

    [Fact]
    public void Ergebnisse_sind_auch_fuer_nicht_DPI_faehige_Threads_physische_Pixel_und_der_Thread_bleibt_unveraendert()
    {
        var aware = (Monitors: ScreenCapture.Monitors(), Virtual: ScreenCapture.VirtualScreen);
        var (unawareMonitors, unawareVirtual, contextAfter) = OnThread(TestNative.DPI_AWARENESS_CONTEXT_UNAWARE, () =>
        {
            var monitors = ScreenCapture.Monitors();
            var vs = ScreenCapture.VirtualScreen;
            ScreenCapture.Capture(new PixelRect(vs.X, vs.Y, 8, 8));
            return (monitors, vs, TestNative.GetThreadDpiAwarenessContext());
        });

        Assert.Equal(aware.Virtual, unawareVirtual);
        Assert.Equal(aware.Monitors, unawareMonitors);
        Assert.True(TestNative.AreDpiAwarenessContextsEqual(contextAfter, TestNative.DPI_AWARENESS_CONTEXT_UNAWARE),
            "Die DPI-Einstellung des aufrufenden Threads wurde nicht zurückgesetzt.");
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-5, 10)]
    [InlineData(10, -1)]
    [InlineData(100_000, 100_000)] // 40 GB
    public void Capture_lehnt_unsinnige_Groessen_ab(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenCapture.Capture(new PixelRect(0, 0, width, height)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenCapture.TryCapture(new PixelRect(0, 0, width, height), out _, out _));
    }

    [Fact]
    public void Capture_liefert_immer_ein_undurchsichtiges_Bild_der_verlangten_Groesse()
    {
        var vs = ScreenCapture.VirtualScreen;
        foreach (var area in new[]
        {
            new PixelRect(vs.X + 3, vs.Y + 5, 123, 77),
            new PixelRect(vs.X - 40, vs.Y - 30, 100, 90),     // teils links oberhalb aller Monitore
            new PixelRect(-1_000_000, -1_000_000, 50, 40),    // ganz außerhalb
            new PixelRect(vs.X, vs.Y, 1, 1),
        })
        {
            var image = ScreenCapture.Capture(area); // wirft nicht, auch wenn Windows kein Bildschirmfoto zulässt
            Assert.Equal(area.Width, image.Width);
            Assert.Equal(area.Height, image.Height);
            Assert.Equal(area.Width * 4, image.Stride);
            Assert.Equal(area.Width * area.Height * 4, image.Bgra.Length);
            for (int i = 3; i < image.Bgra.Length; i += 4) Assert.Equal(255, image.Bgra[i]);
        }
    }

    [NoDesktopFact]
    public void Ohne_sichtbaren_Desktop_meldet_TryCapture_den_Grund_und_Capture_liefert_Schwarz()
    {
        var area = new PixelRect(0, 0, 64, 48);

        bool ok = ScreenCapture.TryCapture(area, out var image, out string? error);
        var black = ScreenCapture.Capture(area);

        Assert.False(ok);
        Assert.Null(image);
        Assert.NotNull(error);
        Assert.Contains("Bildschirmfoto nicht möglich", error);
        Assert.All(Enumerable.Range(0, black.Width * black.Height), i =>
        {
            Assert.Equal(0, black.Bgra[i * 4] | black.Bgra[i * 4 + 1] | black.Bgra[i * 4 + 2]);
            Assert.Equal(255, black.Bgra[i * 4 + 3]);
        });
    }

    [Fact]
    public void Monitornamen_lassen_sich_ohne_Absturz_abfragen()
    {
        var names = DisplayNames.Query(); // in Sitzung 0 verweigert Windows den Zugriff – dann leer
        Assert.All(names, pair =>
        {
            Assert.False(string.IsNullOrWhiteSpace(pair.Key));
            Assert.False(string.IsNullOrWhiteSpace(pair.Value));
        });
    }

    [Theory]
    [InlineData(@"\\.\DISPLAY1", 1)]
    [InlineData(@"\\.\DISPLAY12", 12)]
    [InlineData(@"\\.\display3", 3)]
    [InlineData(@"\\.\DISPLAY", null)]
    [InlineData(@"WinDisc", null)]
    [InlineData(@"", null)]
    [InlineData(@"\\.\MONITOR4", null)]
    public void Geraetenummer_aus_dem_GDI_Namen(string device, int? expected)
        => Assert.Equal(expected, WindowsScreenCapture.DeviceNumber(device));

    [Theory]
    [InlineData(1, null, "Bildschirm 1")]
    [InlineData(2, "", "Bildschirm 2")]
    [InlineData(2, "  DELL U2720Q ", "Bildschirm 2 (DELL U2720Q)")]
    public void Monitorname(int number, string? friendly, string expected)
        => Assert.Equal(expected, ScreenCapture.MonitorName(number, friendly));

    [Fact]
    public void Strukturen_haben_die_Groessen_aus_dem_Windows_SDK()
    {
        Assert.Equal(104, Unsafe.SizeOf<CoreNative.MONITORINFOEXW>());
        Assert.Equal(40, Unsafe.SizeOf<CoreNative.BITMAPINFOHEADER>());
        Assert.Equal(20, Unsafe.SizeOf<CoreNative.DISPLAYCONFIG_DEVICE_INFO_HEADER>());
        Assert.Equal(72, Unsafe.SizeOf<CoreNative.DISPLAYCONFIG_PATH_INFO>());
        Assert.Equal(64, Unsafe.SizeOf<CoreNative.DISPLAYCONFIG_MODE_INFO>());
        Assert.Equal(84, Unsafe.SizeOf<CoreNative.DISPLAYCONFIG_SOURCE_DEVICE_NAME>());
        Assert.Equal(420, Unsafe.SizeOf<CoreNative.DISPLAYCONFIG_TARGET_DEVICE_NAME>());
    }

    internal static bool Contains(PixelRect outer, PixelRect inner)
        => inner.X >= outer.X && inner.Y >= outer.Y
        && inner.X + inner.Width <= outer.X + outer.Width && inner.Y + inner.Height <= outer.Y + outer.Height;

    private static bool Overlaps(PixelRect a, PixelRect b)
        => a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    /// <summary>Führt <paramref name="func"/> auf einem frischen Thread mit der angegebenen DPI-Einstellung aus.</summary>
    private static T OnThread<T>(nint dpiContext, Func<T> func)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                TestNative.SetThreadDpiAwarenessContext(dpiContext);
                result = func();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.Start();
        thread.Join();
        if (error is not null) throw new InvalidOperationException("Fehler im Hilfsthread", error);
        return result;
    }
}

/// <summary>
/// Die GDI-Kopie selbst (DIB-Abschnitt, BitBlt, Zeilenrichtung, Versatz, Alpha) gegen einen Speicher-Gerätekontext –
/// echter GDI-Weg, aber ohne sichtbaren Desktop, läuft also auch in Sitzung 0.
/// </summary>
public sealed class GdiCopyTests
{
    /// <summary>Jeder Pixel trägt seine Koordinaten: B = x, G = y, R = x xor y; Alpha absichtlich 0.</summary>
    private static PixelImage Coordinates(int width, int height)
    {
        var px = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                px[i] = (byte)x;
                px[i + 1] = (byte)y;
                px[i + 2] = (byte)(x ^ y);
                px[i + 3] = 0;
            }
        }
        return new PixelImage(width, height, px);
    }

    [Fact]
    public void Kopiert_den_Ausschnitt_pixelgenau_von_oben_nach_unten_mit_Alpha_255()
    {
        using var source = new MemoryDc(Coordinates(250, 200));
        var area = new PixelRect(37, 21, 121, 90); // ungerade Breite: kein Zeilen-Padding bei 32 Bit

        Assert.True(WindowsScreenCapture.CopyFromDc(source.Handle, area, CoreNative.SRCCOPY, out var image, out string? error), error);

        Assert.Equal(121, image.Width);
        Assert.Equal(90, image.Height);
        Assert.Equal(121 * 4, image.Stride);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int i = y * image.Stride + x * 4;
                int sx = x + area.X, sy = y + area.Y;
                Assert.Equal((byte)sx, image.Bgra[i]);
                Assert.Equal((byte)sy, image.Bgra[i + 1]);
                Assert.Equal((byte)(sx ^ sy), image.Bgra[i + 2]);
                Assert.Equal(255, image.Bgra[i + 3]);
            }
        }
    }

    [Fact]
    public void Mit_CAPTUREBLT_wie_beim_Bildschirm()
    {
        using var source = new MemoryDc(Coordinates(64, 64));

        Assert.True(WindowsScreenCapture.CopyFromDc(source.Handle, new PixelRect(0, 0, 64, 64), CoreNative.SRCCOPY | CoreNative.CAPTUREBLT, out var image, out _));

        Assert.Equal(63, image.Bgra[(63 * 64 + 63) * 4]);
        Assert.Equal(255, image.Bgra[3]);
    }

    [Fact]
    public void QR_Code_im_Bildschirmbild_bleibt_nach_der_Kopie_lesbar()
    {
        const string text = "Lupe Bildschirmausschnitt https://ict-baden.de/?q=1&x=2"; // ASCII: TestCodes schreibt ISO-8859-1
        var screen = TestCodes.Embed(TestCodes.Render(text, BarcodeFormat.QR_CODE, 220, 220), 800, 600, 431, 287);
        using var source = new MemoryDc(screen);

        Assert.True(WindowsScreenCapture.CopyFromDc(source.Handle, new PixelRect(400, 250, 300, 300), CoreNative.SRCCOPY, out var image, out _));

        Assert.Equal(text, DesktopProbe.DecodeQr(image));
        Assert.Equal(1.0, DesktopProbe.Agreement(image, CropOf(screen, 400, 250, 300, 300), tolerance: 0));
    }

    [Fact]
    public void Ausschnitt_teils_ausserhalb_der_Quelle_wird_dort_schwarz()
    {
        using var source = new MemoryDc(Coordinates(100, 80));

        Assert.True(WindowsScreenCapture.CopyFromDc(source.Handle, new PixelRect(90, 70, 20, 20), CoreNative.SRCCOPY, out var image, out _));

        Assert.Equal(20, image.Width);
        int inside = (5 * image.Stride) + 5 * 4; // Quelle (95, 75)
        Assert.Equal(95, image.Bgra[inside]);
        Assert.Equal(75, image.Bgra[inside + 1]);
        int outside = (15 * image.Stride) + 15 * 4; // Quelle (105, 85) gibt es nicht
        Assert.Equal(0, image.Bgra[outside] | image.Bgra[outside + 1] | image.Bgra[outside + 2]);
        Assert.Equal(255, image.Bgra[outside + 3]);
    }

    [Fact]
    public void Ungueltige_Quelle_meldet_Fehler_statt_zu_werfen()
    {
        bool ok = WindowsScreenCapture.CopyFromDc(0, new PixelRect(0, 0, 10, 10), CoreNative.SRCCOPY, out var image, out string? error);

        Assert.False(ok);
        Assert.Null(image);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Viele_Kopien_hintereinander_verlieren_keine_GDI_Objekte()
    {
        using var source = new MemoryDc(Coordinates(64, 64));
        int before = GdiObjects();
        for (int i = 0; i < 300; i++)
            Assert.True(WindowsScreenCapture.CopyFromDc(source.Handle, new PixelRect(i % 32, 0, 32, 32), CoreNative.SRCCOPY, out _, out _));
        Assert.InRange(GdiObjects() - before, -10, 10);
    }

    private static int GdiObjects()
        => (int)GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle, 0 /* GR_GDIOBJECTS */);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetGuiResources(nint process, uint flags);

    private static PixelImage CropOf(PixelImage image, int x, int y, int width, int height)
    {
        var px = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
            Buffer.BlockCopy(image.Bgra, (y + row) * image.Stride + x * 4, px, row * width * 4, width * 4);
        return new PixelImage(width, height, px);
    }
}

/// <summary>Echte Bildschirmfotos eigener Fenster – nur mit sichtbarem Desktop (sonst übersprungen, siehe <see cref="DesktopFactAttribute"/>).</summary>
[Collection(DesktopCollection.Name)]
public sealed class ScreenCaptureDesktopTests
{
    [DesktopFact]
    public void Bildschirmfoto_eines_eigenen_Fensters_mit_QR_Code_ist_lesbar()
    {
        const string text = "Lupe-Bildschirmtest 0815";
        var code = TestCodes.Render(text, BarcodeFormat.QR_CODE, 240, 240);
        var (x, y) = DesktopProbe.Spot(60);
        using var window = TestImageWindow.Show(code, x, y);

        var shot = ScreenCapture.Capture(window.Bounds);

        Assert.Equal(text, DesktopProbe.DecodeQr(shot));
        Assert.True(DesktopProbe.Agreement(shot, code) > 0.99);
    }

    [DesktopFact]
    public void Ausschnitt_mit_Rand_hat_das_Fenster_am_richtigen_Versatz()
    {
        var pattern = DesktopProbe.Pattern(80);
        var (x, y) = DesktopProbe.Spot(80);
        using var window = TestImageWindow.Show(pattern, x, y);
        var b = window.Bounds;

        var shot = ScreenCapture.Capture(new PixelRect(b.X - 17, b.Y - 9, b.Width + 40, b.Height + 30));

        Assert.True(DesktopProbe.Agreement(shot, pattern, 17, 9) > 0.99);
    }

    [DesktopFact]
    public void Ganzer_virtueller_Desktop_enthaelt_das_Fenster_an_seiner_Position()
    {
        var pattern = DesktopProbe.Pattern(64);
        var (x, y) = DesktopProbe.Spot(100);
        using var window = TestImageWindow.Show(pattern, x, y);
        var vs = ScreenCapture.VirtualScreen;

        var shot = ScreenCapture.Capture(vs);

        Assert.Equal(vs.Width, shot.Width);
        Assert.Equal(vs.Height, shot.Height);
        Assert.True(DesktopProbe.Agreement(shot, pattern, window.Bounds.X - vs.X, window.Bounds.Y - vs.Y) > 0.99);
    }

    [DesktopFact]
    public void Geschichtete_Fenster_werden_mit_aufgenommen()
    {
        var pattern = DesktopProbe.Pattern(72);
        var (x, y) = DesktopProbe.Spot(120);
        using var window = TestImageWindow.Show(pattern, x, y, layered: true);

        Assert.True(ScreenCapture.TryCapture(window.Bounds, out var shot, out string? error), error);
        Assert.True(DesktopProbe.Agreement(shot, pattern) > 0.99);
    }

    [DesktopFact]
    public void Ausschnitt_ueber_Monitorgrenzen_hinweg_ist_vollstaendig()
    {
        var monitors = ScreenCapture.Monitors();
        var vs = ScreenCapture.VirtualScreen;
        // Mit einem Monitor: ein Streifen quer über den ganzen Desktop; mit mehreren: über die Grenze der ersten beiden.
        var area = monitors.Count >= 2
            ? new PixelRect(monitors[1].Bounds.X - 50, monitors[1].Bounds.Y + 10, 100, 60)
            : new PixelRect(vs.X, vs.Y + 10, vs.Width, 60);

        Assert.True(ScreenCapture.TryCapture(area, out var shot, out string? error), error);
        Assert.Equal(area.Width, shot.Width);
        Assert.Equal(area.Height, shot.Height);
    }
}
