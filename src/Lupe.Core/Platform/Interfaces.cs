using System.Diagnostics.CodeAnalysis;
using Lupe.Core.Camera;
using Lupe.Core.Input;
using Lupe.Core.Model;
using Lupe.Core.Screen;

namespace Lupe.Core.Platform;

// Die Schnittstellen der Plattform-Bausteine. Texte für Menschen (Fehler, Hinweise) sind immer deutsch.

/// <summary>Webcams auflisten und öffnen.</summary>
public interface ICameraProvider
{
    /// <summary>
    /// Alle brauchbaren Kameras mit eindeutigen Namen (siehe <see cref="CameraList.MakeUnique"/>). Keine Kamera oder
    /// fehlende Systemfunktionen → leere Liste, kein Fehler. Nur ein Abbruch über das Token wirft.
    /// </summary>
    Task<IReadOnlyList<CameraInfo>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Öffnet die Kamera als Bildquelle (startet sie noch nicht). Erwartbare Probleme (belegt, abgezogen, keine Berechtigung)
    /// meldet die Quelle über <see cref="IFrameSource.Failed"/> mit deutschem Text, nicht als Ausnahme.
    /// </summary>
    IFrameSource Open(CameraInfo camera);
}

/// <summary>Bildschirmfotos in physischen Pixeln des virtuellen Desktops (Hauptmonitor beginnt bei 0,0).</summary>
public interface IScreenCapture
{
    /// <summary>Alle Monitore, von links nach rechts; leer, wenn das System keine nennt.</summary>
    IReadOnlyList<MonitorArea> Monitors();

    /// <summary>Der ganze virtuelle Desktop (Vereinigung aller Monitore).</summary>
    PixelRect VirtualScreen { get; }

    /// <summary>
    /// Foto des Rechtecks als BGRA (Alpha 255), genau so groß wie <paramref name="area"/>; Bereiche außerhalb der Monitore
    /// schwarz. Geht es nicht (gesperrt, keine Berechtigung, Wayland-Portal abgelehnt): false und deutscher Grund.
    /// Darf blockieren (z. B. Portal-Anfrage) – nicht im UI-Thread mit langen Wartezeiten aufrufen.
    /// Die Fläche ist schon geprüft (Breite/Höhe &gt; 0, nicht zu groß).
    /// </summary>
    bool TryCapture(PixelRect area, [NotNullWhen(true)] out PixelImage? image, out string? error);
}

/// <summary>
/// Tastatur-Ausgabe für <see cref="KeyboardTyper"/>: Der Core baut die Ereignisfolge (Unicode-Zeichen, Eingabe, Tab,
/// Loslassen gedrückter Umschalttasten), teilt sie in Pakete und hält die Pausen ein; die Plattform schickt die Pakete
/// an das Fenster mit dem Fokus. Tastencodes sind Windows-Virtual-Key-Codes (siehe <see cref="KeyEvent"/>).
/// </summary>
public interface IKeyboardOutput
{
    /// <summary>Sendet die Ereignisse am Stück; liefert, wie viele angenommen wurden (bei weniger: Fehlercode der Plattform).</summary>
    int Send(ReadOnlySpan<KeyEvent> events, out int errorCode);

    /// <summary>Ist die Taste (Virtual-Key-Code) gerade gedrückt? Weiß es die Plattform nicht: false.</summary>
    bool IsDown(int virtualKey);

    /// <summary>Wartet möglichst genau (Zeichenpause beim Tippen wie ein Handscanner); ein Abbruch beendet das Warten.</summary>
    void Pause(int milliseconds, CancellationToken ct);

    /// <summary>Warum Eingaben das Ziel jetzt nicht erreichen würden (deutsch, z. B. Zielfenster mit höheren Rechten, keine Berechtigung), sonst null.</summary>
    string? ForegroundProblem();

    /// <summary>Deutsche Meldung, wenn gar nichts angenommen wurde (<paramref name="errorCode"/> aus <see cref="Send"/>).</summary>
    string DescribeRejection(int errorCode);
}

/// <summary>Was das aktuelle Tastaturlayout auf welche Taste legt (für Anzeige und Eingabe von Tastenkürzeln).</summary>
public interface IKeyboardLayout
{
    /// <summary>Das Zeichen, das die Taste ohne Umschalttaste schreibt (bei Tottasten deren Zeichen); null bei keinem.</summary>
    char? KeyChar(int virtualKey);

    /// <summary>Die Taste, die das Zeichen ohne Umschalttaste schreibt, sonst null.</summary>
    int? KeyForChar(char c);

    /// <summary>Meldet das System AltGr als Strg+Alt (Windows)? Dann kollidieren Kürzel mit Strg+Alt mit AltGr-Zeichen.</summary>
    bool AltGrIsCtrlAlt { get; }

    /// <summary>Was AltGr (+ Umschalt) + Taste schreibt; null, wenn nichts oder unbekannt.</summary>
    string? AltGrText(int virtualKey, bool shift);
}

/// <summary>Systemweite Tastenkürzel.</summary>
public interface IGlobalHotkeyProvider
{
    /// <summary>Warum es hier keine globalen Tastenkürzel gibt (deutsch, z. B. Wayland ohne GlobalShortcuts-Portal), sonst null.</summary>
    string? UnsupportedReason { get; }

