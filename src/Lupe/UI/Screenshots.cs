using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lupe.Core.Model;
using Lupe.Core.Screen;
using Lupe.Demo;
using Lupe.Interop;
using Lupe.Services;
using Lupe.UI.Theme;

namespace Lupe.UI;

/// <summary>
/// <c>Lupe.exe --screenshots &lt;ordner&gt;</c>: Demo laden, jede Seite in einen festen Zustand bringen und als PNG
/// speichern (144 dpi), dazu das Auswahl-Overlay und den Ergebnis-Hinweis. Die Fenster liegen außerhalb des
/// Bildschirms und werden nie aktiviert – das funktioniert auch ohne sichtbaren Desktop (Dienst-Sitzung). Mica gibt
/// es in Bildern nicht, deshalb normaler Hintergrund aus dem Theme.
/// </summary>
internal static class Screenshots
{
    private const double Scale = 1.5; // 144 dpi
    private const double Width = 1100;
    private const double Height = 740;

    public static async Task<int> RunAsync(string folder, string theme)
    {
        Directory.CreateDirectory(folder);
        bool[] variants = theme switch
        {
            "hell" => [false],
            "dunkel" => [true],
            _ => [false, true],
        };
        Console.WriteLine($"Lupe {AppInfo.Version} – Screenshots nach {folder}");

        using var state = AppState.OpenDemo(strict: true, DemoData.ScreenshotTime);
        // Wie im echten Betrieb: Tastenkürzel angemeldet (ohne es hier wirklich anzumelden)
        state.SetHotkeyStatus(true, null);
        int written = 0;
        foreach (var dark in variants)
        {
            string suffix = dark ? "dunkel" : "hell";
            ThemeManager.Initialize(forceDark: dark);
            var window = CreateWindow(new MainWindow(state, screenshotMode: true), Width, Height);
            try
            {
                foreach (var slug in MainWindow.PageSlugs)
                {
                    await window.PrepareScreenshotAsync(slug);
                    // Fluent-Animationen (Knöpfe, Listen) zu Ende laufen lassen
                    await Task.Delay(300);
                    await Ui.IdleAsync();
                    written += Save(window, Path.Combine(folder, $"{slug}-{suffix}.png"), Width, Height);
                }
            }
            finally
            {
                window.Close();
            }

            written += await SaveOverlayAsync(Path.Combine(folder, $"auswahl-{suffix}.png"));
            written += await SaveToastAsync(state, Path.Combine(folder, $"treffer-{suffix}.png"));
        }
        Console.WriteLine($"{written} Bilder geschrieben.");
        return 0;
    }

    /// <summary>Auswahl-Overlay über einem Demo-Desktop, Rechteck um den QR-Code der Rechnung.</summary>
    private static async Task<int> SaveOverlayAsync(string file)
    {
        const int w = 1600, h = 900;
        var (desktop, code) = DemoData.DesktopImage(w, h);
        var monitors = new[] { new MonitorArea(new PixelRect(0, 0, w, h), new PixelRect(0, 0, w, h - 44), 1, true, "Demo") };
        var overlay = new SelectionOverlay(desktop, new PixelRect(0, 0, w, h), monitors, screenshot: true);
        CreateWindow(overlay, w, h);
        try
        {
            overlay.Relayout();
            var selection = code;
            selection.Inflate(26, 22);
            overlay.ShowSelection(selection);
            await Task.Delay(100);
            await Ui.IdleAsync();
            return SaveElement(overlay.Surface, file, w, h, scale: 1);
        }
        finally
        {
            overlay.Close();
        }
    }

    /// <summary>Ergebnis-Hinweis nach einem Bildschirm-Scan (kopiert und eingetippt).</summary>
    private static async Task<int> SaveToastAsync(AppState state, string file)
    {
        var code = new DecodedCode { Text = DemoData.InvoiceUrl, Format = "QR_CODE" };
        var item = new CodeItem(code, ScanSource.Bildschirm, DemoData.ScreenshotTime, state.LinkFor(code.Text));
        var toast = new ResultToast(state, item, "QR-Code erkannt", Labels.Actions(ResultActions.Kopieren | ResultActions.Eintippen), "", null, screenshot: true);
        var window = CreateWindow(toast, 420, double.NaN);
        try
        {
            await Task.Delay(100);
            await Ui.IdleAsync();
            // Das ganze (durchsichtige) Fenster samt Schattenrand, auf dem Hintergrund der App
            return SaveElement(window, file, window.ActualWidth, window.ActualHeight, Scale, background: true);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Fenster außerhalb des Bildschirms in fester Größe, ohne Aktivierung und ohne Mica.
    /// <paramref name="height"/> NaN = Höhe nach Inhalt.
    /// </summary>
    internal static T CreateWindow<T>(T window, double width, double height) where T : Window
    {
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Topmost = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        // Zuerst klein anlegen: Die Größengrenze fragt Windows schon beim Erzeugen ab, der Haken dagegen greift erst
        // danach – die endgültige Größe kommt deshalb nach Show()
        window.Width = 800;
        if (!double.IsNaN(height)) window.Height = 600;
        window.SourceInitialized += (_, _) =>
            HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(AllowLargerThanScreen);
        window.Show();
        window.Width = width;
        if (!double.IsNaN(height)) window.Height = height;
        window.UpdateLayout();
        return window;
    }

    /// <summary>Ohne sichtbaren Desktop ist der „Bildschirm“ oft nur 1024×768 groß – Fenster dürfen hier größer sein.</summary>
    private static nint AllowLargerThanScreen(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != Native.WM_GETMINMAXINFO) return 0;
        var info = Marshal.PtrToStructure<Native.MINMAXINFO>(lParam);
        info.ptMaxSize = info.ptMaxTrackSize = new Native.POINT { X = 16_000, Y = 16_000 };
        Marshal.StructureToPtr(info, lParam, false);
        return 0;
    }

    private static int Save(Window window, string file, double width, double height)
    {
        window.UpdateLayout();
        var root = (FrameworkElement)window.Content;
        if (Math.Abs(root.ActualWidth - width) > 1 || Math.Abs(root.ActualHeight - height) > 1)
            throw new InvalidOperationException($"Das Fenster hat {root.ActualWidth:0}×{root.ActualHeight:0} statt {width:0}×{height:0} Punkte – die Bilder wären verzerrt.");
        return SaveElement(root, file, width, height, Scale, background: true);
    }

    private static int SaveElement(FrameworkElement root, string file, double width, double height, double scale, bool background = false)
    {
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Round(width * scale), (int)Math.Round(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        if (background)
        {
            // Den Hintergrund zeichnet das Fenster selbst, nicht sein Inhalt – deshalb zuerst eine Fläche darunter
            var brush = Application.Current.TryFindResource("ApplicationBackgroundBrush") as Brush ?? Brushes.White;
            var under = new DrawingVisual();
            using (var dc = under.RenderOpen())
                dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
            bitmap.Render(under);
        }
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(file)) encoder.Save(stream);
        Console.WriteLine($"  {Path.GetFileName(file)}");
        return 1;
    }
}
