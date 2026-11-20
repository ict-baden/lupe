# Changelog

All notable changes to Lupe. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versions follow [Semantic Versioning](https://semver.org/). The release notes of each version are its section below.

## [Unreleased]

### Fixed
- Linux camera: if the camera opened but failed to start streaming (e.g. `EBUSY` or too little USB bandwidth), Lupe kept the device open, and every *Try again* reported the camera as in use by another program. The device is now released right away.
- macOS camera: a stop from within a camera event could close a session started right after it, so the preview froze without an error. The stop now closes only its own session.
- macOS screen scan: with a Retina display next to a 1x display, the selection overlay on the 1x display was twice as large and its right and lower half could not be selected. Not yet tried on a real Mac.
- X11: holding the shortcut fired it again about every 30 ms; it now fires once per press.
- Camera page: without permission for the camera (macOS, Linux user not in the group `video`), the page said to connect a webcam. It now shows the reason, with a button to the system settings where there is one.
- Notification after a screen scan: the tip “drag the rectangle a little larger” appeared also when the screenshot failed or the recognition did not load; it now only appears for “No code found”. A failed copy no longer shows a green check mark.
- File page: the heading, the summary and *Copy all* of the previous image stayed visible while the next one was decoded, and for good if decoding failed.
- Settings: *After typing* shows the key names in the interface language (as the shortcut does) instead of always “Enter” and “Tab”.

### Changed
- Avalonia 12.1.6 and FlashCap 1.13.0. Tmds.DBus.Protocol stays at 0.94.1, the version Avalonia's Linux parts are built against, so the app loads only one copy; `THIRD-PARTY-NOTICES.txt` names the shipped versions.
- CI: in a repository without a release of its own, the MSI update test uses the MSIs of earlier versions from the pre-release `altversionen`; a tag without any earlier version to update from fails instead of passing with a warning.

## [0.1.2] – 2026-10-22

### Fixed
- Windows: an MSI update from a build of the pre-release `latest` to the release of the same version could keep the old `Lupe.exe`: both had the same file version (`x.y.z.0`), and Windows Installer only replaces a file with a higher version. Lupe's own program files now carry the build number in their file version in every build, releases included (e.g. `0.1.2.70`), and the MSI also replaces files of an equal version (relevant for bundled libraries whose content changes but whose version does not). Updates to 0.1.1 were not affected; an update from a `latest` build to 0.1.0 kept that build's program file, which had the same code.
- Release run: a tag for a pre-release (e.g. `v1.0.0-rc1`) would have failed to create its release – PowerShell passed `--prerelease` to `gh` letter by letter. Not used so far.

### Changed
- CI tests the MSI as a fresh installation, as an update of the newest build in `latest` and as an update of the last version: exactly one installed product, every file installed with the checksum of the new build, `Lupe.exe` with the new file version.
- CI checks every PowerShell step of the workflow and every script before Lupe is built – also the steps that only run for tags – for parser errors and for typographic quotation marks that end a text.

## [0.1.1] – 2026-10-21

### Changed
- Command line in English, Spanish, French and Chinese: the help, its hints and the *About* page show the English keywords (`read`, `--help`, `--background`, `--screen`, `--theme light|dark|both`); the German interface keeps `lesen`, `--hilfe` and so on. Both work in every language, as before.
- *About* page and `--info`: *Data and privacy* opens the privacy notice for Lupe on ict-baden.de (https://ict-baden.de/datenschutz/lupe/), *Website* the product page (https://ict-baden.de/produkte/lupe/).
- MSI installer in English (Start menu description, messages). It replaces an installed 0.1.0 although that was a German package; CI now installs the MSI of the previous version first and checks that the new one replaces it.
- Linux: `install.sh` also takes `--remove` (besides `--entfernen`), refuses unknown arguments instead of installing, and reports in English unless the system language is German. The `.deb` names the product page as its homepage and no longer GitHub's address as its maintainer.
- Release notes are the section of this changelog for the version (a pre-release without its own section uses the one of its version or *Unreleased*); a tag without a section stops the release run right at the start. Their links point to the documentation of that version. The changelog is now in English.

### Fixed
- Camera loop: a handler of *Detected* that stopped and synchronously restarted the loop while the first `StartAsync` was still finishing could deadlock – seen once as a hanging test on Windows CI. `StartAsync` no longer waits for a running detection; only `StopAsync` does – now also when the loop had already stopped after a failure during the start, so no hit is reported after `StopAsync` returns. Lupe itself was not affected: it never restarts the camera synchronously from an event.
- Documentation: in the workflow “Scan from the screen, type into the ERP”, line breaks are typed as spaces (it still said <kbd>Enter</kbd>); the statements about the release status and about tests on a real Mac are up to date.

## [0.1.0] – 2026-10-21

First tagged version. Before, there were only builds in the release `latest` (`0.1.0-ci.N`).

### Added
- Reading codes from the webcam, a screen region (Win+Shift+Q, on the Mac ⌘⌥Q), image files and the clipboard, decoded with zxing-cpp.
- Copy the result, type it into the program in front or open it as a link through a link rule; the camera works like a hand scanner.
- History with CSV export, settings as JSON, autostart, notification-area icon.
- Command line with documented exit codes, `--demo` and `--screenshots`.
- Windows, Linux (X11 and Wayland, x64 and arm64) and macOS (Apple Silicon and Intel), user interface with Avalonia.
- Packages: MSI, single-file exe, framework-dependent ZIP, `.deb`, `tar.gz`, `Lupe.app`; `SHA256SUMS.txt` in every release.
- Interface in English, German, Spanish, French and Chinese (simplified), following the system language and switchable. Spanish, French and Chinese are machine translations, not yet reviewed by native speakers.
- *About* page with version, build, licence, third-party licences, website, legal notice (Impressum), privacy and support; the licence opens locally instead of in the browser.
- `lupe --version` and `lupe --info` (version, licence, links); English names for commands and switches (`read`, `--background`, `--screen`, `--help`, `--theme light|dark|both`). The help and the *About* page name the command of each system (`Lupe.exe`, `lupe`, on macOS the program inside `Lupe.app`) and the image formats it can read there.

### Changed
- The Linux package is now called `ictbaden-lupe` (was `lupe`) and replaces the old package on installation.
- Every package contains `LICENSE.txt`; the `.deb` has a `copyright` file.
- Package metadata in English by default: the Linux menu entry (with de, es, fr, zh_CN), the `.deb` description, `README.txt` next to `LIESMICH.txt` in the tar.gz; the macOS camera prompt in all five languages; `CFBundleVersion` numbers only.
- CI checks every bundled native library (glibc, minimum macOS version, VC++ runtime) and reads a code with every package – on Linux also on Ubuntu 20.04/22.04 and Debian 12, on Windows also after an MSI installation.
- macOS: signing with a Developer ID and notarisation are prepared and run as soon as the credentials are in the secrets.
- Typing: line breaks in a code are typed as spaces for the camera and the screen scan – a code in front of the camera or on a web page can no longer press <kbd>Enter</kbd> in the program in front. The key chosen to follow the typing still comes.
- Typing: if another window comes to the front while typing, Lupe stops and says so instead of typing the rest there.
- Language selection: Spanish, French and Chinese are marked as machine-translated.
- Release notes in English; documentation and screenshots show the English interface (German screenshots in `docs/assets/screenshots/de`). Legal notice at https://ict-baden.de/impressum/, support and security contact through the contact page.
- Linux and macOS: settings and history are readable only by the own user (folders 0700, files 0600). A CSV export still follows the umask; an overwritten file keeps its permissions.
- Command line: C1 control characters (`\xHH`), the line separators U+2028/U+2029 and characters that change the text direction (`\uXXXX`) are escaped as well; `--help` describes it.
- Linux: the documentation names glibc 2.31 (Ubuntu 20.04, Debian 11) as the minimum – the limit CI checks and tests for every library; the `.deb` needs ICU 66 or later.
- Release: `Lupe-sbom.cdx.json`, a software bill of materials (CycloneDX) of all components.
- History: wider time column (the date was cut off).

### Fixed
- Linux: on Debian 12, Ubuntu 22.04 and other systems with glibc below 2.38, every scan reported “No code found”, then Lupe crashed. The native zxing-cpp library is now built in-house (glibc 2.28, x64 and arm64), and the `.deb` declares the glibc version it needs.
- macOS: the zxing-cpp library required macOS 26 (Apple Silicon) or 15 (Intel); now macOS 12, like the app.
- Windows: the zxing-cpp library needed the VC++ runtime, which no package shipped; it is now linked statically.
- Linux: the hit sound was written under a fixed name to the shared /tmp, where another user could plant a symlink that Lupe then overwrote. It now goes to a new folder of its own. Likewise, the socket for “one Lupe per session” lives in the user's runtime folder instead of /tmp and only accepts connections of the same user.
- Starting without culture data (`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`) failed (“… is an invalid culture identifier”) at start, with `--help` and when building the main window. Lupe now starts with the English interface in that case; tests start Lupe in this mode.
- `THIRD-PARTY-NOTICES.txt` complete: ANGLE, MicroCom, Avalonia.Native, the WiX custom action of the MSI, the notices of the SkiaSharp/HarfBuzzSharp native packages and of the .NET runtime in full instead of a link, Windows SDK licence corrected; CI checks every shipped file.
- Linux and macOS: an error while accepting a signal (for example from another user at the socket) ended the listening for “one Lupe per session”; after that, `--screen` no longer arrived until a restart.
- If the recognition library cannot be loaded, Lupe now reports an error with the code `LUP-0001` (command line: exit code 3; interface: a hint at start and on every scan) instead of “No code found”, and no longer crashes.
