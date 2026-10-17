namespace Lupe.Core.Model;

/// <summary>
/// Ein Bild als 32-Bit-BGRA-Pixel (wie es GDI, WPF und Windows.Media liefern). Bewusst ohne System.Drawing oder
/// WPF-Typen, damit Erkennung, Kamera und Bildschirm dieselbe Form austauschen.
/// </summary>
public sealed class PixelImage
{
    public PixelImage(int width, int height, byte[] bgra, int? stride = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Stride = stride ?? width * 4;
        if (Stride < width * 4) throw new ArgumentOutOfRangeException(nameof(stride), "Zeile kürzer als Breite × 4.");
        if (bgra.Length < (long)Stride * height) throw new ArgumentException("Puffer kleiner als Stride × Höhe.", nameof(bgra));
        Width = width;
        Height = height;
        Bgra = bgra;
    }

    public int Width { get; }
    public int Height { get; }
    /// <summary>Bytes je Zeile (≥ Width × 4).</summary>
    public int Stride { get; }
    public byte[] Bgra { get; }
}

/// <summary>Woher ein Ergebnis kommt.</summary>
public enum ScanSource
{
    Kamera,
    Bildschirm,
    Datei,
    Zwischenablage,
    Befehlszeile,
}

/// <summary>Ein erkannter Code.</summary>
public sealed record DecodedCode
{
    /// <summary>Inhalt als Text (Binärinhalte Byte für Byte als ISO-8859-1, wie schon mit ZXing.Net).</summary>
    public required string Text { get; init; }
    /// <summary>Format in ZXing-Schreibweise, z. B. QR_CODE, DATA_MATRIX, EAN_13, CODE_128 (siehe BarcodeDecoder.AllFormats).</summary>
    public required string Format { get; init; }
    /// <summary>Punkte im Bild (Pixel) für die Markierung: bei 2D-Codes die vier Ecken im Uhrzeigersinn ab oben links, bei Strichcodes die zwei Enden der Leselinie.</summary>
    public IReadOnlyList<(float X, float Y)> Points { get; init; } = [];
    /// <summary>Inhalt als Bytes, wie im Code gespeichert (ohne ECI-Kennungen); null, wenn der Code leer ist.</summary>
    public byte[]? RawBytes { get; init; }
}

/// <summary>Was mit einem erkannten Code passieren soll – aus den Einstellungen.</summary>
[Flags]
public enum ResultActions
{
    Keine = 0,
    /// <summary>Text in die Zwischenablage.</summary>
    Kopieren = 1,
    /// <summary>Text als Tastatureingabe in das zuvor aktive Fenster tippen.</summary>
    Eintippen = 2,
    /// <summary>Passt der Text zu einer Link-Regel, den Link im Standardbrowser öffnen.</summary>
    LinkOeffnen = 4,
}

/// <summary>
/// Link-Regel wie im Barcode-Reader: Passt der Code auf <see cref="Pattern"/> (Regex), wird
/// <see cref="Prefix"/> + Code als Link geöffnet (Präfix leer = der Code ist selbst der Link).
/// </summary>
public sealed record LinkRule(string Pattern, string Prefix = "", bool Enabled = true);

/// <summary>Ein Eintrag im Verlauf.</summary>
public sealed record HistoryEntry
{
    public required DateTimeOffset Time { get; init; }
    public required string Text { get; init; }
    public required string Format { get; init; }
    public required ScanSource Source { get; init; }
    /// <summary>Welche Aktionen tatsächlich ausgeführt wurden (z. B. Link geöffnet).</summary>
    public ResultActions ActionsDone { get; init; }
}

