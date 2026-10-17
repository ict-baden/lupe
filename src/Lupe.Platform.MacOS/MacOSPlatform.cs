using Lupe.Core.Platform;
using Lupe.Platform.MacOS;
using Lupe.Platform.MacOS.Camera;
using Lupe.Platform.MacOS.Imaging;
using Lupe.Platform.MacOS.Input;
using Lupe.Platform.MacOS.Screen;

[assembly: LupePlatform(typeof(MacOSPlatform))]

namespace Lupe.Platform.MacOS;

/// <summary>
/// Lupe unter macOS. Noch ein Gerüst: Jeder Baustein meldet „nicht verfügbar“, bis er umgesetzt ist
/// (siehe docs/architecture-platforms.md).
/// </summary>
/// <remarks>Geplant: Kamera über FlashCap (AVFoundation), Bildschirm über /usr/sbin/screencapture, Tippen über CGEvent, Tastenkürzel über RegisterEventHotKey, Autostart über einen LaunchAgent, Hinweise zu Kamera-, Bildschirmaufnahme- und Bedienungshilfen-Berechtigung.</remarks>
public sealed class MacOSPlatform : IPlatform
{
    public string Name => "macOS";
    public ICameraProvider Cameras { get; } = new MacOSCameraProvider();
    public IScreenCapture Screen { get; } = new MacOSScreenCapture();
    public IKeyboardOutput Keyboard { get; } = new MacOSKeyboard();
    public IKeyboardLayout KeyboardLayout { get; } = new MacOSKeyboardLayout();
    public IGlobalHotkeyProvider Hotkeys { get; } = new MacOSHotkeys();
    public IActiveWindow ActiveWindow { get; } = new MacOSActiveWindow();
    public IAutostart Autostart { get; } = new MacOSAutostart();
    public IImageDecoder Images { get; } = new MacOSImageDecoder();
    public IPlatformProblems Problems { get; } = new MacOSProblems();
}
