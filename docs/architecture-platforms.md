# Platform architecture

Lupe is being ported from Windows-only (WPF + WinRT) to Windows, Linux (X11 and Wayland) and macOS; the UI is now Avalonia. This note describes
the split between the platform-neutral core and the per-OS backends, so that the UI port (Avalonia) and the Linux and
macOS backends can be built in parallel without stepping on each other.

## Project layout

| Project | Target framework | Contents |
| --- | --- | --- |
| `src/Lupe.Core` | `net10.0` | Decoding (zxing-cpp), model, settings, history, link rules, scan loop, repeat filter, hotkey text, typing logic (`KeyboardTyper`) and the **platform interfaces** (`Lupe.Core.Platform`). No P/Invoke, no WinRT. |
| `src/Lupe.Platform.Windows` | `net10.0-windows10.0.19041.0` | Today's Windows code, behaviour unchanged: MediaCapture camera, camera privacy, GDI capture with per-monitor DPI, SendInput + high-resolution waitable timer, UIPI/elevation check, RegisterHotKey + AltGr guard, WIC image decoding, HKCU `Run` autostart. |
| `src/Lupe.Platform.Linux` | `net10.0` | X11 and Wayland: FlashCap/V4L2 camera, X11 `XGetImage` / Screenshot portal, XTest / RemoteDesktop portal, `XGrabKey` / GlobalShortcuts portal, SkiaSharp image files, `~/.config/autostart/lupe.desktop`. Details in [platform-linux.md](platform-linux.md). |
| `src/Lupe.Platform.MacOS` | `net10.0` | Skeleton. To be filled: FlashCap/AVFoundation, `screencapture -x`, CGEvent typing, Carbon `RegisterEventHotKey`, LaunchAgent autostart, permission hints. |
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

`Lupe.Core.Settings.AppPaths` decides where per-user files live; `SettingsStore.DefaultPath` and
`HistoryStore.DefaultPath` use it.

| OS | Settings (`einstellungen.json`) | History (`verlauf.json`) |
| --- | --- | --- |
| Windows | `%LocalAppData%\Lupe` (unchanged) | `%LocalAppData%\Lupe` |
| Linux | `$XDG_CONFIG_HOME/lupe` (default `~/.config/lupe`) | `$XDG_DATA_HOME/lupe` (default `~/.local/share/lupe`) |
| macOS | `~/Library/Application Support/Lupe` | `~/Library/Application Support/Lupe` |

Backends that keep files of their own (the Linux RemoteDesktop restore token) put them next to the settings.

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
