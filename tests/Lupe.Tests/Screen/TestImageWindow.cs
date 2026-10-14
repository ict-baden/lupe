using System.Runtime.InteropServices;
using Lupe.Core.Model;
using Lupe.Core.Screen;

namespace Lupe.Tests.Screen;

/// <summary>
/// Zeigt ein <see cref="PixelImage"/> 1:1 in einem randlosen, obenauf liegenden Fenster (Systemklasse STATIC mit
/// SS_BITMAP – kein eigener Fensterprozeduren-Code nötig). Wahlweise als geschichtetes Fenster (WS_EX_LAYERED), um
/// CAPTUREBLT zu prüfen.
/// </summary>
internal sealed class TestImageWindow : IDisposable
{
    private readonly UiThread _ui;
    private readonly nint _bitmap;

    private TestImageWindow(UiThread ui, nint handle, nint bitmap, PixelRect bounds)
    {
        _ui = ui;
        Handle = handle;
        _bitmap = bitmap;
        Bounds = bounds;
    }

    public nint Handle { get; }

    /// <summary>Lage des Fensters in physischen Pixeln des virtuellen Desktops.</summary>
    public PixelRect Bounds { get; }

    public static TestImageWindow Show(PixelImage image, int x, int y, bool layered = false)
    {
        var ui = new UiThread();
        try
        {
            nint bitmap = ui.Invoke(() => CreateBitmap(image));
            uint exStyle = TestNative.WS_EX_TOPMOST | TestNative.WS_EX_TOOLWINDOW | TestNative.WS_EX_NOACTIVATE
                | (layered ? TestNative.WS_EX_LAYERED : 0);
            nint hwnd = ui.CreateWindow(exStyle, "STATIC", TestNative.WS_POPUP | TestNative.SS_BITMAP, x, y, image.Width, image.Height);
            var bounds = ui.Invoke(() =>
            {
                if (layered) TestNative.SetLayeredWindowAttributes(hwnd, 0, 255, TestNative.LWA_ALPHA);
                TestNative.SendMessageW(hwnd, TestNative.STM_SETIMAGE, TestNative.IMAGE_BITMAP, bitmap);
                TestNative.SetWindowPos(hwnd, TestNative.HWND_TOPMOST, x, y, image.Width, image.Height,
                    TestNative.SWP_NOACTIVATE | TestNative.SWP_SHOWWINDOW);
                TestNative.UpdateWindow(hwnd);
                TestNative.GetWindowRect(hwnd, out var r);
                return new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            });
            // Warten, bis die Desktopkomposition das Fenster wirklich zeigt.
            for (int i = 0; i < 3; i++)
            {
                ui.Invoke(() => { }); // WM_PAINT ist verarbeitet, sobald die Warteschlange leer ist
                TestNative.DwmFlush();
                Thread.Sleep(50);
            }
            return new TestImageWindow(ui, hwnd, bitmap, bounds);
        }
        catch
        {
            ui.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _ui.Dispose(); // zerstört das Fenster
        TestNative.DeleteObject(_bitmap);
    }

    /// <summary>32-Bit-DIB (von oben nach unten) mit den Pixeln des Bildes.</summary>
    internal static nint CreateBitmap(PixelImage image) => CreateDib(image, out _);

    internal static nint CreateDib(PixelImage image, out nint bits)
    {
        var info = new TestNative.BITMAPINFO
        {
            bmiHeader = new TestNative.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<TestNative.BITMAPINFOHEADER>(),
                biWidth = image.Width,
                biHeight = -image.Height,
                biPlanes = 1,
                biBitCount = 32,
            },
        };
        nint bitmap = TestNative.CreateDIBSection(0, ref info, 0, out bits, 0, 0);
        if (bitmap == 0 || bits == 0) throw new System.ComponentModel.Win32Exception();
        for (int row = 0; row < image.Height; row++)
            Marshal.Copy(image.Bgra, row * image.Stride, bits + row * image.Width * 4, image.Width * 4);
        return bitmap;
    }
}

/// <summary>Speicher-Gerätekontext mit einer Bitmap als Quelle für die GDI-Kopie – funktioniert auch ohne sichtbaren Desktop.</summary>
internal sealed class MemoryDc : IDisposable
{
    private readonly nint _bitmap;
    private readonly nint _previous;

    public MemoryDc(PixelImage image)
    {
        Handle = TestNative.CreateCompatibleDC(0);
        if (Handle == 0) throw new System.ComponentModel.Win32Exception();
        _bitmap = TestImageWindow.CreateDib(image, out nint bits);
        Bits = bits;
        _previous = TestNative.SelectObject(Handle, _bitmap);
    }

    public nint Handle { get; }
    public nint Bits { get; }

    public void Dispose()
    {
        TestNative.SelectObject(Handle, _previous);
        TestNative.DeleteObject(_bitmap);
        TestNative.DeleteDC(Handle);
    }
}
