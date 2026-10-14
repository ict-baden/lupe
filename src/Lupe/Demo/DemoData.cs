using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lupe.Core.Camera;
using Lupe.Core.Model;
using Lupe.Services;
using ZXingCpp;

namespace Lupe.Demo;

/// <summary>
/// Eingebaute Beispiele für <c>--demo</c> und <c>--screenshots</c>: Demo-Kamera mit erzeugten Codes, Verlauf,
/// ein Lieferschein mit mehreren Codes, ein Desktop für das Auswahl-Overlay. Alle Codes erzeugt zxing-cpp – sie sind
/// also echt lesbar. Links nur auf example.org/.example (reserviert, gehören niemandem).
/// </summary>
internal static class DemoData
{
    public const string CameraId = "lupe-demo-kamera";
    public static CameraInfo Camera { get; } = new(CameraId, "Demo-Kamera");

    public const string DeliveryUrl = "https://example.org/lieferung/4711";
    public const string InvoiceUrl = "https://example.org/rechnung/2026-118";
    public const string Ean = "4012345000009";
    public const string DeliveryNote = "LS-2026-004711";
    public const string Serial = "SN:LUP-000815;LOT:2610A";

    /// <summary>Feste Zeit für reproduzierbare Screenshots (14.10.2026, 10:42 Ortszeit).</summary>
    public static DateTimeOffset ScreenshotTime { get; } = new(new DateTime(2026, 10, 14, 10, 42, 0, DateTimeKind.Local));

    public static LupeSettings Settings() => new()
    {
        Actions = ResultActions.Kopieren,
        ScreenActions = ResultActions.Kopieren | ResultActions.Eintippen,
        LinkRules =
        [
            new(@"^https://\S+$"),
            new(@"^\d{13}$", "https://warenwirtschaft.example/artikel/"),
            new(@"^LS-\d{4}-\d{6}$", "https://erp.example/lieferschein/", Enabled: false),
        ],
    };

    public static IReadOnlyList<HistoryEntry> History(DateTimeOffset now)
    {
        HistoryEntry E(TimeSpan ago, ScanSource source, string format, string text, ResultActions done = ResultActions.Kopieren) =>
            new() { Time = now - ago, Source = source, Format = format, Text = text, ActionsDone = done };
        return
        [
            E(TimeSpan.FromMinutes(2), ScanSource.Kamera, "QR_CODE", DeliveryUrl),
            E(TimeSpan.FromMinutes(6), ScanSource.Bildschirm, "QR_CODE", InvoiceUrl, ResultActions.Kopieren | ResultActions.Eintippen),
            E(TimeSpan.FromMinutes(15), ScanSource.Datei, "CODE_128", DeliveryNote),
            E(TimeSpan.FromMinutes(15), ScanSource.Datei, "DATA_MATRIX", Serial, ResultActions.Keine),
            E(TimeSpan.FromMinutes(15), ScanSource.Datei, "EAN_13", Ean, ResultActions.Keine),
            E(TimeSpan.FromMinutes(48), ScanSource.Zwischenablage, "QR_CODE", "WIFI:T:WPA;S:Gaeste;P:Willkommen-2026;;"),
            E(TimeSpan.FromMinutes(81), ScanSource.Kamera, "EAN_13", Ean, ResultActions.Kopieren | ResultActions.LinkOeffnen),
            E(TimeSpan.FromHours(3.2), ScanSource.Bildschirm, "AZTEC", "TICKET 7X9-KL2 WAGEN 8 PLATZ 64", ResultActions.Eintippen),
            E(TimeSpan.FromHours(22), ScanSource.Bildschirm, "QR_CODE", "BEGIN:VCARD\nVERSION:3.0\nN:Muster;Anna\nORG:ICT-Baden\nTEL:+41 56 000 00 00\nEND:VCARD"),
            E(TimeSpan.FromHours(26), ScanSource.Kamera, "PDF_417", "PAKET 1Z999AA10123456784"),
            E(TimeSpan.FromHours(50), ScanSource.Datei, "CODE_39", "INV-2026-0042"),
            E(TimeSpan.FromHours(75), ScanSource.Kamera, "QR_CODE", "https://example.com/termine?id=118", ResultActions.Kopieren | ResultActions.LinkOeffnen),
        ];
    }

