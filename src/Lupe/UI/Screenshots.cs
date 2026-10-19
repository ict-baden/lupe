// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Lupe.Core.Model;
using Lupe.Core.Resources;
using Lupe.Demo;
using Lupe.Services;
using Lupe.UI.Theme;
using CorePixelRect = Lupe.Core.Screen.PixelRect;
using MonitorArea = Lupe.Core.Screen.MonitorArea;

namespace Lupe.UI;

/// <summary>
/// <c>Lupe.exe --screenshots &lt;ordner&gt;</c>: Demo laden, jede Seite in einen festen Zustand bringen und als PNG
/// speichern (144 dpi), dazu das Auswahl-Overlay und den Ergebnis-Hinweis. Läuft ohne Bildschirm (Avalonia.Headless mit
/// Skia, siehe <see cref="Program"/>) – auch in einer Dienst-Sitzung oder unter Linux ohne X-Server. Mica gibt es in
/// Bildern nicht, deshalb normaler Hintergrund aus dem Theme; Akzentfarbe fest (Windows-Standardblau).
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
        Console.WriteLine(string.Format(Strings.App_Screenshots_Start, AppInfo.Version, folder));

        using var state = AppState.OpenDemo(strict: true, DemoData.ScreenshotTime);
        // Wie im echten Betrieb: Tastenkürzel angemeldet (ohne es hier wirklich anzumelden)
        state.SetHotkeyStatus(true, null);
        int written = 0;
        foreach (var dark in variants)
        {
            string suffix = dark ? "dunkel" : "hell";
            ThemeManager.Force(dark);
            var window = Open(new MainWindow(state, screenshotMode: true), Width, Height);
            try
            {
                foreach (var slug in MainWindow.PageSlugs)
                {
                    await window.PrepareScreenshotAsync(slug);
                    // Fluent-Übergänge (Knöpfe, Listen) zu Ende laufen lassen
                    await Task.Delay(300);
                    await Ui.IdleAsync();
                    written += Save(window, Path.Combine(folder, $"{slug}-{suffix}.png"), Width, Height, Scale);
                }
            }
            finally
            {
                window.Close();
            }

            written += await SaveOverlayAsync(Path.Combine(folder, $"auswahl-{suffix}.png"));
            written += await SaveToastAsync(state, Path.Combine(folder, $"treffer-{suffix}.png"));
        }
        Console.WriteLine(string.Format(Strings.App_Screenshots_Written, written));
        return 0;
    }

    /// <summary>Auswahl-Overlay über einem Demo-Desktop, Rechteck um den QR-Code der Rechnung.</summary>
    private static async Task<int> SaveOverlayAsync(string file)
    {
        const int w = 1600, h = 900;
        var (desktop, code) = DemoData.DesktopImage(w, h);
        var monitor = new MonitorArea(new CorePixelRect(0, 0, w, h), new CorePixelRect(0, 0, w, h - 44), 1, true, "Demo");
        var overlay = Open(new OverlayWindow(desktop, monitor.Bounds, monitor, primary: true, screenshot: true), w, h);
        try
        {
            overlay.Relayout();
            overlay.ShowSelection(code.Inflate(new Thickness(26, 22)));
            await Task.Delay(100);
            await Ui.IdleAsync();
            return SaveControl(overlay.Surface, file, w, h, scale: 1);
        }
        finally
        {
            overlay.CloseOverlay();
        }
    }

    /// <summary>Ergebnis-Hinweis nach einem Bildschirm-Scan (kopiert und eingetippt).</summary>
    private static async Task<int> SaveToastAsync(AppState state, string file)
    {
        var code = new DecodedCode { Text = DemoData.InvoiceUrl, Format = "QR_CODE" };
        var item = new CodeItem(code, ScanSource.Bildschirm, DemoData.ScreenshotTime, state.LinkFor(code.Text));
        var toast = new ResultToast(state, item, string.Format(Strings.App_Scan_Recognized, item.FormatLabel), Labels.Actions(ResultActions.Kopieren | ResultActions.Eintippen), "", null, screenshot: true);
        var window = Open(toast, 420, double.NaN);
        try
        {
            await Task.Delay(100);
            await Ui.IdleAsync();
            // Das ganze Fenster samt Schattenrand, auf dem Hintergrund der App
            return Save(window, file, window.Bounds.Width, window.Bounds.Height, Scale);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Fenster in fester Größe (ohne Bildschirm), <paramref name="height"/> NaN = Höhe nach Inhalt.</summary>
    private static T Open<T>(T window, double width, double height) where T : Window
    {
        window.WindowDecorations = WindowDecorations.None;
        window.ShowActivated = false;
        window.Topmost = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.SizeToContent = SizeToContent.Manual;
        window.Width = width;
        if (double.IsNaN(height) && window.Content is Control content)
        {
            // Höhe nach Inhalt: vorher selbst messen – ohne Bildschirm passt SizeToContent die Fenstergröße nicht an,
            // und eine spätere Größenänderung zeichnet der Headless-Renderer verzerrt
            content.Measure(new Size(width, double.PositiveInfinity));
            height = Math.Ceiling(content.DesiredSize.Height);
        }
        window.Height = height;
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static int Save(Window window, string file, double width, double height, double scale)
    {
        window.UpdateLayout();
        if (Math.Abs(window.Bounds.Width - width) > 1 || Math.Abs(window.Bounds.Height - height) > 1)
            throw new InvalidOperationException(string.Format(Strings.App_Screenshots_WrongSize, window.Bounds.Width, window.Bounds.Height, width, height));
        return SaveControl(window, file, width, height, scale);
    }

    private static int SaveControl(Visual root, string file, double width, double height, double scale)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Round(width * scale), (int)Math.Round(height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(root);
        bitmap.Save(file, PngBitmapEncoderOptions.Default);
        Console.WriteLine($"  {Path.GetFileName(file)}");
        return 1;
    }
}