    /// <summary>
    /// Meldet das Kürzel an; kehrt zurück, wenn das Ergebnis feststeht (<see cref="IGlobalHotkey.IsRegistered"/>,
    /// <see cref="IGlobalHotkey.Error"/>). Wirft nicht wegen des Systems.
    /// </summary>
    IGlobalHotkey Register(Hotkey hotkey);
}

/// <summary>Ein angemeldetes Tastenkürzel. Dispose meldet ab; danach kommt kein <see cref="Pressed"/> mehr.</summary>
public interface IGlobalHotkey : IDisposable
{
    /// <summary>Auf einem Hintergrund-Thread.</summary>
    event EventHandler? Pressed;
    bool IsRegistered { get; }
    /// <summary>Warum die Anmeldung scheiterte (deutsch), sonst null.</summary>
    string? Error { get; }
}

/// <summary>
/// Das Fenster im Vordergrund – damit Lupe nach einem Scan in das Programm tippen kann, das vorher vorn war.
/// Kennungen sind undurchsichtig (Windows: HWND, X11: Window-ID, macOS: Prozess-ID); 0 = unbekannt/keins.
/// </summary>
public interface IActiveWindow
{
    /// <summary>Kann die Plattform das Vordergrundfenster überhaupt bestimmen? (Wayland: meist nein.)</summary>
    bool IsSupported { get; }

    /// <summary>Kennung des Vordergrundfensters, 0 wenn unbekannt.</summary>
    nint Foreground();

    /// <summary>Gehört das Fenster zu Lupe selbst?</summary>
    bool IsOwnWindow(nint window);

    /// <summary>
    /// Holt das Fenster nach vorn und wartet kurz, bis es den Fokus hat. null = vorn, sonst deutscher Grund (dann nicht tippen).
    /// </summary>
    Task<string?> ActivateAsync(nint window, CancellationToken ct = default);
}

/// <summary>Ziele für „Eintippen“, die kein echtes Fenster sind.</summary>
public static class TypeTargets
{
    /// <summary>
    /// „Das Fenster mit dem Fokus“, wenn das System nicht sagt, welches vorn ist (Wayland: <see cref="IActiveWindow.IsSupported"/>
    /// ist false). Nur dann vergeben; <see cref="IActiveWindow.ActivateAsync"/> hat dort nichts zu tun.
    /// </summary>
    public const nint FocusedWindow = -1;

    /// <summary>
    /// Ziel aus dem Vordergrundfenster: Lupe selbst oder unbekannt bei bekanntem System = 0 (nicht tippen). Weiß das System
    /// es nicht (<paramref name="foreground"/> 0, nicht unterstützt), entscheidet <paramref name="lupeActive"/>.
    /// </summary>
    public static nint From(IActiveWindow active, nint foreground, bool lupeActive)
    {
        if (foreground != 0) return active.IsOwnWindow(foreground) ? 0 : foreground;
        return !active.IsSupported && !lupeActive ? FocusedWindow : 0;
    }
}

/// <summary>„Mit dem System starten“ für den aktuellen Benutzer (startet Lupe nur im Infobereich: <c>--hintergrund</c>).</summary>
public interface IAutostart
{
    bool IsSupported { get; }
    bool IsEnabled { get; }
    /// <summary>Ein- oder ausschalten; deutsche Fehlermeldung oder null.</summary>
    string? Set(bool enabled);
}

/// <summary>Bilddateien in Pixel umwandeln (für <see cref="Decoding.ImageFile"/>).</summary>
public interface IImageDecoder
{
    /// <summary>Dateiendungen (klein, mit Punkt), die der Öffnen-Dialog anbietet – in sinnvoller Reihenfolge.</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>
    /// Dekodiert das erste Bild (ICO: das größte) aus dem durchsuchbaren Strom ab Position 0: EXIF-Drehung angewendet,
    /// höchstens <paramref name="maxPixels"/> (sonst verkleinert), BGRA ohne Transparenz (auf Weiß verrechnet, siehe
    /// <see cref="Decoding.ImageFile.FlattenOnWhite"/>). Den Strom nicht schließen. Unverständliches →
    /// <see cref="InvalidDataException"/> mit deutscher Meldung; andere Ausnahmen übersetzt <see cref="Translate"/>.
    /// </summary>
    Task<PixelImage> DecodeAsync(Stream stream, string name, int maxPixels, CancellationToken ct);

    /// <summary>Übersetzt eine Ausnahme des Decoders in eine deutsche, anzeigbare (InvalidDataException/IOException).</summary>
    Exception Translate(Exception ex, string name);
}

/// <summary>Wofür eine Berechtigung oder Systemeinstellung fehlt.</summary>
public enum PlatformFeature
{
    Camera,
    Screen,
    Typing,
    Hotkey,
    ActiveWindow,
    Autostart,
}

/// <summary>Ein bekanntes Hindernis: deutsche Meldung, was fehlt und was zu tun ist; optional ein Link in die Systemeinstellungen.</summary>
public sealed record PlatformProblem(PlatformFeature Feature, string Message, string? SettingsLink = null);

/// <summary>Berechtigungen und Systemeinstellungen prüfen (macOS: Kamera, Bildschirmaufnahme, Bedienungshilfen; Windows: Kamera-Datenschutz …).</summary>
public interface IPlatformProblems
{
    /// <summary>Was gerade fehlt; leer, wenn nichts bekannt ist. Schnell und ohne Rückfragen an den Benutzer.</summary>
    IReadOnlyList<PlatformProblem> Check();
}
