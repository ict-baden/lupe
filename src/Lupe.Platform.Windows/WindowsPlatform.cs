using Lupe.Core.Platform;
using Lupe.Platform.Windows.Camera;
using Lupe.Platform.Windows.Imaging;
using Lupe.Platform.Windows.Input;
using Lupe.Platform.Windows.Screen;
using Lupe.Platform.Windows;

[assembly: LupePlatform(typeof(WindowsPlatform))]

namespace Lupe.Platform.Windows;

/// <summary>Lupe unter Windows: MediaCapture, GDI, SendInput, RegisterHotKey, Registry-Autostart, WIC.</summary>
public sealed class WindowsPlatform : IPlatform
{
    public string Name => "Windows";
    public ICameraProvider Cameras { get; } = new WindowsCameraProvider();
    public IScreenCapture Screen { get; } = new WindowsScreenCapture();
    public IKeyboardOutput Keyboard => WindowsKeyboard.Instance;
    public IKeyboardLayout KeyboardLayout { get; } = new WindowsKeyboardLayout();
    public IGlobalHotkeyProvider Hotkeys { get; } = new WindowsHotkeys();
    public IActiveWindow ActiveWindow { get; } = new WindowsActiveWindow();
    public IAutostart Autostart { get; } = new WindowsAutostart();
    public IImageDecoder Images { get; } = new WicImageDecoder();
    public IPlatformProblems Problems { get; } = new WindowsProblems();
}
