<sub><a href="../README.md">Lupe</a> › <a href="../README.md#documentation">Documentation</a> › Limitations</sub>

# Limitations

What Lupe cannot do, what was not tested the way it will be used, and the pitfalls worth knowing – stated plainly.

**Contents:** [Status](#status) · [Automated tests and real hardware](#automated-tests-and-real-hardware) · [Decoding](#decoding) · [Camera](#camera) · [Screen scan and shortcut](#screen-scan-and-shortcut) · [Typing](#typing) · [History and files](#history-and-files) · [Command line](#command-line) · [Linux and macOS](#linux-and-macos)

## Status

- **Version 1.0, macOS only partly tested.** Lupe 1.0.0 was tested on Windows and Linux. On a real Mac (Apple Silicon) it has been started with its window, the demo camera and reading images from the command line; the screen scan, typing, the shortcut and a real camera have not been tried there yet (see [Linux and macOS](#linux-and-macos)).
- **Versions and builds.** The versions are the releases with a tag (the current one is marked *Latest*); the pre-release `latest` is an automatic build of `main` for trying out changes.
- **Not code-signed.** SmartScreen may warn about `Lupe.exe` and the MSI; macOS asks once before the first start.
- **Spanish, French and Chinese are machine translations.** The interface comes in English, German, Spanish, French and Chinese (simplified); es, fr and zh-Hans were machine-translated and have not been reviewed by native speakers yet. The command line takes English and German keywords in every language (`read` or `lesen`, `--help` or `--hilfe`, `light|dark|both` or `hell|dunkel|beide` …); the help shows the German ones only in the German interface.
- **Windows x64** (Windows 10 1809 or later), **Linux** x64/arm64 (X11 and Wayland) and **macOS** 12 or later (Apple Silicon and Intel). Lupe was developed on Windows and tested on Windows and Linux; see [Linux and macOS](#linux-and-macos).

## Automated tests and real hardware

- **No webcam in the automated tests.** The camera code – opening, format choice, frame conversion, error handling – is covered by tests against a **simulated frame source** and by tests for the error classification. Tests that open a real camera exist, but skip themselves when the system reports no video capture device, as on CI. Beyond the release tests on Windows and Linux, cameras, drivers and their quirks have not been tried.
- **No interactive desktop in the build environment.** Screen capture, the global shortcut and real typing have tests that need a visible, active desktop. They skip themselves in the build environment and on the CI runner and are meant to be run on a normal desktop. What works without a desktop is tested: copying pixels from an in-memory drawing surface, the overlay over the demo desktop, the typing sequence against a simulated keyboard, and the shortcut logic against a simulated Windows API.
- **The real-typing tests type into a standard Windows text box** (a Win32 edit control); they only run on a visible desktop and have not run in the build environment. Programs that read raw keyboard scan codes instead of characters – some games, some remote-desktop or terminal clients – may not accept Lupe's Unicode input; use *Copy* there.

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
- **No light-on-dark barcodes from the camera.** The live search reads light 2D codes (QR, Data Matrix, Aztec, PDF417, MaxiCode), but not a light 1D barcode on a dark background. A photo of it on the page *File* gets the thorough search.
- **Shared mode.** If another program holds the camera, Lupe may get it only in shared read-only mode with that program's format, or not at all.
- **Typing from the camera** works like a handheld scanner into the program in front (*Type*, off by default). Line breaks inside a code are typed as spaces (from every source, see [Typing](#typing)), so a code held in front of the camera cannot press <kbd>Enter</kbd> in that program – for example run a command in a terminal while Lupe works unattended. The key after typing (<kbd>Enter</kbd> or <kbd>Tab</kbd>, if set) is still sent; multi-line contents come out complete via copying.

## Screen scan and shortcut

- **No screen scan while Windows refuses the capture** – locked desktop, sign-in screen, no visible session. Lupe says so (*Screen scan not possible*) instead of showing a black still image.
- **The shortcut can be taken.** If another program already uses *Win+Shift+Q*, Lupe cannot register it and says so; choose another combination.
- **Shortcuts with <kbd>Ctrl</kbd>+<kbd>Alt</kbd>** collide with <kbd>AltGr</kbd> on German keyboards. Lupe re-types the AltGr character, but a shortcut with <kbd>Win</kbd> is safer ([details](settings.md#screen-scan)).
- **One Lupe per Windows session.** A second start only brings the first window to the front.

## Typing

- **Elevated windows.** Windows does not allow a normal program to type into a program running as administrator. Lupe detects it, types nothing and says so. Start Lupe as administrator too if you need it ([details](installation.md#running-as-administrator)).
- **Line breaks become spaces.** A code – held in front of the camera or on a web page caught by the screen scan – must not be able to press <kbd>Enter</kbd> in the program in front. Copy multi-line contents instead.
- **Typing stops when the window changes.** If another window comes to the front while Lupe is typing, it stops and says so instead of typing the rest into that window.
- **Control characters are dropped.** Tabs become <kbd>Tab</kbd>; everything else below code 32 – for example the group separator `0x1D` in GS1 codes – cannot be typed. The command line shows it as `\x1D`; the clipboard keeps it.
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
- **No file name in the output lines** – call `read` once per image if you need it ([example](command-line.md#examples)).


## Linux and macOS

- **Linux: automated tests in containers.** CI tests the Linux backend in containers: X11 under Xvfb (screen capture, XTest typing, `XGrabKey`) and Wayland in a headless sway with `xdg-desktop-portal-wlr` (screenshot portal). Lupe 1.0.0 was also tested on Linux; the KDE and GNOME portals and multi-monitor or HiDPI setups are not covered by the automated tests.
- **macOS only partly tested.** On a real Mac (Apple Silicon), Lupe 1.0.0 has been started with its window, the demo camera and reading images from the command line, also the Intel build under Rosetta; the screen scan, typing, the shortcut and a real camera have not been tried there yet. Its logic tests pass on CI.
- **Wayland: no foreground window.** Wayland does not tell programs which window is in front. Lupe cannot bring back “the window you came from”; it types into whatever has the focus after the selection closes.
- **Wayland: everything through portals.** Screen scan (Screenshot portal), typing (RemoteDesktop portal) and the shortcut (GlobalShortcuts portal) need `xdg-desktop-portal` with a backend for the desktop. The desktop asks for permission and may pick other keys for the shortcut. Without the GlobalShortcuts portal (GNOME before 48, some wlroots compositors) there is no global shortcut – Lupe says so in the settings; a shortcut of the desktop itself can run `lupe --screen`, and the tray menu and the window still start the scan. sway with `xdg-desktop-portal-wlr` has no RemoteDesktop portal, so *Type* reports that and only copies. The window runs through XWayland (Avalonia draws with X11); with XWayland switched off Lupe cannot open a window. Characters that are not on the active keyboard layout may not arrive through the RemoteDesktop portal, depending on the desktop – use *Copy* there.
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
