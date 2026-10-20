// SPDX-License-Identifier: MIT
using Lupe.Core.Platform;
using Lupe.Platform.MacOS;
using Lupe.Platform.MacOS.Camera;
using Lupe.Platform.MacOS.Imaging;
using Lupe.Platform.MacOS.Input;
using Lupe.Platform.MacOS.Screen;

[assembly: LupePlatform(typeof(MacOSPlatform))]

namespace Lupe.Platform.MacOS;

/// <summary>
/// Lupe unter macOS: Kamera über AVFoundation (Swift-Helfer libLupeCamera.dylib, Native/LupeCamera.swift), Bildschirm über /usr/sbin/screencapture, Tippen über CGEvent,
/// Tastenkürzel über Carbon RegisterEventHotKey, Autostart über einen LaunchAgent, Bilddateien über ImageIO, Hinweise zu
/// den Berechtigungen Kamera, Bildschirmaufnahme und Bedienungshilfen (siehe docs/architecture-platforms.md).
/// </summary>
/// <remarks>Auf einem anderen System erzeugt (Tests) meldet jeder Baustein „nicht verfügbar“, ohne macOS-Funktionen aufzurufen.</remarks>
public sealed class MacOSPlatform : IPlatform
{
    private readonly MacOSKeyboardLayout _layout = new();

    public MacOSPlatform() => Hotkeys = new MacOSHotkeys(_layout);

    public string Name => "macOS";
    public ICameraProvider Cameras { get; } = new MacOSCameraProvider();
    public IScreenCapture Screen { get; } = new MacOSScreenCapture();
    public IKeyboardOutput Keyboard { get; } = new MacOSKeyboard();
    public IKeyboardLayout KeyboardLayout => _layout;
    public IGlobalHotkeyProvider Hotkeys { get; }
    public IActiveWindow ActiveWindow { get; } = new MacOSActiveWindow();
    public IAutostart Autostart { get; } = new MacOSAutostart();
    public IImageDecoder Images { get; } = new MacOSImageDecoder();
    public IPlatformProblems Problems { get; } = new MacOSProblems();
}
