<sub><a href="../README.md">Lupe</a> › <a href="../README.md#documentation">Documentation</a> › <a href="development.md">Development</a> › Platform architecture</sub>

# Platform architecture

Lupe runs on Windows, Linux (X11 and Wayland) and macOS with an Avalonia UI; it started as a Windows-only WPF + WinRT app.
This note describes the split between the platform-neutral core and the per-OS backends.

## Project layout

| Project | Target framework | Contents |
| --- | --- | --- |
| `src/Lupe.Core` | `net10.0` | Decoding (zxing-cpp), model, settings, history, link rules, scan loop, repeat filter, hotkey text, typing logic (`KeyboardTyper`) and the **platform interfaces** (`Lupe.Core.Platform`). No P/Invoke, no WinRT. |
| `src/Lupe.Platform.Windows` | `net10.0-windows10.0.19041.0` | Today's Windows code, behaviour unchanged: MediaCapture camera, camera privacy, GDI capture with per-monitor DPI, SendInput + high-resolution waitable timer, UIPI/elevation check, RegisterHotKey + AltGr guard, WIC image decoding, HKCU `Run` autostart. |
| `src/Lupe.Platform.Linux` | `net10.0` | X11 and Wayland: FlashCap/V4L2 camera, X11 `XGetImage` / Screenshot portal, XTest / RemoteDesktop portal, `XGrabKey` / GlobalShortcuts portal, SkiaSharp image files, `~/.config/autostart/lupe.desktop`. Details in [platform-linux.md](platform-linux.md). |
| `src/Lupe.Platform.MacOS` | `net10.0` | AVFoundation camera through the Swift helper `libLupeCamera.dylib`, `screencapture -x` + ImageIO, CGEvent Unicode typing, Carbon `RegisterEventHotKey`, NSWorkspace foreground app, LaunchAgent autostart, ImageIO image files, permission hints. See [macOS backend](#macos-backend). |
| `src/Lupe` | `net10.0;net10.0-windows10.0.19041.0` | The Avalonia 12 app. The Windows target references `Lupe.Platform.Windows`, the `net10.0` target `Lupe.Platform.Linux` and `Lupe.Platform.MacOS`; `Program.CreatePlatform()` picks one with `OperatingSystem.IsXxx()` and calls `LupePlatform.Use(...)`. A runtime identifier selects the target (`-r win-x64` → Windows, anything else → `net10.0`). |
| `tests/Lupe.Tests` | `net10.0-windows10.0.19041.0;net10.0` | Windows TFM: all tests (core, Windows backend, the app as a process, headless UI). `net10.0`: platform-neutral tests plus the headless UI tests (`Ui/`), runs on every OS. References the app and the Linux/macOS backends in both TFMs. |

`Lupe.Core` grants `InternalsVisibleTo` to `Lupe.Tests` and all three `Lupe.Platform.*` assemblies, so backends may reuse
internal helpers such as `CameraList.Build/MakeUnique/CleanName`, `FormatChooser`, `SafeEvents`, `PixelImages`,
`HotkeyText.Problem` and the `KeyboardTyper.VK_*` constants.

## Selecting the platform

```csharp
// App start (recommended, no reflection, trimming-safe):
LupePlatform.Use(new WindowsPlatform());   // or LinuxPlatform / MacOSPlatform, chosen with OperatingSystem.IsXxx()

// Anywhere else:
IPlatform p = LupePlatform.Current;
```

If nothing was set, `LupePlatform.Current` loads `Lupe.Platform.Windows|Linux|MacOS` (by `OperatingSystem.IsXxx()`)
from the app directory and instantiates the type named by its `[assembly: LupePlatform(typeof(...))]` attribute. If
that fails, it falls back to `UnsupportedPlatform` (no camera, no capture, no typing, no hotkey, no autostart, no image
files; decoding, history, settings and the CLI still work). Each backend assembly must carry the attribute and the
platform class must have a public parameterless constructor.

The static facades in Core keep the old call sites working and route through `LupePlatform.Current`:
`Cameras.ListAsync/Open`, `ScreenCapture.Monitors/VirtualScreen/Capture/TryCapture`, `KeyboardTyper.Type/TryType`,
`ImageFile.LoadAsync/Extensions`, `HotkeyText.AltGrWarning`.

## Interfaces (`Lupe.Core.Platform`)

All human-readable text returned by a backend comes from `Lupe.Core.Resources.Strings` and is therefore in the UI language (see [Languages](development.md#languages)). Expected failures are reported (bool + message,
`IsSupported`, `UnsupportedReason`, `Failed` event), not thrown.

| Interface | Members | Notes |
| --- | --- | --- |
| `IPlatform` | `Name`, `Cameras`, `Screen`, `Keyboard`, `KeyboardLayout`, `Hotkeys`, `ActiveWindow`, `Autostart`, `Images`, `Problems` | Aggregate, one per OS. |
| `ICameraProvider` | `ListAsync(ct)` → `IReadOnlyList<CameraInfo>`; `Open(CameraInfo)` → `IFrameSource` | No camera → empty list. `IFrameSource` (in `Lupe.Core.Camera`) delivers BGRA `PixelImage`s on a background thread and reports problems via `Failed` (text in the UI language from `Strings.resx`), also during `StartAsync`. Use `CameraList.MakeUnique` for names, `FormatChooser` to pick ~1280×720. |
| `IScreenCapture` | `Monitors()`, `VirtualScreen`, `TryCapture(PixelRect, out PixelImage?, out string? error)` | Physical pixels of the virtual desktop; primary monitor at 0,0. The facade validates the rectangle first. May block (Wayland portal). |
| `IKeyboardOutput` | `Send(ReadOnlySpan<KeyEvent>, out int errorCode)` → accepted count; `IsDown(vk)`; `Pause(ms, ct)`; `ForegroundProblem()`; `DescribeRejection(errorCode)` | Backend for `KeyboardTyper`. Core builds the sequence, waits for held keys, releases modifiers, batches (≤ 400 events, 15 ms pause) or sends one key per batch with the per-character delay. |
| `IKeyboardLayout` | `KeyChar(vk)`, `KeyForChar(char)`, `AltGrIsCtrlAlt`, `AltGrText(vk, shift)` | Used by `HotkeyText` to show and parse layout keys (Ü, #, …) and for the AltGr warning (only where AltGr = Ctrl+Alt, i.e. Windows). |
| `IGlobalHotkeyProvider` | `UnsupportedReason`; `Register(Hotkey)` → `IGlobalHotkey` | `IGlobalHotkey`: `Pressed` (background thread), `IsRegistered`, `Error`, `Dispose()` unregisters. `Register` returns once the outcome is known. |
| `IActiveWindow` | `IsSupported`, `Foreground()` → opaque `nint` (0 = unknown), `IsOwnWindow(nint)`, `ActivateAsync(nint, ct)` → null or a reason in the UI language | Remembers the window that was in front before a scan, to type into it afterwards. Wayland: `IsSupported = false`. |
| `IAutostart` | `IsSupported`, `IsEnabled`, `Set(bool)` → null or an error in the UI language | Start command must pass `--hintergrund` (tray only). |
| `IImageDecoder` | `Extensions`, `DecodeAsync(stream, name, maxPixels, ct)`, `Translate(Exception, name)` | Seekable stream at position 0, do not close it. First frame (ICO: largest), EXIF orientation applied, downscale above `maxPixels`, then `ImageFile.FlattenOnWhite`. Core handles file opening, truncation check and the common error texts. |
| `IPlatformProblems` | `Check()` → `IReadOnlyList<PlatformProblem>` | `PlatformProblem(Feature, Message, SettingsLink?)`; e.g. Windows camera privacy, macOS Camera / Screen Recording / Accessibility permissions. Fast, never prompts. |

### Keys

`KeyEvent(VirtualKey, Scan, Flags)` uses **Windows virtual-key codes** as the shared vocabulary, and `KeyFlags`
(`Extended = 1`, `Up = 2`, `Unicode = 4`) has the same values as `KEYEVENTF_*`. With `Unicode`, `Scan` holds a UTF-16
code unit; characters outside the BMP come as a surrogate pair (both halves down, then both up) – non-Windows backends
should join the pair into one code point. Keys a backend has to map: `0x0D` Enter, `0x09` Tab, `0xA0/0xA1` Shift,
`0xA2/0xA3` Ctrl, `0xA4/0xA5` Alt, `0x5B/0x5C` Win/Super/Cmd, `0xE8` (unassigned mask key, may be ignored).
`Hotkey.Key` is a virtual-key code too (`HotkeyText` parses and formats them), so hotkey backends map VK → keysym /
macOS key code.

## Paths

`Lupe.Core.Settings.AppPaths` decides where per-user files live; `SettingsStore.DefaultPath` and
`HistoryStore.DefaultPath` use it.

| OS | Settings (`einstellungen.json`) | History (`verlauf.json`) |
| --- | --- | --- |
| Windows | `%LocalAppData%\Lupe` (unchanged) | `%LocalAppData%\Lupe` |
| Linux | `$XDG_CONFIG_HOME/lupe` (default `~/.config/lupe`) | `$XDG_DATA_HOME/lupe` (default `~/.local/share/lupe`) |
| macOS | `~/Library/Application Support/Lupe` | `~/Library/Application Support/Lupe` |

Backends that keep files of their own (the Linux RemoteDesktop restore token) put them next to the settings.

## macOS backend

`src/Lupe.Platform.MacOS` talks to the system through P/Invoke only (no Xamarin/Microsoft.macOS workload), so it builds
on every OS. The one exception is the camera: `Native/LupeCamera.swift` is compiled into `libLupeCamera.dylib` (arm64 +
x86_64, macOS 12+) when building on a Mac (needs Xcode or the Command Line Tools); built elsewhere, the library is missing
and macOS reports no camera. Created on another OS (tests), every part reports "nicht verfügbar" without calling native code. The
signatures in `Native/Native.cs` follow the macOS 14 SDK headers (each C declaration is quoted above its import); none
of it has run on a Mac yet.

| Part | Implementation | Notes |
| --- | --- | --- |
| Camera | `libLupeCamera.dylib` from `Native/LupeCamera.swift`: `AVCaptureSession` + `AVCaptureVideoDataOutput` with `kCVPixelFormatType_32BGRA`, frames copied in `AVFoundationFrameSource` (C ABI, `[UnmanagedCallersOnly]` callbacks) | Size chosen with `FormatChooser` (~1280×720) from the device's formats. Asks for the camera permission on first start, denied → hint in the UI language. Unplugging (`AVCaptureDeviceWasDisconnected`) and session errors are reported. Replaces FlashCap, whose AVFoundation backend (1.12.0) swaps YUYV/UYVY and over-releases Objective-C objects (wrong colours, crash when a thread's autorelease pool drains). |
| Screen | `CGGetActiveDisplayList` / `CGDisplayBounds` / `CGDisplayModeGetPixelWidth`; one `screencapture -x -t png -D n` per touched display, decoded with ImageIO | Pixel space = points × the largest display scale, so mixed Retina/non-Retina layouts don't overlap; captures from a lower-scale display are upscaled (nearest). `WorkArea` = `Bounds` (menu bar/Dock not subtracted). `CGDisplayCreateImage` is obsolete from macOS 15. |
| Typing | `CGEventCreateKeyboardEvent(key 0)` + `CGEventKeyboardSetUnicodeString`, posted at `kCGHIDEventTap`, flags cleared | Layout-independent. Surrogate pairs are joined into one event. Needs the Accessibility permission (`AXIsProcessTrusted`), otherwise macOS drops the events silently – `ForegroundProblem` reports it and shows the system prompt once. |
| Layout | `TISCopyCurrentKeyboardLayoutInputSource` + `UCKeyTranslate` | TIS is only called on the main thread (`pthread_main_np`), the table is cached. Letters/digits keep their VK; OEM keys map by US position (`MacKeyCodes`). |
| Hotkey | Carbon `RegisterEventHotKey` + `InstallEventHandler` on the application target | Win → ⌘, Alt → ⌥. Events need the app's Cocoa run loop (Avalonia). No permission needed. Note: the default Win+Shift+Q becomes ⌘⇧Q, which is "Log Out" in the Apple menu – the UI should offer a different default on macOS. |
| Active window | `NSWorkspace.frontmostApplication.processIdentifier`, `NSRunningApplication.activateWithOptions:` | The handle is a PID. |
| Autostart | `~/Library/LaunchAgents/de.ict-baden.lupe.plist` (`RunAtLoad`, `--hintergrund`) | Not loaded with `launchctl` (it would start a second instance now). Refused when running through `dotnet`. |
| Image files | ImageIO (`CGImageSourceCreateThumbnailAtIndex` with transform for EXIF), drawn onto white in a BGRA bitmap context | PNG, JPEG, GIF, BMP, TIFF, HEIC/HEIF, WebP, ICO (largest frame). |
| Problems | `authorizationStatusForMediaType:`, `CGPreflightScreenCaptureAccess`, `AXIsProcessTrusted` | Links: `x-apple.systempreferences:com.apple.preference.security?Privacy_Camera` / `Privacy_ScreenCapture` / `Privacy_Accessibility`. |
| Paths | `MacOSPaths.ApplicationSupport` = `~/Library/Application Support/Lupe` | .NET 8+ already maps `LocalApplicationData` there on macOS, so `SettingsStore`/`HistoryStore` need no change (checked by a macOS-only test). |

Packaging notes for the app bundle: `Info.plist` needs `NSCameraUsageDescription` (without it macOS kills the
process on camera access). Permissions are granted per bundle; an unsigned app loses them when its binary changes.

## Working in parallel

- **Avalonia UI agent**: owns `src/Lupe` (and new UI test files). Consume only `Lupe.Core` APIs and `IPlatform`; do not
  add OS calls to Core. Choose the platform in `Program.Main` and call `LupePlatform.Use(...)`. Window-level native
  bits (Mica, no-activate toast, topmost) belong to the app, not to `IPlatform`.
- **Linux backend agent**: owns `src/Lupe.Platform.Linux` and `tests/Lupe.Tests/Platform/Linux*` (run on Linux only,
  e.g. with `[Fact(Skip = ...)]` guards or `OperatingSystem.IsLinux()`, under `xvfb-run` in CI). Replace the stub
  classes file by file; keep class names so `LinuxPlatform` keeps compiling.
- **macOS backend agent**: same for `src/Lupe.Platform.MacOS` and `tests/Lupe.Tests/Platform/MacOS*`.
- Changes to the interfaces in `src/Lupe.Core/Platform` affect everyone: keep them additive (new members with a default
  implementation, or a new interface on `IPlatform` plus stubs in all three backends and `UnsupportedPlatform`).
