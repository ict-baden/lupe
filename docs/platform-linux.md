<sub><a href="../README.md">Lupe</a> › <a href="../README.md#documentation">Documentation</a> › <a href="development.md">Development</a> › Lupe on Linux</sub>

# Lupe on Linux

How `src/Lupe.Platform.Linux` works, what it needs from the system and where X11 and Wayland differ. User-visible
messages come from `Strings.resx` in the UI language (English or German); this note is for developers and packagers.

## Session detection

`LinuxSession.Kind` reads the environment once per call:

| Environment | Session | Notes |
| --- | --- | --- |
| `WAYLAND_DISPLAY` set or `XDG_SESSION_TYPE=wayland` | Wayland | `DISPLAY` (XWayland) may be set too, but X11 calls only reach X11 programs there, so Lupe uses the portals. |
| only `DISPLAY` | X11 | |
| neither | none | Screenshots, typing and hotkeys report a reason; camera, files and the CLI still work. |

`LUPE_BACKEND=x11` forces X11 even under Wayland (only useful when the target programs run under XWayland);
`LUPE_BACKEND=wayland` forces the portals.

## Features

| Feature | X11 | Wayland |
| --- | --- | --- |
| Camera | FlashCap 1.12.0 (V4L2, `/dev/video*`), same on both | same |
| Screenshot | `XGetImage` on the root window, monitors from XRandR (`XRRGetMonitors`), work area from `_NET_WORKAREA` | `org.freedesktop.portal.Screenshot` with `interactive=false`; the PNG the portal writes is read with SkiaSharp and deleted |
| Typing | XTest (`libXtst`), layout-independent | `org.freedesktop.portal.RemoteDesktop`, `NotifyKeyboardKeysym` |
| Global hotkey | `XGrabKey` on the root window, also with Caps Lock / Num Lock | `org.freedesktop.portal.GlobalShortcuts` (KDE Plasma, GNOME 48+, Hyprland …); the desktop decides the final keys |
| Foreground window | `_NET_ACTIVE_WINDOW` (without a window manager: the input focus), activation via the EWMH message | not available (`IsSupported = false`); Lupe types into whatever has the focus after the selection closes |
| Image files | SkiaSharp: PNG, JPEG, BMP, GIF, WebP, ICO, WBMP; EXIF orientation applied | same |
| Autostart | `$XDG_CONFIG_HOME/autostart/lupe.desktop` with `--hintergrund` (`$APPIMAGE` is used when set) | same |

### Camera

`LinuxCameraProvider` lists the V4L2 devices that offer a format Lupe can convert and picks ~1280×720 with
`FormatChooser`. Frames come raw (`TranscodeFormats.DoNotTranscode`) and `FrameConversion` turns them into BGRA:
YUYV, UYVY and NV12 with BT.601 limited range, RGB24/RGB32, MJPEG via SkiaSharp. FlashCap puts a BMP header in front of
uncompressed frames; it is skipped. Errors are mapped from the errno in FlashCap's message: `EACCES` → the user is not in
the `video` group, `EBUSY` → in use, `ENOENT`/`ENODEV` → not connected. A watchdog reports a camera that disappears
(`/dev/videoN` gone) or stops delivering frames for 8 s.

### Typing on X11

`X11Typer` sends each character through the key that has it in the current layout (level 1, or level 2 with Shift). A
character the layout lacks goes through a spare keycode (one with no keysym) that is temporarily mapped to the keysym,
like `xdotool`. Up to ten spare keycodes are used in turn, and two remaps are at least 25 ms apart: Xlib clients
re-read the keymap only when they process the `MappingNotify`, and they lost characters when the remaps came faster
(seen in the Xvfb tests). The temporary mappings are removed 1 s after the last typing. Keysyms: Latin-1 directly,
everything else `0x01000000 + code point` (surrogate pairs from the core are joined first).

### Typing on Wayland

The first time, the desktop asks whether Lupe may control the keyboard. With RemoteDesktop version 2 or later, Lupe
asks for `persist_mode=2` and stores the restore token in `~/.config/lupe/remotedesktop-token` (mode 0600), so the
question does not come back on every start. Desktops without a RemoteDesktop portal (for example wlroots with only
`xdg-desktop-portal-wlr`) get a message and copying still works. Characters outside the active keyboard layout
may not arrive, depending on how the compositor maps keysyms.

### Hotkeys

Key codes are Windows virtual-key codes (see `docs/architecture-platforms.md`). Letters, digits, F-keys and named keys
map to fixed keysyms; the OEM codes (`VK_OEM_1` …) stand for the physical key position (US layout positions, e.g.
`VK_OEM_4` is the key right of P, `Ü` on a German keyboard) and `LinuxKeyboardLayout` reads their characters from XKB.
Ctrl+Alt+F1…F12 never reach programs on X11 (XKB switches to the text consoles).

## Problems shown to the user

`LinuxProblems.Check()` reports: no graphical session; no X server connection; no XTest extension; no session bus under
Wayland; the foreground-window limitation of Wayland; `/dev/video*` present but not readable (`sudo usermod -aG video
$USER`, then log in again).

## Runtime dependencies

`libx11-6`, `libxtst6`, `libxrandr2` (X11), `libfontconfig1` (SkiaSharp), on Wayland a running `xdg-desktop-portal`
with a backend for the desktop. The native SkiaSharp library comes from `SkiaSharp.NativeAssets.Linux` (x64 and arm64).
Package versions match Avalonia 12.1.4 (SkiaSharp 3.119.4, Tmds.DBus.Protocol 0.94.1) so the app loads only one copy.

## Tested

- `tests/Lupe.Tests/Platform/LinuxPlatformTests.cs`: frame conversion, keysym tables, XDG paths, `.desktop` content,
  cropping of portal screenshots, image decoding, behaviour without a session – on every OS.
- `LinuxX11Tests.cs` under Xvfb: `XGetImage` of a coloured test window, black fill outside the screen, XTest typing of
  umlauts, `€`, `Ł`, arrows, Greek and an emoji into a test X client (with and without per-character delay), held-key
  detection, `XGrabKey` with Num Lock and the "already taken" error, foreground window and activation, US layout keys.
- `LinuxWaylandTests` in sway headless with `xdg-desktop-portal-wlr`: Screenshot portal capture; the German messages
  when RemoteDesktop and GlobalShortcuts are missing.
- Not tested on real hardware: a real webcam (no device in the test container – only "no camera" and the conversions),
  RemoteDesktop and GlobalShortcuts portals (KDE/GNOME), multi-monitor and HiDPI layouts.
