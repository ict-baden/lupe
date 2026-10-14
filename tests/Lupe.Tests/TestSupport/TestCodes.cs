using System.Text;
using Lupe.Core.Model;
using Zx = ZXingCpp;

namespace Lupe.Tests.TestSupport;

/// <summary>
/// Die Formate der Erkennung in ihrer ZXing-Schreibweise – dieselben Namen wie <c>BarcodeDecoder.AllFormats</c>, damit
/// <c>format.ToString()</c> mit <c>DecodedCode.Format</c> verglichen werden kann.
/// </summary>
public enum BarcodeFormat
{
    QR_CODE, DATA_MATRIX, AZTEC, PDF_417, MAXICODE, MICRO_QR_CODE, RMQR_CODE, MICRO_PDF_417,
    CODE_128, CODE_39, CODE_93, CODABAR, ITF, EAN_13, EAN_8, UPC_A, UPC_E, RSS_14, RSS_EXPANDED, RSS_LIMITED, TELEPEN, DX_FILM_EDGE,
}

/// <summary>
/// Erzeugt echte Codes als <see cref="PixelImage"/> (mit dem Schreiber von zxing-cpp), optional verkleinert, gedreht,
/// invertiert, verrauscht oder irgendwo in ein größeres Bild gesetzt. Gemeinsame Grundlage für Erkennung,
/// Kamera-Attrappe, Bildschirm- und App-Tests.
/// </summary>
public static class TestCodes
{
    /// <summary>Strichcodes (eine Leselinie); alles andere ist zweidimensional.</summary>
    public static bool IsLinear(BarcodeFormat format) => format >= BarcodeFormat.CODE_128;

