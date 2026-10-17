using Lupe.Core.Camera;
using Lupe.Core.Decoding;
using Lupe.Core.Input;
using Lupe.Core.Screen;

namespace Lupe.Core.Platform;

/// <summary>
/// Alles, was Lupe vom Betriebssystem braucht, gebündelt. Eine Umsetzung je System (Lupe.Platform.Windows, .Linux,
/// .MacOS); die Oberfläche und die statischen Fassaden im Core (<see cref="Cameras"/>, <see cref="ScreenCapture"/>,
/// <see cref="KeyboardTyper"/>, <see cref="ImageFile"/>, <see cref="HotkeyText"/>) greifen über
/// <see cref="LupePlatform.Current"/> darauf zu.
/// </summary>
/// <remarks>
/// Kann ein System eine Funktion nicht (z. B. globale Tastenkürzel unter manchen Wayland-Desktops), wirft die
/// Umsetzung nicht, sondern meldet es: <c>IsSupported = false</c> plus deutscher Grund, oder über
/// <see cref="Problems"/>. Ein Fehler beim Benutzen kommt – wie bisher – als deutsche Meldung zurück.
/// </remarks>
public interface IPlatform
{
    /// <summary>Kurzname für Anzeige und Diagnose: „Windows“, „Linux (X11)“, „Linux (Wayland)“, „macOS“.</summary>
    string Name { get; }

    ICameraProvider Cameras { get; }
    IScreenCapture Screen { get; }
    IKeyboardOutput Keyboard { get; }
    IKeyboardLayout KeyboardLayout { get; }
    IGlobalHotkeyProvider Hotkeys { get; }
    IActiveWindow ActiveWindow { get; }
    IAutostart Autostart { get; }
    IImageDecoder Images { get; }
    IPlatformProblems Problems { get; }
}

/// <summary>
/// Kennzeichnet die <see cref="IPlatform"/>-Umsetzung einer Plattform-Assembly, damit <see cref="LupePlatform.Current"/>
/// sie ohne festen Verweis findet: <c>[assembly: LupePlatform(typeof(WindowsPlatform))]</c>. Der Typ braucht einen
/// öffentlichen parameterlosen Konstruktor.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class LupePlatformAttribute(Type platformType) : Attribute
{
    public Type PlatformType { get; } = platformType;
}
