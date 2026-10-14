using System.Globalization;
using System.Reflection;
using Lupe.Core.Model;

namespace Lupe;

public static class AppInfo
{
    public const string RepositoryUrl = "https://github.com/ict-baden/lupe";
    /// <summary>Der Barcode-Reader, aus dem Lupe hervorgegangen ist (MIT-Lizenz).</summary>
    public const string OriginUrl = "https://github.com/pieewiee/Barcode-Reader";
    public const string Publisher = "ICT-Baden";

    /// <summary>Version ohne Build-Anhang, z. B. „0.1.0“.</summary>
    public static string Version { get; } =
        (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.1.0").Split('+')[0];
}

/// <summary>Deutsche Bezeichnungen für Formate, Quellen, Aktionen und Zeiten.</summary>
public static class Labels
{
    public static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>
    /// Anzeigenamen aller Formate der Erkennung (<c>BarcodeDecoder.AllFormats</c>) und früherer Formate, die noch im
    /// Verlauf stehen können (IMB las nur ZXing.Net; MSI, Plessey, Pharmacode und UPC/EAN-Zusatz las Lupe nie, sie
    /// konnten aber von Hand in den Einstellungen stehen).
    /// </summary>
    private static readonly Dictionary<string, string> FormatNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["QR_CODE"] = "QR-Code",
        ["DATA_MATRIX"] = "Data Matrix",
        ["AZTEC"] = "Aztec",
        ["PDF_417"] = "PDF417",
        ["MAXICODE"] = "MaxiCode",
        ["MICRO_QR_CODE"] = "Micro-QR-Code",
        ["RMQR_CODE"] = "rMQR-Code",
        ["MICRO_PDF_417"] = "MicroPDF417",
        ["EAN_13"] = "EAN-13",
        ["EAN_8"] = "EAN-8",
        ["UPC_A"] = "UPC-A",
        ["UPC_E"] = "UPC-E",
        ["UPC_EAN_EXTENSION"] = "UPC/EAN-Zusatz",
        ["CODE_128"] = "Code 128",
        ["CODE_39"] = "Code 39",
        ["CODE_93"] = "Code 93",
        ["CODABAR"] = "Codabar",
        ["ITF"] = "ITF",
        ["RSS_14"] = "GS1 DataBar",
        ["RSS_EXPANDED"] = "GS1 DataBar Expanded",
        ["RSS_LIMITED"] = "GS1 DataBar Limited",
        ["TELEPEN"] = "Telepen",
        ["DX_FILM_EDGE"] = "DX-Filmrand",
        ["MSI"] = "MSI",
        ["PLESSEY"] = "Plessey",
        ["IMB"] = "Intelligent Mail",
        ["PHARMA_CODE"] = "Pharmacode",
    };

    /// <summary>„QR_CODE“ → „QR-Code“; Unbekanntes mit Leerzeichen statt Unterstrichen.</summary>
    public static string Format(string zxingName) =>
        FormatNames.TryGetValue(zxingName, out var name) ? name : zxingName.Replace('_', ' ');

    /// <summary>Zweidimensionale Formate (für die Markierung: Fläche statt Linie).</summary>
    public static bool Is2D(string zxingName) => zxingName.ToUpperInvariant() is "QR_CODE" or "DATA_MATRIX" or "AZTEC" or "PDF_417"
        or "MAXICODE" or "MICRO_QR_CODE" or "RMQR_CODE" or "MICRO_PDF_417";

    public static string Source(ScanSource source) => source switch
    {
        ScanSource.Kamera => "Kamera",
        ScanSource.Bildschirm => "Bildschirm",
        ScanSource.Datei => "Datei",
        ScanSource.Zwischenablage => "Zwischenablage",
        ScanSource.Befehlszeile => "Befehlszeile",
        _ => source.ToString(),
    };

    public static string SourceGlyph(ScanSource source) => source switch
    {
        ScanSource.Kamera => "\uE722",
        ScanSource.Bildschirm => "\uE7F4",
        ScanSource.Datei => "\uE8B9",
        ScanSource.Zwischenablage => "\uE77F",
        _ => "\uE756",
    };

    /// <summary>„Kopiert · Eingetippt · Link geöffnet“, leer bei keiner Aktion.</summary>
    public static string Actions(ResultActions done)
    {
        var parts = new List<string>(3);
        if (done.HasFlag(ResultActions.Kopieren)) parts.Add("Kopiert");
        if (done.HasFlag(ResultActions.Eintippen)) parts.Add("Eingetippt");
        if (done.HasFlag(ResultActions.LinkOeffnen)) parts.Add("Link geöffnet");
        return string.Join(" · ", parts);
    }

    public static string Time(DateTimeOffset time) => time.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", German);

    /// <summary>Uhrzeit für heute, sonst Datum und Uhrzeit.</summary>
    public static string ShortTime(DateTimeOffset time, DateTimeOffset now)
    {
        var local = time.ToLocalTime();
        return local.Date == now.ToLocalTime().Date ? local.ToString("HH:mm:ss", German) : local.ToString("dd.MM. HH:mm", German);
    }

    public static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n.ToString("N0", German)} {many}";

    /// <summary>Inhalt einzeilig für Listen: Zeilenumbrüche als ⏎, Steuerzeichen als ·, gekürzt.</summary>
    public static string OneLine(string text, int max = 400)
    {
        var chars = new char[Math.Min(text.Length, max)];
        int n = 0;
        foreach (var c in text)
        {
            if (n >= chars.Length) break;
            if (c == '\r') continue;
            chars[n++] = c switch
            {
                '\n' => '⏎',
                '\t' => ' ',
                < ' ' => '·',
                _ => c,
            };
        }
        var line = new string(chars, 0, n);
        return text.Length > max ? line + " …" : line;
    }
}