    /// <summary>Das zxing-cpp-Format zum Schreiben.</summary>
    public static Zx.BarcodeFormat ZxFormat(BarcodeFormat format) => format switch
    {
        BarcodeFormat.QR_CODE => Zx.BarcodeFormat.QRCode,
        BarcodeFormat.DATA_MATRIX => Zx.BarcodeFormat.DataMatrix,
        BarcodeFormat.AZTEC => Zx.BarcodeFormat.Aztec,
        BarcodeFormat.PDF_417 => Zx.BarcodeFormat.PDF417,
        BarcodeFormat.MAXICODE => Zx.BarcodeFormat.MaxiCode,
        BarcodeFormat.MICRO_QR_CODE => Zx.BarcodeFormat.MicroQRCode,
        BarcodeFormat.RMQR_CODE => Zx.BarcodeFormat.RMQRCode,
        BarcodeFormat.MICRO_PDF_417 => Zx.BarcodeFormat.MicroPDF417,
        BarcodeFormat.CODE_128 => Zx.BarcodeFormat.Code128,
        BarcodeFormat.CODE_39 => Zx.BarcodeFormat.Code39Std,
        BarcodeFormat.CODE_93 => Zx.BarcodeFormat.Code93,
        BarcodeFormat.CODABAR => Zx.BarcodeFormat.Codabar,
        BarcodeFormat.ITF => Zx.BarcodeFormat.ITF,
        BarcodeFormat.EAN_13 => Zx.BarcodeFormat.EAN13,
        BarcodeFormat.EAN_8 => Zx.BarcodeFormat.EAN8,
        BarcodeFormat.UPC_A => Zx.BarcodeFormat.UPCA,
        BarcodeFormat.UPC_E => Zx.BarcodeFormat.UPCE,
        BarcodeFormat.RSS_14 => Zx.BarcodeFormat.DataBar,
        BarcodeFormat.RSS_EXPANDED => Zx.BarcodeFormat.DataBarExp,
        BarcodeFormat.RSS_LIMITED => Zx.BarcodeFormat.DataBarLtd,
        BarcodeFormat.TELEPEN => Zx.BarcodeFormat.Telepen,
        BarcodeFormat.DX_FILM_EDGE => Zx.BarcodeFormat.DXFilmEdge,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>
    /// Ein Code als Bild, Hintergrund weiß, Ruhezone inklusive – aufgebaut wie früher mit dem Schreiber von ZXing.Net:
    /// ganzzahlig viele Pixel je Modul, mittig; 2D-Codes mit <paramref name="margin"/> Modulen Ruhezone je Seite,
    /// Strichcodes mit <paramref name="margin"/> Modulen Ruhezone insgesamt und Strichen über die ganze Höhe. Ist das Bild
    /// kleiner als der Code, wird es so groß wie nötig (1 Pixel je Modul).
    /// </summary>
    /// <param name="options">Optionen für zxing-cpps Schreiber, z. B. „eci=UTF-8“ oder „gs1“.</param>
    public static PixelImage Render(string text, BarcodeFormat format = BarcodeFormat.QR_CODE, int width = 300, int height = 300, int margin = 4,
        string? options = null)
    {
        var (modules, mw, mh) = Modules(text, format, options);
        return Layout(modules, mw, mh, IsLinear(format), width, height, margin);
    }

    /// <summary>
    /// Die Module des Codes (true = dunkel), ein Eintrag je Modul. Wie früher bei ZXing.Net: QR mit Fehlerkorrektur L,
    /// Data Matrix quadratisch, Text ohne ECI-Kennung, solange er in ISO-8859-1 passt (sonst UTF-8 mit ECI).
    /// </summary>
    public static (bool[] Modules, int Width, int Height) Modules(string text, BarcodeFormat format, string? options = null)
    {
        var defaults = format switch
        {
            BarcodeFormat.QR_CODE => "ecLevel=L",
            BarcodeFormat.DATA_MATRIX => "forceSquare",
            _ => null,
        };
        if (text.All(c => c < 0x80))
        {
            using var creator = new Zx.CreatorOptions(ZxFormat(format), Join(defaults, options));
            using var barcode = new Zx.Barcode(text, creator);
            return ToModules(barcode);
        }
        // Die .NET-Brücke übergibt Texte im ANSI-Zeichensatz statt UTF-8 („Invalid UTF-8“) – andere Texte deshalb als Bytes
        bool latin1 = text.All(c => c <= 0xFF);
        var data = latin1 ? Encoding.Latin1.GetBytes(text) : Encoding.UTF8.GetBytes(text);
        using var bytesCreator = new Zx.CreatorOptions(ZxFormat(format), Join(defaults, latin1 ? "eci=0" : "eci=UTF-8", options));
        using var bytesBarcode = new Zx.Barcode(data, bytesCreator);
        return ToModules(bytesBarcode);

        static string? Join(params string?[] parts)
        {
            var all = string.Join(",", parts.Where(p => !string.IsNullOrEmpty(p)));
            return all.Length == 0 ? null : all;
        }
    }

    /// <summary>Bytes als Code (z. B. Binärinhalt oder ein Zeichensatz ohne ECI-Kennung), sonst wie <see cref="Render"/>.</summary>
    public static PixelImage RenderBytes(byte[] data, BarcodeFormat format, string? options, int width = 300, int height = 300, int margin = 4)
    {
        using var creator = new Zx.CreatorOptions(ZxFormat(format), options);
        using var barcode = new Zx.Barcode(data, creator);
        var (modules, mw, mh) = ToModules(barcode);
        return Layout(modules, mw, mh, IsLinear(format), width, height, margin);
    }

    private static (bool[] Modules, int Width, int Height) ToModules(Zx.Barcode barcode)
    {
        using var writer = new Zx.WriterOptions { Scale = 1, AddQuietZones = false };
        using var image = barcode.ToImage(writer);
        var lum = image.ToArray();
        return (lum.Select(v => v < 128).ToArray(), image.Width, image.Height);
    }

    private static PixelImage Layout(bool[] modules, int mw, int mh, bool linear, int width, int height, int margin)
    {
        int fullW = linear ? mw + margin : mw + 2 * margin, fullH = linear ? mh : mh + 2 * margin;
        int outW = Math.Max(width, fullW), outH = Math.Max(height, linear ? 1 : fullH);
        int unit = Math.Max(1, linear ? outW / fullW : Math.Min(outW / fullW, outH / fullH));
        int left = (outW - mw * unit) / 2, top = linear ? 0 : (outH - mh * unit) / 2;
        int codeH = linear ? outH : mh * unit;
        var px = new byte[outW * outH * 4];
        Array.Fill(px, (byte)255);
        for (int y = 0; y < codeH; y++)
        {
            int my = linear ? Math.Min(mh - 1, (int)((long)y * mh / codeH)) : y / unit;
            for (int x = 0; x < mw * unit; x++)
            {
                if (!modules[my * mw + x / unit]) continue;
                int i = ((top + y) * outW + left + x) * 4;
                px[i] = px[i + 1] = px[i + 2] = 0;
            }
        }
        return new PixelImage(outW, outH, px);
    }

    /// <summary>Setzt ein Bild in eine größere, graue Fläche (z. B. Bildschirm- oder Kamerabild mit Code irgendwo).</summary>
    public static PixelImage Embed(PixelImage code, int canvasWidth, int canvasHeight, int x, int y, byte background = 200)
    {
        var px = new byte[canvasWidth * canvasHeight * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = px[i + 1] = px[i + 2] = background; px[i + 3] = 255; }
        for (int row = 0; row < code.Height; row++)
        {
            int ty = y + row;
            if (ty < 0 || ty >= canvasHeight) continue;
            for (int col = 0; col < code.Width; col++)
            {
                int tx = x + col;
                if (tx < 0 || tx >= canvasWidth) continue;
                Buffer.BlockCopy(code.Bgra, row * code.Stride + col * 4, px, (ty * canvasWidth + tx) * 4, 4);
            }
        }
        return new PixelImage(canvasWidth, canvasHeight, px);
    }

    /// <summary>Hell und dunkel vertauschen (heller Code auf dunklem Grund, wie auf manchen Displays).</summary>
    public static PixelImage Invert(PixelImage img)
    {
        var px = (byte[])img.Bgra.Clone();
        for (int i = 0; i < px.Length; i += 4) { px[i] = (byte)(255 - px[i]); px[i + 1] = (byte)(255 - px[i + 1]); px[i + 2] = (byte)(255 - px[i + 2]); }
        return new PixelImage(img.Width, img.Height, px, img.Stride);
    }

    /// <summary>Um 90°-Schritte drehen.</summary>
    public static PixelImage Rotate90(PixelImage img, int times = 1)
    {
        var cur = img;
        for (int t = 0; t < ((times % 4) + 4) % 4; t++)
        {
            int w = cur.Width, h = cur.Height;
            var px = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    Buffer.BlockCopy(cur.Bgra, y * cur.Stride + x * 4, px, (x * h + (h - 1 - y)) * 4, 4);
            cur = new PixelImage(h, w, px);
        }
        return cur;
    }

    /// <summary>Gleichmäßiges Rauschen ±amount auf jeden Kanal, reproduzierbar über den Seed.</summary>
    public static PixelImage Noise(PixelImage img, int amount, int seed = 1)
    {
        var rnd = new Random(seed);
        var px = (byte[])img.Bgra.Clone();
        for (int i = 0; i < px.Length; i += 4)
        {
            int d = rnd.Next(-amount, amount + 1);
            for (int c = 0; c < 3; c++) px[i + c] = (byte)Math.Clamp(px[i + c] + d, 0, 255);
        }
        return new PixelImage(img.Width, img.Height, px, img.Stride);
    }
}
