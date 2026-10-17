using Lupe.Core.Platform;
using Lupe.Platform.Linux;
using Lupe.Platform.Linux.Camera;
using Lupe.Platform.Linux.Imaging;
using Lupe.Platform.Linux.Input;
using Lupe.Platform.Linux.Screen;

[assembly: LupePlatform(typeof(LinuxPlatform))]

namespace Lupe.Platform.Linux;

/// <summary>
/// Lupe unter Linux. Noch ein Gerüst: Jeder Baustein meldet „nicht verfügbar“, bis er umgesetzt ist
/// (siehe docs/architecture-platforms.md).
/// </summary>
/// <remarks>Geplant: Kamera über FlashCap (V4L2), Bildschirm über XGetImage bzw. das Screenshot-Portal, Tippen über XTest bzw. das RemoteDesktop-Portal, Tastenkürzel über XGrabKey bzw. das GlobalShortcuts-Portal, Autostart über ~/.config/autostart/lupe.desktop.</remarks>
public sealed class LinuxPlatform : IPlatform
{
    public string Name => "Linux";
    public ICameraProvider Cameras { get; } = new LinuxCameraProvider();
    public IScreenCapture Screen { get; } = new LinuxScreenCapture();
    public IKeyboardOutput Keyboard { get; } = new LinuxKeyboard();
    public IKeyboardLayout KeyboardLayout { get; } = new LinuxKeyboardLayout();
    public IGlobalHotkeyProvider Hotkeys { get; } = new LinuxHotkeys();
    public IActiveWindow ActiveWindow { get; } = new LinuxActiveWindow();
    public IAutostart Autostart { get; } = new LinuxAutostart();
    public IImageDecoder Images { get; } = new LinuxImageDecoder();
    public IPlatformProblems Problems { get; } = new LinuxProblems();
}
