// SPDX-License-Identifier: MIT
using Zx = ZXingCpp.BarcodeFormat;

namespace Lupe.Core.Decoding;

/// <summary>Ein Format, wie Lupe es nennt, und womit zxing-cpp danach sucht.</summary>
/// <param name="Name">Name in ZXing-Schreibweise (QR_CODE …) – steht in Einstellungen, Verlauf, CSV-Export und Befehlszeile.</param>
/// <param name="Linear">Strichcode (eine Leselinie) statt 2D-Code (Fläche).</param>
/// <param name="Read">Die zxing-cpp-Formate, mit denen danach gesucht wird.</param>
internal sealed record CodeFormat(string Name, bool Linear, params Zx[] Read)
{
    /// <summary>
    /// Strichcodes, die zxing-cpp selbst nicht invertiert liest (helle Striche auf dunklem Grund), dazu PDF417 und MaxiCode –
    /// zxing-cpp probiert die Umkehrung nur für QR, Data Matrix und Aztec (Familien wie Micro QR eingeschlossen).
    /// </summary>
    public bool NeedsInvertedPass => Linear || Name is "PDF_417" or "MICRO_PDF_417" or "MAXICODE";

    /// <summary>
    /// Lesegruppe: Formate einer Gruppe liest ein gemeinsamer Lesevorgang, die Gruppen laufen parallel. So aufgeteilt,
    /// dass kein Lesevorgang lange dauert – auch ein Abbruch wartet höchstens auf den längsten (verrauschtes 4K-Bild:
    /// alle 2D-Formate am Stück 680 ms, Data Matrix allein 330 ms). 0 = QR-Familie, 1 = Data Matrix, 2 = Aztec,
    /// 3 = PDF417-Familie und MaxiCode (nicht drehungsfest, invertiert nur über die invertierte Fassung), 4 = Strichcodes.
    /// </summary>
    public int Group => Linear ? 4 : Name switch
    {
        "QR_CODE" or "MICRO_QR_CODE" or "RMQR_CODE" => 0,
        "DATA_MATRIX" => 1,
        "AZTEC" => 2,
        _ => 3,
    };
}

/// <summary>
/// Die eine Zuordnung zwischen Lupes Formatnamen und zxing-cpp. Die Namen sind seit ZXing.Net dieselben, weil sie in den
/// Einstellungen, im Verlauf, im CSV-Export und in der Ausgabe der Befehlszeile stehen; neue Formate von zxing-cpp
/// heißen im selben Stil.
/// <list type="bullet">
/// <item>QR_CODE: QR Code Modell 1 und 2. Micro QR und rMQR sind eigene Formate (MICRO_QR_CODE, RMQR_CODE).</item>
/// <item>AZTEC: Aztec-Codes; Aztec-Runen (nur eine Zahl 0–255, kein Inhalt im eigentlichen Sinn) werden nicht gelesen.</item>
/// <item>PDF_417: auch Compact PDF417; MicroPDF417 ist ein eigenes Format (MICRO_PDF_417).</item>
/// <item>CODE_39: nur Standard-Code-39 – wie bei ZXing.Net und wie Handscanner ab Werk. „Full ASCII“ (Code 39 Extended)
/// deutete einen gewöhnlichen Code mit „/“, „+“, „$“ oder „%“ vor einem Buchstaben stillschweigend um („S/N1“ würde
/// „S.1“), Code 32 (italienische Pharmazentralnummer) wandelte manche sechsstelligen Codes in eine andere Zahl um.</item>
/// <item>ITF: auch ITF-14. EAN_13: auch ISBN (Bookland, 978/979). EAN/UPC-Zusatzcodes (2 oder 5 Ziffern) werden übergangen – der
/// Inhalt bleibt wie bisher die Hauptnummer.</item>
/// <item>RSS_14: GS1 DataBar (Omnidirectional, Truncated, Stacked, Stacked Omnidirectional); RSS_LIMITED: DataBar Limited;
/// RSS_EXPANDED: DataBar Expanded, einzeilig und gestapelt.</item>
/// <item>Neu mit zxing-cpp: MICRO_QR_CODE, RMQR_CODE, MICRO_PDF_417, RSS_LIMITED, TELEPEN, DX_FILM_EDGE.</item>
/// <item>Nicht mehr dabei: IMB (Intelligent Mail) – zxing-cpp kann es nicht lesen. Ältere Einstellungen und
/// Verlaufseinträge mit diesem Namen bleiben lesbar; in der Formatliste wird er übergangen.</item>
/// </list>
/// </summary>
internal static class FormatCatalog
{
    /// <summary>Alle Formate – 2D zuerst, dann Strichcodes (gängige vor seltenen), die mit zxing-cpp neuen jeweils dahinter.</summary>
    public static readonly CodeFormat[] All =
    [
        new("QR_CODE", false, Zx.QRCodeModel1, Zx.QRCodeModel2),
        new("DATA_MATRIX", false, Zx.DataMatrix),
        new("AZTEC", false, Zx.AztecCode),
        new("PDF_417", false, Zx.PDF417, Zx.CompactPDF417),
        new("MAXICODE", false, Zx.MaxiCode),
        new("MICRO_QR_CODE", false, Zx.MicroQRCode),
        new("RMQR_CODE", false, Zx.RMQRCode),
        new("MICRO_PDF_417", false, Zx.MicroPDF417),
        new("CODE_128", true, Zx.Code128),
        new("CODE_39", true, Zx.Code39Std),
        new("CODE_93", true, Zx.Code93),
        new("CODABAR", true, Zx.Codabar),
        // ITF-14 mit dazu: Ist nur ein einziges zxing-cpp-Format verlangt, nimmt der ITF-Leser schon vier Ziffern an
        new("ITF", true, Zx.ITF, Zx.ITF14),
        new("EAN_13", true, Zx.EAN13),
        new("EAN_8", true, Zx.EAN8),
        new("UPC_A", true, Zx.UPCA),
        new("UPC_E", true, Zx.UPCE),
        new("RSS_14", true, Zx.DataBarOmni, Zx.DataBarStk, Zx.DataBarStkOmni),
        new("RSS_EXPANDED", true, Zx.DataBarExp, Zx.DataBarExpStk),
        new("RSS_LIMITED", true, Zx.DataBarLtd),
        new("TELEPEN", true, Zx.Telepen),
        new("DX_FILM_EDGE", true, Zx.DXFilmEdge),
    ];