    /// <summary>Letzte Bildschirm-Treffer für die Seite „Bildschirm“.</summary>
    /// <param name="entries">Der Verlauf der Demo – die Zeilen gehören zu seinen Einträgen (Kopieren wird dort vermerkt).</param>
    public static IEnumerable<CodeItem> ScreenResults(DateTimeOffset now, Func<string, Uri?> linkFor, IReadOnlyList<HistoryEntry> entries) =>
        entries.Where(e => e.Source == ScanSource.Bildschirm && e.Time <= now).Take(3)
            .Select(e => new CodeItem(new DecodedCode { Text = e.Text, Format = e.Format }, e.Source, e.Time, linkFor(e.Text), entry: e));

    // ================= Bilder =================

    /// <summary>Demo-Kamera: ein Lieferschein-Etikett, dann ein Artikel mit EAN, dann ein Typenschild – dazwischen leerer Tisch.</summary>
    public static IFrameSource CreateCamera()
    {
        var blank = Scene(640, 360, null, 0, 0);
        var frames = new List<PixelImage>();
        (string Text, DemoFormat Format)[] codes = [(DeliveryUrl, DemoFormat.Qr), (Ean, DemoFormat.Ean13), (Serial, DemoFormat.DataMatrix)];
        foreach (var (text, format) in codes)
        {
            var card = Card(text, format);
            for (int i = 0; i < 8; i++)
                frames.Add(Scene(640, 360, card, Math.Sin(i * 0.8) * 6, Math.Cos(i * 0.6) * 4));
            for (int i = 0; i < 8; i++) frames.Add(blank);
        }
        return new FakeFrameSource(frames, framesPerSecond: 8, name: Camera.Name);
    }

    /// <summary>Ein Kamerabild für den Screenshot: Etikett mit QR-Code auf dem Tisch.</summary>
    public static PixelImage CameraFrame() => Scene(1280, 720, Card(DeliveryUrl, DemoFormat.Qr, scale: 2), 40, -10);

