# Platform architecture

Lupe is being ported from Windows-only (WPF + WinRT) to Windows, Linux (X11 and Wayland) and macOS. This note describes
the split between the platform-neutral core and the per-OS backends, so that the UI port (Avalonia) and the Linux and
macOS backends can be built in parallel without stepping on each other.

## Project layout

| Project | Target framework | Contents |
| --- | --- | --- |
| `src/Lupe.Core` | `net10.0` | Decoding (zxing-cpp), model, settings, history, link rules, scan loop, repeat filter, hotkey text, typing logic (`KeyboardTyper`) and the **platform interfaces** (`Lupe.Core.Platform`). No P/Invoke, no WinRT. |
| `src/Lupe.Platform.Windows` | `net10.0-windows10.0.19041.0` | Today's Windows code, behaviour unchanged: MediaCapture camera, camera privacy, GDI capture with per-monitor DPI, SendInput + high-resolution waitable timer, UIPI/elevation check, RegisterHotKey + AltGr guard, WIC image decoding, HKCU `Run` autostart. |
| `src/Lupe.Platform.Linux` | `net10.0` | Skeleton (every part reports “nicht verfügbar”). To be filled: FlashCap/V4L2, X11 `XGetImage` / Screenshot portal, XTest / RemoteDesktop portal, `XGrabKey` / GlobalShortcuts portal, `~/.config/autostart/lupe.desktop`. |
| `src/Lupe.Platform.MacOS` | `net10.0` | FlashCap/AVFoundation camera, `screencapture -x` + ImageIO, CGEvent Unicode typing, Carbon `RegisterEventHotKey`, NSWorkspace foreground app, LaunchAgent autostart, ImageIO image files, permission hints. See [macOS backend](#macos-backend). |
| `src/Lupe` | `net10.0-windows10.0.19041.0` (WPF, for now) | The app. Calls `LupePlatform.Use(new WindowsPlatform())` at start. Will be replaced by the Avalonia app, which multi-targets `net10.0;net10.0-windows10.0.19041.0` and references the backend for its OS. |
| `tests/Lupe.Tests` | `net10.0-windows10.0.19041.0;net10.0` | Windows TFM: all tests (core, Windows backend, WPF app). `net10.0`: platform-neutral tests only (files excluded in the csproj), runs on every OS. References the Linux/macOS skeletons in both TFMs. |

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

All human-readable text returned by a backend is **German**. Expected failures are reported (bool + message,
`IsSupported`, `UnsupportedReason`, `Failed` event), not thrown.

| Interface | Members | Notes |
| --- | --- | --- |
| `IPlatform` | `Name`, `Cameras`, `Screen`, `Keyboard`, `KeyboardLayout`, `Hotkeys`, `ActiveWindow`, `Autostart`, `Images`, `Problems` | Aggregate, one per OS. |
| `ICameraProvider` | `ListAsync(ct)` → `IReadOnlyList<CameraInfo>`; `Open(CameraInfo)` → `IFrameSource` | No camera → empty list. `IFrameSource` (in `Lupe.Core.Camera`) delivers BGRA `PixelImage`s on a background thread and reports problems via `Failed` (German), also during `StartAsync`. Use `CameraList.MakeUnique` for names, `FormatChooser` to pick ~1280×720. |
| `IScreenCapture` | `Monitors()`, `VirtualScreen`, `TryCapture(PixelRect, out PixelImage?, out string? error)` | Physical pixels of the virtual desktop; primary monitor at 0,0. The facade validates the rectangle first. May block (Wayland portal). |
| `IKeyboardOutput` | `Send(ReadOnlySpan<KeyEvent>, out int errorCode)` → accepted count; `IsDown(vk)`; `Pause(ms, ct)`; `ForegroundProblem()`; `DescribeRejection(errorCode)` | Backend for `KeyboardTyper`. Core builds the sequence, waits for held keys, releases modifiers, batches (≤ 400 events, 15 ms pause) or sends one key per batch with the per-character delay. |
| `IKeyboardLayout` | `KeyChar(vk)`, `KeyForChar(char)`, `AltGrIsCtrlAlt`, `AltGrText(vk, shift)` | Used by `HotkeyText` to show and parse layout keys (Ü, #, …) and for the AltGr warning (only where AltGr = Ctrl+Alt, i.e. Windows). |
| `IGlobalHotkeyProvider` | `UnsupportedReason`; `Register(Hotkey)` → `IGlobalHotkey` | `IGlobalHotkey`: `Pressed` (background thread), `IsRegistered`, `Error`, `Dispose()` unregisters. `Register` returns once the outcome is known. |
| `IActiveWindow` | `IsSupported`, `Foreground()` → opaque `nint` (0 = unknown), `IsOwnWindow(nint)`, `ActivateAsync(nint, ct)` → null or German reason | Remembers the window that was in front before a scan, to type into it afterwards. Wayland: `IsSupported = false`. |
| `IAutostart` | `IsSupported`, `IsEnabled`, `Set(bool)` → null or German error | Start command must pass `--hintergrund` (tray only). |
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

Settings and history currently live under `LocalApplicationData/Lupe` (`SettingsStore.DefaultPath`,
`HistoryStore.DefaultPath`). Moving them to XDG (`~/.config/lupe`, `~/.local/share/lupe`) and
`~/Library/Application Support/Lupe` is open; do it in Core (it needs no native calls), keeping the Windows path unchanged.

## macOS backend

`src/Lupe.Platform.MacOS` talks to the system through P/Invoke only (no Xamarin/Microsoft.macOS workload), so it builds
on every OS. Created on another OS (tests), every part reports "nicht verfügbar" without calling native code. The
signatures in `Native/Native.cs` follow the macOS 14 SDK headers (each C declaration is quoted above its import); none
of it has run on a Mac yet.

| Part | Implementation | Notes |
| --- | --- | --- |
| Camera | FlashCap 1.12.0 `AVFoundationDevices`; YUV is transcoded by FlashCap and arrives as BMP (`DibConverter`), MJPEG is decoded with ImageIO | Format chosen with `FormatChooser` (~1280×720). Denied permission → German hint before opening. Unplugging is not detected (FlashCap has no event). |
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
