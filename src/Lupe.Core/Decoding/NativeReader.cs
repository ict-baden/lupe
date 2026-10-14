using System.Text;
using ZXingCpp;
using Zx = ZXingCpp.BarcodeFormat;

namespace Lupe.Core.Decoding;

/// <summary>Ein Treffer von zxing-cpp, schon aus den nativen Objekten herausgelesen.</summary>
/// <param name="Format">Lupe-Format.</param>
/// <param name="Native">Format, wie zxing-cpp es meldet (z. B. EAN-13 für einen UPC-A).</param>
/// <param name="Text">Inhalt als Text (Rohform, noch ohne Lupes Anpassungen je Format).</param>
/// <param name="Bytes">Inhalt als Bytes, wie im Code gespeichert (ohne ECI-Kennungen).</param>
/// <param name="Upce">Bei UPC-E die achtstellige Originalform (zxing-cpp liefert als Text die 13-stellige GTIN).</param>
/// <param name="Corners">Ecken im gelesenen Bild: oben links, oben rechts, unten rechts, unten links – bezogen auf den
/// Code selbst; bei Strichcodes Anfang und Ende der ersten und letzten Lesezeile.</param>
/// <param name="Uec">Ungenutzte Fehlerkorrektur (0–1, ISO/IEC 15415) bei QR, Data Matrix, Aztec und PDF417, sonst null:
/// 1 = fehlerfrei gelesen, 0 = die ganze Fehlerkorrektur verbraucht – ein solches Ergebnis ist nur knapp gesichert.</param>
internal sealed record NativeCode(CodeFormat Format, Zx Native, string Text, byte[] Bytes, string? Upce, (double X, double Y)[] Corners,
    double? Uec = null);

/// <summary>Einstellungen für einen Lesevorgang von zxing-cpp.</summary>
/// <param name="Formats">Gesuchte Lupe-Formate.</param>
/// <param name="TryInvert">zxing-cpp probiert für QR, Data Matrix und Aztec auch die Umkehrung (hell auf dunkel).</param>
/// <param name="TryDownscale">zxing-cpp sucht zusätzlich in verkleinerten Fassungen (große Codes, Rauschen).</param>
internal sealed record ReadSettings(IReadOnlyList<CodeFormat> Formats, bool TryInvert = true, bool TryDownscale = true);

/// <summary>
/// Die Brücke zu zxing-cpp (NuGet-Paket ZXingCpp, native ZXing.dll): ein Graustufenbild lesen, alle nativen Objekte
/// sofort wieder freigeben und die Treffer als <see cref="NativeCode"/> zurückgeben.
/// <para>
/// Ein Aufruf lässt sich nicht unterbrechen – Abbruch geht nur zwischen zwei Aufrufen; deshalb teilt
/// <see cref="BarcodeDecoder"/> große Bilder in Arbeitspakete. Threadsicher: jeder Aufruf hat eigene Leseoptionen,
/// zxing-cpp selbst hat keinen gemeinsamen Zustand.
/// </para>
/// </summary>
internal static class NativeReader
{
    /// <summary>
    /// Liest alle Codes der gewünschten Formate. Treffer anderer Formate (zxing-cpp sucht mit „PDF417“ z. B. auch
    /// MicroPDF417) und Treffer ohne Inhalt fallen weg.
    /// </summary>
    public static unsafe List<NativeCode> Read(GrayImage image, ReadSettings settings)
    {
        var result = new List<NativeCode>();
        if (settings.Formats.Count == 0) return result;
        var formats = settings.Formats.SelectMany(f => f.Read).Distinct().ToArray();
        using var options = new ReaderOptions
        {
            Formats = new BarcodeFormats(formats),
            TryHarder = true,
            TryRotate = true,
            TryInvert = settings.TryInvert,
            TryDownscale = settings.TryDownscale,
            // Plain: Bytes nach ECI-Angabe bzw. erkanntem Zeichensatz als Text, GS1-Trennzeichen (0x1D) bleiben im Text.
            // Die Voreinstellung HRI setzte GS1-Inhalte in Klammern und Binärinhalte in <…>-Schreibweise.
            TextMode = TextMode.Plain,
            EanAddOnSymbol = EanAddOnSymbol.Ignore,
        };

        Barcode[] barcodes;
        // Angeheftet, solange zxing-cpp liest: Die ImageView merkt sich nur den Zeiger
        fixed (byte* pixels = image.Pixels)
        {
            var view = new ImageView((IntPtr)pixels, image.Width, image.Height, ImageFormat.Lum);
            barcodes = BarcodeReader.Read(view, options);
            GC.KeepAlive(view);
        }

        try
        {
            foreach (var barcode in barcodes)
            {
                if (!barcode.IsValid) continue;
                var native = barcode.Format;
                var format = FormatCatalog.FromResult(native);
                if (format is null || !settings.Formats.Contains(format)) continue;
                var bytes = barcode.Bytes;
                var text = TextOf(barcode, bytes);
                if (text.Length == 0) continue;
                var p = barcode.Position;
                result.Add(new NativeCode(format, native, text, bytes,
                    format.Name == "UPC_E" ? NullIfEmpty(barcode.Extra("UPCE")) : null,
                    [(p.TopLeft.X, p.TopLeft.Y), (p.TopRight.X, p.TopRight.Y), (p.BottomRight.X, p.BottomRight.Y), (p.BottomLeft.X, p.BottomLeft.Y)],
                    format.Linear ? null : Uec(barcode.Extra("UEC"))));
            }
        }
        finally
        {
            foreach (var barcode in barcodes) barcode.Dispose();
        }
        return result;
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    /// <summary>„0.85“ → 0,85; leer oder negativ (zxing-cpp: unbekannt) → null.</summary>
    internal static double? Uec(string value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var uec) && uec >= 0
            ? uec
            : null;

    /// <summary>
    /// Der Text von zxing-cpp – außer der Inhalt enthält ein Nullbyte: Die Brücke reicht den Text als C-Zeichenkette
    /// herüber, die am ersten Nullbyte endete („A\0B“ käme als „A“ an). Dann wird hier selbst dekodiert, wie zxing-cpp
    /// es ohne ECI täte (Nullbytes schließen Shift_JIS aus): UTF-8, wenn es gültiges UTF-8 mit Umlauten o. Ä. ist, sonst
    /// ISO-8859-1 – so wie ZXing.Net Binärinhalte lieferte.
    /// </summary>
    internal static string TextOf(Barcode barcode, byte[] bytes) =>
        Array.IndexOf(bytes, (byte)0) < 0 ? barcode.Text : DecodeWithNul(bytes);

    internal static string DecodeWithNul(byte[] bytes)
    {
        if (bytes.Any(b => b >= 0x80))
        {
            try
            {
                return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                // kein gültiges UTF-8 – dann ISO-8859-1
            }
        }
        return Encoding.Latin1.GetString(bytes);
    }
}