    /// <summary>Lieferschein mit vier Codes (QR, Data Matrix, Code 128, EAN-13) – für die Seite „Datei“.</summary>
    public static PixelImage DeliveryNoteImage()
    {
        const int w = 1240, h = 880;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            Text(dc, "Lieferschein", 64, 56, 40, FontWeights.SemiBold);
            Text(dc, $"Nr. {DeliveryNote} · 14.10.2026", 64, 112, 20, FontWeights.Normal, Gray(90));
            Text(dc, "ICT-Baden · Musterstrasse 1 · 5400 Baden", 64, 146, 16, FontWeights.Normal, Gray(120));
            dc.DrawImage(Code(DeliveryUrl, DemoFormat.Qr, 210, 210), new Rect(w - 64 - 210, 40, 210, 210));

            // Tabelle
            var line = new Pen(Gray(205), 1.5);
            double y = 300;
            Text(dc, "Pos.", 64, y, 16, FontWeights.SemiBold);
            Text(dc, "Artikel", 150, y, 16, FontWeights.SemiBold);
            Text(dc, "Menge", 760, y, 16, FontWeights.SemiBold);
            dc.DrawLine(line, new Point(64, y + 34), new Point(w - 64, y + 34));
            string[] items = ["Etikettendrucker, 300 dpi", "Thermo-Etiketten 50 × 25 mm", "Handscanner mit Ständer"];
            for (int i = 0; i < items.Length; i++)
            {
                double row = y + 50 + i * 40;
                Text(dc, (i + 1).ToString(Labels.German), 64, row, 16, FontWeights.Normal);
                Text(dc, items[i], 150, row, 16, FontWeights.Normal);
                Text(dc, i == 1 ? "12" : "1", 760, row, 16, FontWeights.Normal);
                dc.DrawLine(line, new Point(64, row + 32), new Point(w - 64, row + 32));
            }

            dc.DrawImage(Code(DeliveryNote, DemoFormat.Code128, 460, 110), new Rect(64, 560, 460, 110));
            Text(dc, "Lieferschein-Nr.", 64, 680, 14, FontWeights.Normal, Gray(110));
            dc.DrawImage(Code(Ean, DemoFormat.Ean13, 300, 120), new Rect(600, 552, 300, 120));
            Text(dc, "Artikel (EAN)", 600, 680, 14, FontWeights.Normal, Gray(110));
            dc.DrawImage(Code(Serial, DemoFormat.DataMatrix, 150, 150), new Rect(w - 64 - 150, 532, 150, 150));
            Text(dc, "Seriennummer", w - 64 - 150, 690, 14, FontWeights.Normal, Gray(110));
            Text(dc, "Bitte bei Rückfragen die Lieferschein-Nummer angeben.", 64, 790, 15, FontWeights.Normal, Gray(120));
        }
        return Render(visual, w, h);
    }

    /// <summary>Ein Desktop mit einer Rechnung im Fenster – Hintergrund des Auswahl-Overlays im Screenshot.</summary>
    public static (PixelImage Image, Rect CodeArea) DesktopImage(int w, int h)
    {
        var visual = new DrawingVisual();
        var code = new Rect(w * 0.585, h * 0.50, 170, 170);
        using (var dc = visual.RenderOpen())
        {
            var wallpaper = new LinearGradientBrush(Color.FromRgb(0x2B, 0x3A, 0x55), Color.FromRgb(0x6B, 0x4A, 0x6E), 35);
            dc.DrawRectangle(wallpaper, null, new Rect(0, 0, w, h));

            // Fenster mit Dokument
            var window = new Rect(w * 0.18, h * 0.08, w * 0.62, h * 0.80);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0)), null, new Rect(window.X + 4, window.Y + 8, window.Width, window.Height), 8, 8);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)), new Pen(Gray(200), 1), window, 8, 8);
            Text(dc, "Rechnung_2026-118.pdf – Dokumentanzeige", window.X + 16, window.Y + 10, 13, FontWeights.Normal, Gray(60));
            var paper = new Rect(window.X + 60, window.Y + 46, window.Width - 120, window.Height - 46);
            dc.DrawRectangle(Brushes.White, new Pen(Gray(220), 1), paper);
            Text(dc, "Rechnung 2026-118", paper.X + 40, paper.Y + 34, 26, FontWeights.SemiBold);
            for (int i = 0; i < 9; i++)
                dc.DrawRoundedRectangle(new SolidColorBrush(Gray(228).Color), null, new Rect(paper.X + 40, paper.Y + 96 + i * 26, (i % 3 == 2 ? 0.45 : 0.75) * paper.Width, 10), 3, 3);
            Text(dc, "Bequem bezahlen: Code scannen", paper.X + 40, code.Y + 20, 15, FontWeights.SemiBold, Gray(70));
            Text(dc, "Betrag CHF 1’240.00 · fällig am 13.11.2026", paper.X + 40, code.Y + 48, 14, FontWeights.Normal, Gray(110));
            dc.DrawImage(Code(InvoiceUrl, DemoFormat.Qr, (int)code.Width, (int)code.Height), code);

            // Taskleiste
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x20)), null, new Rect(0, h - 44, w, 44));
            for (int i = 0; i < 6; i++)
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), null, new Rect(w / 2.0 - 150 + i * 52, h - 36, 28, 28), 5, 5);
        }
        return (Render(visual, w, h), code);
    }

    // ================= Bausteine =================

    /// <summary>Ein Etikett (weiße Karte) mit Code und kleiner Beschriftung.</summary>
    private static BitmapSource Card(string text, DemoFormat format, double scale = 1)
    {
        bool linear = format is DemoFormat.Ean13 or DemoFormat.Code128;
        int codeW = (int)((linear ? 200 : 130) * scale), codeH = (int)((linear ? 80 : 130) * scale);
        int pad = (int)(18 * scale);
        int w = codeW + 2 * pad, h = codeH + 2 * pad + (int)(26 * scale);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(Brushes.White, null, new Rect(0, 0, w, h), 6 * scale, 6 * scale);
            dc.DrawImage(Code(text, format, codeW, codeH), new Rect(pad, pad, codeW, codeH));
            var caption = format switch
            {
                DemoFormat.Qr => "Lieferung 4711",
                DemoFormat.Ean13 => "Artikel",
                _ => "Typenschild",
            };
            Text(dc, caption, pad, pad + codeH + 4 * scale, 14 * scale, FontWeights.SemiBold, Gray(70));
        }
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Kamerabild: Tisch (Holzton mit Verlauf und Vignette), darauf die Karte leicht versetzt.</summary>
    private static PixelImage Scene(int w, int h, BitmapSource? card, double dx, double dy)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var desk = new LinearGradientBrush(Color.FromRgb(0x8C, 0x6A, 0x4F), Color.FromRgb(0x5E, 0x46, 0x34), 20);
            dc.DrawRectangle(desk, null, new Rect(0, 0, w, h));
            var grain = new Pen(new SolidColorBrush(Color.FromArgb(0x18, 0, 0, 0)), 2);
            for (int i = 0; i < 14; i++) dc.DrawLine(grain, new Point(0, h * i / 14.0 + 7), new Point(w, h * i / 14.0 + 11));
            if (card is not null)
            {
                double x = Math.Round((w - card.PixelWidth) / 2.0 + dx), y = Math.Round((h - card.PixelHeight) / 2.0 + dy);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)), null, new Rect(x + 6, y + 9, card.PixelWidth, card.PixelHeight), 8, 8);
                dc.DrawImage(card, new Rect(x, y, card.PixelWidth, card.PixelHeight));
            }
            var vignette = new RadialGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0x70, 0, 0, 0)) { RadiusX = 0.75, RadiusY = 0.75 };
            dc.DrawRectangle(vignette, null, new Rect(0, 0, w, h));
        }
        return Render(visual, w, h);
    }

    private enum DemoFormat { Qr, DataMatrix, Ean13, Code128 }

    /// <summary>
    /// Ein echter Code von zxing-cpp, genau in der gewünschten Pixelgröße und scharf: ganzzahlig viele Pixel je Modul,
    /// mittig mit Ruhezone (QR und Data Matrix 1 Modul, Strichcodes 6), Striche über die ganze Höhe – so wie die
    /// Bilder, die Lupe bisher mit ZXing.Net erzeugte.
    /// </summary>
    private static BitmapSource Code(string text, DemoFormat format, int width, int height)
    {
        var (zx, options) = format switch
        {
            DemoFormat.Qr => (BarcodeFormat.QRCode, "ecLevel=L"),
            DemoFormat.DataMatrix => (BarcodeFormat.DataMatrix, "forceSquare"),
            DemoFormat.Ean13 => (BarcodeFormat.EAN13, ""),
            _ => (BarcodeFormat.Code128, ""),
        };
        bool linear = format is DemoFormat.Ean13 or DemoFormat.Code128;
        int margin = linear ? 6 : 1;
        using var creator = new CreatorOptions(zx, options);
        using var barcode = new Barcode(text, creator);
        using var writer = new WriterOptions { Scale = 1, AddQuietZones = false };
        using var symbol = barcode.ToImage(writer);
        var modules = symbol.ToArray(); // ein Pixel je Modul, 0 = dunkel
        int mw = symbol.Width, mh = linear ? 1 : symbol.Height;

        int unit = Math.Max(1, Math.Min(width / (mw + 2 * margin), linear ? int.MaxValue : height / (mh + 2 * margin)));
        int left = (width - mw * unit) / 2, top = linear ? 0 : (height - mh * unit) / 2, codeHeight = linear ? height : mh * unit;
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, (byte)255);
        for (int y = 0; y < codeHeight; y++)
        {
            int my = linear ? 0 : y / unit;
            for (int x = 0; x < mw * unit; x++)
            {
                if (modules[my * symbol.Width + x / unit] >= 128) continue;
                int px = left + x, py = top + y;
                if (px < 0 || py < 0 || px >= width || py >= height) continue;
                int i = (py * width + px) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = 0;
            }
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static readonly Typeface Face = new("Segoe UI");

    private static void Text(DrawingContext dc, string text, double x, double y, double size, FontWeight weight, Brush? brush = null)
    {
        var face = new Typeface(Face.FontFamily, FontStyles.Normal, weight, FontStretches.Normal);
        dc.DrawText(new FormattedText(text, Labels.German, FlowDirection.LeftToRight, face, size, brush ?? Gray(30), 1.0), new Point(x, y));
    }

    private static SolidColorBrush Gray(byte v)
    {
        var brush = new SolidColorBrush(Color.FromRgb(v, v, v));
        brush.Freeze();
        return brush;
    }

    private static PixelImage Render(Visual visual, int w, int h)
    {
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return Images.FromBitmap(bitmap);
    }
}
