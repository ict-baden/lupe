<sub><a href="../README.md">Lupe</a> › <a href="../README.md#documentation">Documentation</a> › Limitations</sub>

# Limitations

What Lupe cannot do, what was not tested the way it will be used, and the pitfalls worth knowing – stated plainly.

**Contents:** [Status](#status) · [Not tested on real hardware](#not-tested-on-real-hardware) · [Decoding](#decoding) · [Camera](#camera) · [Screen scan and shortcut](#screen-scan-and-shortcut) · [Typing](#typing) · [History and files](#history-and-files) · [Command line](#command-line)

## Status

- **Pre-release 0.1.0.** Builds come from GitHub Actions; there is no stable release yet.
- **Not code-signed.** SmartScreen may warn about `Lupe.exe` and the MSI.
- **German only.** User interface and command-line output are German.
- **Windows x64 only**, Windows 10 1809 or later.
- **No licence yet.** The licence has not been decided; the repository contains no licence file.

## Not tested on real hardware

- **No webcam during development.** The camera code – opening, format choice, frame conversion, error handling – is covered by tests against a **simulated frame source** and by tests for the error classification. Tests that open a real camera exist, but skip themselves when Windows reports no video capture device, which was the case on the development machine and on CI. Real cameras, drivers and their quirks have not been tried yet.
- **No interactive desktop in the build environment.** Screen capture, the global shortcut and real typing have tests that need a visible, active desktop. They skip themselves in the build environment and on the CI runner and are meant to be run on a normal desktop. What works without a desktop is tested: copying pixels from an in-memory drawing surface, the overlay over the demo desktop, the typing sequence against a simulated keyboard, and the shortcut logic against a simulated Windows API.
- **The real-typing tests type into a standard Windows text box** (a Win32 edit control); they only run on a visible desktop and have not run in the build environment. Programs that read raw keyboard scan codes instead of characters – some games, some remote-desktop or terminal clients – may not accept Lupe's Unicode input; use *Kopieren* there.

## Decoding

- **The thorough second stage only runs when the first found nothing.** If a picture contains one easy and one hard code – very small, blurred, slanted – the easy one can stop the search before the hard one is tried. Select or crop the hard code on its own.
- **MaxiCode only from clean images.** ZXing's MaxiCode reader has no detector that searches a larger picture: the code has to be practically alone on a plain background, e.g. a cropped image.
- **Shift_JIS content** (QR codes with Japanese Kanji) may be decoded wrongly – ZXing.Net has no Shift_JIS code page under .NET and falls back to UTF-8. Lupe's repair only covers ISO-8859-1 text that ZXing mistakes for Shift_JIS.
- **Intelligent Mail (`IMB`) is enabled but untested.** ZXing.Net has a reader for it, but there is no test image for this format.
- **Code 39** is read as printed: a check digit is not verified, Full ASCII sequences are not expanded.
- **EAN/UPC add-ons** (2 or 5 digits) are not part of the result.
- **GS1 DataBar Expanded** is read in a single row only, not stacked.
- **Not supported at all:** MSI, Pharmacode (phantom codes) and Plessey (ZXing.Net cannot read it) – see [How it works › Phantom-code protection](how-it-works.md#phantom-code-protection).
- **Very large pictures** are reduced to 40 megapixels while loading. Codes with very fine modules in huge photos can be lost by that.

## Camera

- **Fixed resolution.** Lupe picks a camera format near 1280 × 720 and searches at most that. A small code far away from the camera needs to come closer.
- **No camera controls.** Lupe does not set focus, zoom, exposure or a light; it uses what the driver does by itself.
- **Live search only.** The camera uses the fast first stage; the thorough search with enlarged and rotated versions is reserved for the screen scan and files.
- **No light-on-dark barcodes from the camera.** The live search reads inverted codes with the 2D readers only; a light 1D barcode on a dark background is not found there. A photo of it on the page *Datei* gets the thorough search.
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

---

<p align="center"><sub>
<a href="installation.md">Installation</a> · <a href="usage.md">Using the app</a> · <a href="settings.md">Settings</a> · <a href="link-rules.md">Link rules</a> · <a href="command-line.md">Command line</a> · <a href="how-it-works.md">How it works</a> · <a href="data-and-privacy.md">Data &amp; privacy</a> · <b>Limitations</b> · <a href="development.md">Development</a><br>
<a href="../README.md">← Back to Lupe</a>
</sub></p>