    public static readonly IReadOnlyList<string> AllNames = Array.AsReadOnly(All.Select(f => f.Name).ToArray());

    private static readonly Dictionary<string, CodeFormat> ByName = All.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Ergebnisformat von zxing-cpp → Lupe-Format (genaue Variante, sonst über die Familie).</summary>
    private static readonly Dictionary<int, CodeFormat> ByResult = BuildResultMap();

    private static Dictionary<int, CodeFormat> BuildResultMap()
    {
        var map = new Dictionary<int, CodeFormat>();
        foreach (var f in All)
            foreach (var z in f.Read)
                map[z] = f;
        // Was zxing-cpp beim Lesen als Familie meldet (ein QR-Code Modell 2 kommt als „QR Code“ zurück)
        map[Zx.QRCode] = ByName["QR_CODE"];
        map[Zx.Aztec] = ByName["AZTEC"];
        map[Zx.Code39] = ByName["CODE_39"];
        map[Zx.DataBar] = ByName["RSS_14"];
        map[Zx.EANUPC] = ByName["EAN_13"];
        map[Zx.Telepen] = ByName["TELEPEN"];
        map[Zx.TelepenAlpha] = ByName["TELEPEN"];
        map[Zx.TelepenNumeric] = ByName["TELEPEN"];
        return map;
    }

    /// <summary>Format zu einem Namen (Groß/Klein, Leerzeichen, „-“ und „_“ egal), sonst null.</summary>
    public static CodeFormat? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return ByName.GetValueOrDefault(name.Trim().Replace('-', '_').Replace(' ', '_'));
    }

    /// <summary>
    /// Formatnamen → Formate in der Reihenfolge von <see cref="All"/>. Unbekannte Namen werden übergangen; bleibt nichts
    /// Bekanntes übrig, gelten alle Formate (lieber zu viel als stillschweigend nichts erkennen).
    /// </summary>
    public static CodeFormat[] Parse(IReadOnlyList<string>? names)
    {
        if (names is null || names.Count == 0) return All;
        var wanted = new HashSet<CodeFormat>();
        foreach (var raw in names)
            if (Find(raw) is { } f) wanted.Add(f);
        return wanted.Count == 0 ? All : [.. All.Where(wanted.Contains)];
    }

    /// <summary>Lupe-Format eines Lese-Ergebnisses von zxing-cpp, null für Formate, die Lupe nicht führt.</summary>
    public static CodeFormat? FromResult(Zx format)
    {
        if (ByResult.TryGetValue(format, out var f)) return f;
        return ByResult.GetValueOrDefault(format.Symbology());
    }

    /// <summary>Zweidimensional (Fläche statt Leselinie)? Unbekannte Namen gelten als Strichcode.</summary>
    public static bool Is2D(string? name) => Find(name) is { Linear: false };
}
