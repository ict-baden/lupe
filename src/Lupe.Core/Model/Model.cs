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
    /// <summary>Inhalt als Text (bei Binärinhalten so, wie ZXing ihn liefert).</summary>
    public required string Text { get; init; }
    /// <summary>Format in ZXing-Schreibweise, z. B. QR_CODE, DATA_MATRIX, EAN_13, CODE_128.</summary>
    public required string Format { get; init; }
    /// <summary>Eckpunkte im Bild (Pixel), 2 Punkte bei 1D-Codes, meist 3–4 bei 2D-Codes. Für die Markierung.</summary>
    public IReadOnlyList<(float X, float Y)> Points { get; init; } = [];
    /// <summary>Rohbytes, falls das Format welche liefert (z. B. QR im Byte-Modus).</summary>
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

/// <summary>Tastenkürzel, z. B. Strg+Alt+Q. <see cref="Key"/> ist ein Windows-Virtual-Key-Code.</summary>
public sealed record Hotkey(bool Ctrl, bool Alt, bool Shift, bool Win, int Key)
{
    public static Hotkey Default { get; } = new(Ctrl: true, Alt: true, Shift: false, Win: false, Key: 0x51); // Strg+Alt+Q
}

/// <summary>Einstellungen – als JSON unter %LocalAppData%\Lupe\einstellungen.json gespeichert.</summary>
public sealed record LupeSettings
{
    /// <summary>Aktionen für Ergebnisse aus Kamera, Datei und Zwischenablage.</summary>
    public ResultActions Actions { get; init; } = ResultActions.Kopieren;
    /// <summary>Aktionen für den Bildschirmausschnitt (Standard: tippen, dort wo man vorher war).</summary>
    public ResultActions ScreenActions { get; init; } = ResultActions.Kopieren;
    /// <summary>Nach dem Eintippen eine Taste senden: "" (nichts), "Enter" oder "Tab".</summary>
    public string TypeSuffix { get; init; } = "";
    public IReadOnlyList<LinkRule> LinkRules { get; init; } = [new(@"^https://\S+$")];
    /// <summary>Aktive Formate in ZXing-Schreibweise; leer = alle.</summary>
    public IReadOnlyList<string> Formats { get; init; } = [];
    public Hotkey ScreenHotkey { get; init; } = Hotkey.Default;
    public bool Sound { get; init; } = true;
    /// <summary>Zielhilfe (Fadenkreuz) im Kamerabild.</summary>
    public bool AimHelp { get; init; } = true;
    /// <summary>So lange (ms) wird derselbe Code nach einem Treffer nicht erneut gemeldet.</summary>
    public int RepeatSuppressMs { get; init; } = 3000;
    /// <summary>Kamera-Kennung (Gerätename/ID), leer = erste.</summary>
    public string Camera { get; init; } = "";
    /// <summary>Verlauf speichern (aus = nur im Speicher, nichts auf der Platte).</summary>
    public bool KeepHistory { get; init; } = true;
    /// <summary>Höchstzahl der Verlaufseinträge; ältere fallen weg.</summary>
    public int HistoryLimit { get; init; } = 1000;
}