/// <summary>Tastenkürzel, z. B. Win+Umschalt+Q. <see cref="Key"/> ist ein Windows-Virtual-Key-Code.</summary>
public sealed record Hotkey(bool Ctrl, bool Alt, bool Shift, bool Win, int Key)
{
    /// <summary>
    /// Win+Umschalt+Q – gebaut wie Win+Umschalt+S (Bildschirmausschnitt von Windows). Bewusst nicht Strg+Alt+Q: Windows
    /// meldet AltGr als Strg+Alt, ein systemweit angemeldetes Strg+Alt+Q finge auf deutschen Tastaturen AltGr+Q = „@“ ab.
    /// </summary>
    public static Hotkey Default { get; } = new(Ctrl: false, Alt: false, Shift: true, Win: true, Key: 0x51); // Win+Umschalt+Q

    /// <summary>macOS: ⌘⌥Q (Win+Alt+Q). ⌘⇧Q (Win+Umschalt+Q) ist dort „Abmelden“.</summary>
    public static Hotkey MacDefault { get; } = new(Ctrl: false, Alt: true, Shift: false, Win: true, Key: 0x51);

    /// <summary>Standard für dieses System: <see cref="MacDefault"/> unter macOS, sonst <see cref="Default"/>.</summary>
    public static Hotkey PlatformDefault => OperatingSystem.IsMacOS() ? MacDefault : Default;
}

/// <summary>Einstellungen – als JSON unter %LocalAppData%\Lupe\einstellungen.json gespeichert.</summary>
public sealed record LupeSettings
{
    /// <summary>Aktionen für Ergebnisse aus Kamera, Datei und Zwischenablage.</summary>
    public ResultActions Actions { get; init; } = ResultActions.Kopieren;
    /// <summary>Aktionen für den Bildschirmausschnitt (Standard: kopieren; „Eintippen“ tippt in das Fenster, das vor dem Scan vorn war).</summary>
    public ResultActions ScreenActions { get; init; } = ResultActions.Kopieren;
    /// <summary>Nach dem Eintippen eine Taste senden: "" (nichts), "Enter" oder "Tab".</summary>
    public string TypeSuffix { get; init; } = "";
    /// <summary>
    /// Pause zwischen zwei getippten Zeichen in Millisekunden (0 = alles auf einmal). Notepad unter Windows 11 liest das
    /// Zeichen einer Unicode-Eingabe erst beim Verarbeiten und bekommt bei zu schnellen Zeichen nur noch das zuletzt
    /// gesendete („Wikipédia,eeeeee…“) – gemessen: bis 8 ms Pause fehlerhaft, ab etwa 16 ms sicher. Echte Handscanner
    /// haben dafür ebenfalls eine Zeichenverzögerung.
    /// </summary>
    public int TypeDelayMs { get; init; } = 20;
    public IReadOnlyList<LinkRule> LinkRules { get; init; } = [new(@"^https://\S+$")];
    /// <summary>Aktive Formate in ZXing-Schreibweise; leer = alle.</summary>
    public IReadOnlyList<string> Formats { get; init; } = [];
    public Hotkey ScreenHotkey { get; init; } = Hotkey.PlatformDefault;
    public bool Sound { get; init; } = true;
    /// <summary>Zielhilfe (Fadenkreuz) im Kamerabild.</summary>
    public bool AimHelp { get; init; } = true;
    /// <summary>So lange (ms) wird derselbe Code nach einem Treffer nicht erneut gemeldet.</summary>
    public int RepeatSuppressMs { get; init; } = 3000;
    /// <summary>Kamera-Kennung (Gerätename/ID), leer = erste.</summary>
    public string Camera { get; init; } = "";
    /// <summary>Verlauf speichern (aus = nur im Speicher, nichts auf der Platte).</summary>
    public bool KeepHistory { get; init; } = true;
    /// <summary>Höchstzahl der Verlaufseinträge (1 bis 10 000); ältere fallen weg.</summary>
    public int HistoryLimit { get; init; } = 1000;
    /// <summary>Beim Schließen des Fensters im Infobereich weiterlaufen – das Tastenkürzel bleibt dann aktiv.</summary>
    public bool KeepRunningInTray { get; init; } = true;
    /// <summary>Merker der Oberfläche: Der Hinweis „Lupe läuft im Infobereich weiter“ wurde schon einmal gezeigt.</summary>
    public bool TrayHintShown { get; init; }
}
