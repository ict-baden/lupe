<sub><a href="../README.md">Lupe</a> › <a href="../README.md#documentation">Documentation</a> › Limitations</sub>

# Limitations

What Lupe cannot do, what was not tested the way it will be used, and the pitfalls worth knowing – stated plainly.

**Contents:** [Status](#status) · [Not tested on real hardware](#not-tested-on-real-hardware) · [Decoding](#decoding) · [Camera](#camera) · [Screen scan and shortcut](#screen-scan-and-shortcut) · [Typing](#typing) · [History and files](#history-and-files) · [Command line](#command-line) · [Linux and macOS](#linux-and-macos)

## Status

- **Pre-release 0.1.0.** Builds come from GitHub Actions; there is no stable release yet.
- **Not code-signed.** SmartScreen may warn about `Lupe.exe` and the MSI.
- **Spanish, French and Chinese are machine translations.** The interface comes in English, German, Spanish, French and Chinese (simplified); es, fr and zh-Hans were machine-translated and have not been reviewed by native speakers yet – that happens before the first public release (ICT-Baden guideline 13). The German command-line keywords (`lesen`, `--hilfe`, `hell|dunkel|beide` …) stay; English names (`read`, `--help`, `light|dark|both`) work as well. The MSI installer is German.
- **Windows x64** (Windows 10 1809 or later), **Linux** x64/arm64 (X11 and Wayland) and **macOS** 12 or later (Apple Silicon and Intel). Windows is the platform Lupe was built and tested on; see [Linux and macOS](#linux-and-macos).

## Not tested on real hardware

- **No webcam during development.** The camera code – opening, format choice, frame conversion, error handling – is covered by tests against a **simulated frame source** and by tests for the error classification. Tests that open a real camera exist, but skip themselves when Windows reports no video capture device, which was the case on the development machine and on CI. Real cameras, drivers and their quirks have not been tried yet.
- **No interactive desktop in the build environment.** Screen capture, the global shortcut and real typing have tests that need a visible, active desktop. They skip themselves in the build environment and on the CI runner and are meant to be run on a normal desktop. What works without a desktop is tested: copying pixels from an in-memory drawing surface, the overlay over the demo desktop, the typing sequence against a simulated keyboard, and the shortcut logic against a simulated Windows API.
- **The real-typing tests type into a standard Windows text box** (a Win32 edit control); they only run on a visible desktop and have not run in the build environment. Programs that read raw keyboard scan codes instead of characters – some games, some remote-desktop or terminal clients – may not accept Lupe's Unicode input; use *Kopieren* there.

## Decoding

- **Very small barcodes in reduced screenshots.** A barcode shown at about one pixel per bar – for example in a document preview scaled to half size – is usually not readable; a QR code at that size often is. Zoom in or open the original.
- **MaxiCode only from clean, upright images.** zxing-cpp's MaxiCode reader has no detector that searches a larger picture: the code has to be the only dark thing on a plain background and must not be rotated, noisy or part of a busy picture.
- **Code 39** is read as printed: a check digit is not verified, Full ASCII sequences (`+A` for `a`, `/N` for `.`) are not expanded, Code 32 (Italian pharmacy code) is not converted – like hardware scanners in their factory setting.
- **EAN/UPC add-ons** (2 or 5 digits) are not part of the result.
- **Misreads in very blurry product photos.** In a handful of zxing-cpp's own sample photos of strongly blurred UPC codes, a wrong but check-digit-valid EAN-13 is read. The earlier version had the same problem.
- **Not supported at all:** Intelligent Mail (`IMB`, read by earlier versions of Lupe – zxing-cpp has no reader for it), MSI, Pharmacode, Plessey and Aztec runes – see [How it works › Phantom-code protection](how-it-works.md#phantom-code-protection). History entries with `IMB` stay visible; a settings file that lists only `IMB` means “all formats”.
- **Two identical codes in one picture** are reported once (same format, same content).
- **Very large pictures** are reduced to 40 megapixels while loading. Codes with very fine modules in huge photos can be lost by that.

## Camera

- **Fixed resolution.** Lupe picks a camera format near 1280 × 720 and searches at most that. A small code far away from the camera needs to come closer.
- **No camera controls.** Lupe does not set focus, zoom, exposure or a light; it uses what the driver does by itself.
- **Live search only.** The camera reads the whole frame; the slanted and enlarged versions of the thorough search are reserved for the screen scan and files. A barcode held at a steep angle is found once you hold it straighter.
- **No light-on-dark barcodes from the camera.** The live search reads light 2D codes (QR, Data Matrix, Aztec, PDF417, MaxiCode), but not a light 1D barcode on a dark background. A photo of it on the page *Datei* gets the thorough search.
- **Shared mode.** If another program holds the camera, Lupe may get it only in shared read-only mode with that program's format, or not at all.
- **No typing from the camera** – Lupe is the active window then. Copy, or use the screen scan.

## Screen scan and shortcut

- **No screen scan while Windows refuses the capture** – locked desktop, sign-in screen, no visible session. Lupe says so (*Bildschirm-Scan nicht möglich*) instead of showing a black still image.
- **The shortcut can be taken.** If another program already uses *Win+Umschalt+Q*, Lupe cannot register it and says so; choose another combination.
- **Shortcuts with <kbd>Ctrl</kbd>+<kbd>Alt</kbd>** collide with <kbd>AltGr</kbd> on German keyboards. Lupe re-types the AltGr character, but a shortcut with <kbd>Win</kbd> is safer ([details](settings.md#screen-scan)).
- **One Lupe per Windows session.** A second start only brings the first window to the front.

## Typing

- **Elevated windows.** Windows does not allow a normal program to type into a program running as administrator. Lupe detects it, types nothing and says so. Start Lupe as administrator too if you need it ([details](installation.md#running-as-administrator)).
- **Control characters are dropped.** Line breaks become <kbd>Enter</kbd>, tabs <kbd>Tab</kbd>; everything else below code 32 – for example the group separator `0x1D` in GS1 codes – cannot be typed. The command line shows it as `\x1D`; the clipboard keeps it.
- **Only with the shortcut from another program.** A scan started from the Lupe window or the notification-area menu does not type – the window in front would be Lupe.
- **Keys held down.** Lupe waits up to 1.5 seconds for you to release the shortcut keys, then releases the modifiers itself.

## History and files

- **A broken history file is replaced** by the next entry, without a backup (the settings file, in contrast, is backed up as `.defekt-…`).
- **Up to 10,000 history entries.** The whole file is rewritten after every scan; much larger histories would make every hit noticeably slower.
- **CSV values starting with `=`, `+`, `-` or `@`** get an apostrophe in front, e.g. phone numbers like `+41 …` – deliberate protection against formulas in Excel.
- **Hand-edited settings** are overwritten by the next change in the app if Lupe was running while you edited ([details](settings.md#the-settings-file)).
- **Uninstalling** leaves the settings, the history and the autostart entry behind ([details](installation.md#updates-and-uninstalling)).

## Command line

- **A Windows program:** command prompts do not wait for it unless you use `start /wait` or `| Out-Default` – and `start /wait` together with `> file` leaves the file empty ([details](command-line.md#waiting-for-the-end)).
- **Pipes use the console's code page.** Characters outside it are replaced; redirect into a file for lossless UTF-8.
- **PowerShell 7.4+** writes the console's code page into files with `> codes.txt`; use a `.cmd` file or set `[Console]::OutputEncoding` first.
- **`> NUL`** does not silence the output.
- **No file name in the output lines** – call `lesen` once per image if you need it ([example](command-line.md#examples)).


## Linux and macOS

- **New ports, less tested.** The Linux backend ran in containers only: X11 under Xvfb (screen capture, XTest typing, `XGrabKey`) and Wayland in a headless sway with `xdg-desktop-portal-wlr` (screenshot portal). No real webcam, no KDE or GNOME portals, no multi-monitor or HiDPI setup. **The macOS backend has not run on a Mac yet** – it builds, and its logic tests pass on CI; the native calls are untested.
- **Wayland: no foreground window.** Wayland does not tell programs which window is in front. Lupe cannot bring back “the window you came from”; it types into whatever has the focus after the selection closes.
- **Wayland: everything through portals.** Screen scan (Screenshot portal), typing (RemoteDesktop portal) and the shortcut (GlobalShortcuts portal) need `xdg-desktop-portal` with a backend for the desktop. The desktop asks for permission and may pick other keys for the shortcut. Without the GlobalShortcuts portal (GNOME before 48, some wlroots compositors) there is no global shortcut – Lupe says so in the settings; a shortcut of the desktop itself can run `lupe --bildschirm`, and the tray menu and the window still start the scan. sway with `xdg-desktop-portal-wlr` has no RemoteDesktop portal, so *Eintippen* reports that and only copies. The window runs through XWayland (Avalonia draws with X11); with XWayland switched off Lupe cannot open a window. Characters that are not on the active keyboard layout may not arrive through the RemoteDesktop portal, depending on the desktop – use *Kopieren* there.
- **Wayland: monitor layout.** The overlay is one full-screen window per monitor; monitor positions come from XWayland and are only known after the first screenshot.
- **Linux tray icon.** GNOME shows no tray icon without the AppIndicator extension. Lupe cannot detect that; closing the window then leaves Lupe running in the background without a visible icon.
- **Linux camera.** Needs the group `video`. Unplugging is detected by a watchdog (no frames for a while), not by a system event.
- **macOS permissions.** Camera, Screen Recording and Accessibility must be allowed in *System Settings › Privacy & Security*; after allowing Screen Recording, Lupe has to be restarted. The shortcut only works while Lupe runs (also in the background).
- **macOS screen capture** uses `screencapture` per display. The work area is the full screen (menu bar and Dock are not subtracted). With displays of different scale, the screenshot of a lower-scale display is enlarged.
- **Not notarised.** Gatekeeper blocks the first start; allow it once under *System Settings › Privacy & Security › Open Anyway*, see [Installation › macOS](installation.md#macos). Each update counts as a new app for the permissions (ad-hoc signature), so *Screen Recording* and *Accessibility* have to be granted again.

---

<p align="center"><sub>
<a href="installation.md">Installation</a> · <a href="usage.md">Using the app</a> · <a href="settings.md">Settings</a> · <a href="link-rules.md">Link rules</a> · <a href="command-line.md">Command line</a> · <a href="how-it-works.md">How it works</a> · <a href="data-and-privacy.md">Data &amp; privacy</a> · <b>Limitations</b> · <a href="development.md">Development</a><br>
<a href="../README.md">← Back to Lupe</a>
</sub></p>
