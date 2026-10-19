// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Reflection;
using Lupe.Core.Model;
using Lupe.Core.Resources;

namespace Lupe;

public static class AppInfo
{
    public const string RepositoryUrl = "https://github.com/ict-baden/lupe";
    public const string Publisher = "ICT-Baden";
    // Info-Seite (Richtlinien 07 und 10). Impressum und Webseite fehlen noch: die Domain ist offen (richtlinien/OFFEN.md).
    public const string LicenseUrl = RepositoryUrl + "/blob/main/LICENSE";
    public const string PrivacyUrl = RepositoryUrl + "/blob/main/docs/data-and-privacy.md";
    public const string SupportUrl = RepositoryUrl + "/blob/main/SUPPORT.md";

    private static readonly string Informational =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    /// <summary>Version ohne Build-Anhang, z. B. „0.1.0“.</summary>
    public static string Version { get; } = Informational.Split('+')[0];

    /// <summary>Commit-Kürzel des Builds (vom SDK als „+&lt;sha&gt;“ an die Version gehängt), leer ohne Git.</summary>
    public static string Build { get; } = BuildOf(Informational);

    internal static string BuildOf(string informationalVersion)
    {
        int plus = informationalVersion.IndexOf('+');
        if (plus < 0) return "";
        var sha = informationalVersion[(plus + 1)..];
        return sha.Length > 7 ? sha[..7] : sha;
    }
}

/// <summary>Bezeichnungen für Formate, Quellen, Aktionen und Zeiten in der Sprache der Oberfläche.</summary>
public static class Labels
{
    /// <summary>Nur noch für Aufrufer, die deutsche Zahlen brauchen; Anzeigen formatieren mit <see cref="CultureInfo.CurrentCulture"/>.</summary>
    public static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>
    /// Anzeigenamen aller Formate der Erkennung (<c>BarcodeDecoder.AllFormats</c>) und früherer Formate, die noch im
    /// Verlauf stehen können (IMB las nur ZXing.Net; MSI, Plessey, Pharmacode und UPC/EAN-Zusatz las Lupe nie, sie
    /// konnten aber von Hand in den Einstellungen stehen). Namen, die je Sprache anders lauten, liefert
    /// <see cref="LocalizedFormat"/> – erst beim Aufruf, wenn die Sprache feststeht.
    /// </summary>
    private static readonly Dictionary<string, string> FormatNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DATA_MATRIX"] = "Data Matrix",
        ["AZTEC"] = "Aztec",
        ["PDF_417"] = "PDF417",
        ["MAXICODE"] = "MaxiCode",
        ["MICRO_PDF_417"] = "MicroPDF417",
        ["EAN_13"] = "EAN-13",
        ["EAN_8"] = "EAN-8",
        ["UPC_A"] = "UPC-A",
        ["UPC_E"] = "UPC-E",
        ["CODE_128"] = "Code 128",
        ["CODE_39"] = "Code 39",
        ["CODE_93"] = "Code 93",
        ["CODABAR"] = "Codabar",
        ["ITF"] = "ITF",
        ["RSS_14"] = "GS1 DataBar",
        ["RSS_EXPANDED"] = "GS1 DataBar Expanded",
        ["RSS_LIMITED"] = "GS1 DataBar Limited",
        ["TELEPEN"] = "Telepen",
        ["MSI"] = "MSI",
        ["PLESSEY"] = "Plessey",
        ["IMB"] = "Intelligent Mail",
        ["PHARMA_CODE"] = "Pharmacode",
    };

    /// <summary>„QR_CODE“ → „QR-Code“; Unbekanntes mit Leerzeichen statt Unterstrichen.</summary>
    public static string Format(string zxingName) =>
        LocalizedFormat(zxingName) ?? (FormatNames.TryGetValue(zxingName, out var name) ? name : zxingName.Replace('_', ' '));

    /// <summary>Formatnamen, die übersetzt werden; null für die übrigen.</summary>
    private static string? LocalizedFormat(string zxingName) => zxingName.ToUpperInvariant() switch
    {
        "QR_CODE" => Strings.App_Format_QrCode,
        "MICRO_QR_CODE" => Strings.App_Format_MicroQrCode,
        "RMQR_CODE" => Strings.App_Format_RmqrCode,
        "UPC_EAN_EXTENSION" => Strings.App_Format_UpcEanExtension,
        "DX_FILM_EDGE" => Strings.App_Format_DxFilmEdge,
        _ => null,
    };

    /// <summary>Zweidimensionale Formate (für die Markierung: Fläche statt Linie).</summary>
    public static bool Is2D(string zxingName) => zxingName.ToUpperInvariant() is "QR_CODE" or "DATA_MATRIX" or "AZTEC" or "PDF_417"
        or "MAXICODE" or "MICRO_QR_CODE" or "RMQR_CODE" or "MICRO_PDF_417";

    public static string Source(ScanSource source) => source switch
    {
        ScanSource.Kamera => Strings.App_Nav_Camera,
        ScanSource.Bildschirm => Strings.App_Nav_Screen,
        ScanSource.Datei => Strings.App_Nav_File,
        ScanSource.Zwischenablage => Strings.App_Source_Clipboard,
        ScanSource.Befehlszeile => Strings.App_Source_CommandLine,
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
        if (done.HasFlag(ResultActions.Kopieren)) parts.Add(Strings.App_Action_Copied);
        if (done.HasFlag(ResultActions.Eintippen)) parts.Add(Strings.App_Action_Typed);
        if (done.HasFlag(ResultActions.LinkOeffnen)) parts.Add(Strings.App_Action_LinkOpened);
        return string.Join(" · ", parts);
    }

    /// <summary>Datum und Uhrzeit; das Muster steht je Sprache in den Ressourcen (deutsch „dd.MM.yyyy HH:mm:ss“).</summary>
    public static string Time(DateTimeOffset time) => time.ToLocalTime().ToString(Strings.App_Time_DateTime, CultureInfo.CurrentCulture);

    /// <summary>Uhrzeit für heute, sonst Datum und Uhrzeit.</summary>
    public static string ShortTime(DateTimeOffset time, DateTimeOffset now)
    {
        var local = time.ToLocalTime();
        return local.ToString(local.Date == now.ToLocalTime().Date ? Strings.App_Time_Time : Strings.App_Time_DateShortTime, CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Veraltet: setzt die Anzahl vor ein Wort („5 Codes“) – nur noch für Aufrufer, die noch nicht auf Formattexte
    /// (…_One/…_Other in Strings.resx) umgestellt sind.
    /// </summary>
    public static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n.ToString("N0", CultureInfo.CurrentCulture)} {many}";

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
