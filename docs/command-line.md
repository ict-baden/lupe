<sub><a href="../README.md">Lupe</a> › <a href="../README.md#documentation">Documentation</a> › Command line</sub>

# Command line

The same `Lupe.exe` that opens the window also reads image files without one: given the command `lesen` (read), it prints one line per code and ends with an exit code – for scripts, scheduled tasks and folders full of scans. Messages follow the interface language (English, German, Spanish, French or Chinese – the system language or `LUPE_LANGUAGE`); the output lines (`FORMAT<Tab>content`) are the same in every language. On Linux the command is `lupe` (the `.deb` and `install.sh` create it), on macOS `/Applications/Lupe.app/Contents/MacOS/Lupe` – see [Linux and macOS](#linux-and-macos).

**Contents:** [Overview](#overview) · [lesen](#lesen) · [Output](#output) · [Messages and exit codes](#messages-and-exit-codes) · [Waiting for the end](#waiting-for-the-end) · [Examples](#examples) · [Encoding](#encoding) · [Linux and macOS](#linux-and-macos) · [Start switches](#start-switches)

## Overview

```text
Lupe.exe                              start the window
Lupe.exe lesen <image> [<image> …] [--format <format>]
Lupe.exe --hintergrund                start in the notification area only (for autostart)
Lupe.exe --bildschirm                 scan a part of the screen right away (for a desktop shortcut)
Lupe.exe --demo                       window with demo camera and sample history
Lupe.exe --screenshots <folder> [--theme hell|dunkel|beide]
Lupe.exe --hilfe                      help (also --help, -h, -?, /?, /h, hilfe)
```

English names work as well: `read` for `lesen`, `--help`, `--background` for `--hintergrund`, `--screen` for `--bildschirm`, and the theme values `light`, `dark`, `both` for `hell`, `dunkel`, `beide`. `--version` prints the version (for bug reports), `--info` the version, licence, website, legal notice (Impressum), privacy and support links. Commands and options are not case-sensitive. An unknown command or option ends with an error message and exit code 3, for example:

```text
Fehler: Unbekannter Befehl „frobnicate“.
Hilfe: Lupe.exe --hilfe
```

The command line works on its own: it does not need a running Lupe, does not touch the settings or the history, and runs no actions – nothing is copied, typed or opened.

## lesen

```bat
Lupe.exe lesen etikett.png
Lupe.exe lesen C:\Scans\*.jpg D:\Eingang\lieferschein.png
Lupe.exe lesen C:\Scans\*.png --format QR_CODE,EAN_13
```

**Images.** Any number of files. Each is read like on the page *Datei*: PNG, JPEG, BMP, GIF, TIFF, ICO, JPEG XR and, with the matching Windows extension, WebP, HEIC/HEIF, AVIF and JPEG XL – see [How it works › Image files](how-it-works.md#image-files).

- **Wildcards** `*` and `?` are allowed in the file name, not in folder names: `C:\Scans\*.jpg`. The folder is not searched recursively; matching files are read in alphabetical order.
- **Environment variables** such as `%USERPROFILE%\Scans\*.png` are expanded by Lupe itself, so they also work in PowerShell.
- **A folder** is not accepted – give a pattern instead: *„C:\Scans“ ist ein Ordner – bitte Bilder angeben, z. B. C:\Scans\\\*.png*.

**`--format <format>`** – search only these formats. Several can be given with commas, semicolons or by repeating the option; `--format=QR_CODE` works as well. Upper/lower case, hyphens and underscores do not matter (`qr-code`, `EAN13`); `qr`, `microqr` and `rmqr` stand for `QR_CODE`, `MICRO_QR_CODE` and `RMQR_CODE`. The names are the ones in the [format table](../README.md#supported-formats). An unknown name stops the call before any image is read:

```text
Fehler: Unbekanntes Format „foo“. Möglich: QR_CODE, DATA_MATRIX, AZTEC, PDF_417, … TELEPEN, DX_FILM_EDGE.
Hilfe: Lupe.exe --hilfe
```

Without `--format`, all formats are searched. The format selection in the app's settings does not apply here.

**Search.** Every image gets the thorough search of the app – stage 2 if stage 1 finds nothing, on all cores ([details](how-it-works.md#decoding)). <kbd>Ctrl</kbd>+<kbd>C</kbd> cancels cleanly with *Abgebrochen.* and exit code 3.

## Output

One line per code on **stdout**: the format name, a tab, the content.

```text
QR_CODE	https://example.org/lieferung/4711
CODE_128	LS-2026-004711
EAN_13	4012345000009
DATA_MATRIX	SN:LUP-000815;LOT:2610A
```

- **Per image**, codes come in reading order – top to bottom, left to right – and each content appears once per format. Images are processed in the order given; a pattern expands alphabetically.
- **No file name** in the line. If you need to know which image a code came from, call `lesen` once per image – see [Examples](#examples).
- **Control characters** are escaped so that each code stays on one line: `\n` (line feed), `\r`, `\t` (tab) and `\xHH` for the others, including the C1 range U+0080–U+009F – e.g. `\x1D`, the group separator in GS1 codes, or `\x85` (NEL). Line and paragraph separators (U+2028, U+2029) and the characters that change the writing direction in a terminal (U+200E, U+200F, U+061C, U+202A–U+202E, U+2066–U+2069) appear as `\uXXXX`. A backslash in the content stays as it is, so Wi-Fi codes like `WIFI:S:Netz\;…` and paths remain readable.
- **Hints and errors** go to **stderr**, so stdout can be processed as it is.

## Messages and exit codes

On stderr, one line each:

| Message | Meaning |
|---|---|
| *Kein Code gefunden: …* | The image was read, but contains no code (a hint, not an error). |
| *Datei nicht gefunden: …* | The file does not exist. |
| *Keine Datei passt zu „…“.* | A wildcard pattern matched nothing. |
| *Ordner nicht gefunden: …* | The folder of a pattern does not exist. |
| *„…“ ist ein Ordner – bitte Bilder angeben, z. B. …* | A folder was given instead of images. |
| *„…“ ließ sich nicht lesen: …* | The file could not be read as an image – the reason follows, e.g. incomplete, missing codec, locked by another program. |
| *Fehler: …* followed by *Hilfe: Lupe.exe --hilfe* | Wrong call: unknown command, option or format, or no image given. |
| *Abgebrochen.* | Cancelled with <kbd>Ctrl</kbd>+<kbd>C</kbd>. |
| *LUP-0001: …* | The recognition library (zxing-cpp) cannot be loaded on this system – Lupe stops at once instead of reporting *no code found* for every image. See [Error codes](error-codes.md#lup-0001). |

| Exit code | Meaning |
|---|---|
| `0` | at least one code found, and every image could be read |
| `1` | no code found in any image, and every image could be read |
| `3` | error – an image is missing or unreadable, a pattern matched nothing, wrong call, cancelled, or the recognition cannot run on this system (`LUP-0001`). **Also when codes were found in other images**; their lines are printed anyway. |

## Waiting for the end

`Lupe.exe` is a Windows (GUI) program. Started from a command prompt it attaches to that console for its output, but the prompt does not wait for it – the prompt can appear before Lupe's lines, and `%ERRORLEVEL%` or `$LASTEXITCODE` are not yet set. To wait:

| Where | Command |
|---|---|
| cmd, output in the console | `start /wait Lupe.exe lesen …` |
| PowerShell | `Lupe.exe lesen … \| Out-Default` |
| `.cmd` or `.bat` file | nothing needed – cmd waits by itself, and `%ERRORLEVEL%` is Lupe's exit code |
| Task Scheduler | nothing needed – it waits and records the exit code as *Last Run Result* |

> [!WARNING]
> **Do not combine `start /wait` with a redirection.** In `start /wait Lupe.exe lesen … > codes.txt` the redirection belongs to `start`, which does not pass it on to Lupe – the file stays empty. Redirect without `start`: in a `.cmd` file that waits anyway; in an interactive prompt the prompt returns at once and the file is complete when Lupe has finished.

## Examples

**All codes of a folder into one file** – in a `.cmd` file, or typed into cmd:

```bat
"C:\Program Files\Lupe\Lupe.exe" lesen C:\Scans\*.jpg --format QR_CODE > codes.txt
```

**One result file per image** – a `.cmd` file that writes `scan-01.txt` next to `scan-01.png` and so on. An image without a code gives an empty file; the hint goes to the console:

```bat
@echo off
rem codes-per-image.cmd
for %%f in (C:\Scans\*.png) do "C:\Program Files\Lupe\Lupe.exe" lesen "%%f" > "%%~dpnf.txt"
```

**Check the result in a script** – the exit code tells found, nothing or error:

```bat
@echo off
rem check.cmd IMAGE - writes IMAGE.txt, errors to IMAGE.errors.txt
"C:\Program Files\Lupe\Lupe.exe" lesen "%~1" > "%~dpn1.txt" 2> "%~dpn1.errors.txt"
if %ERRORLEVEL%==0 echo Codes found.
if %ERRORLEVEL%==1 echo No code in the image.
if %ERRORLEVEL%==3 echo Error, see "%~dpn1.errors.txt".
```

## Encoding

Lupe chooses the encoding per destination, separately for stdout and stderr, so that umlauts and other characters arrive intact:

| Output goes to | Encoding |
|---|---|
| a console window | Unicode, without changing the console's code page |
| a file | UTF-8 (`> codes.txt`, `>> codes.txt`, `2> errors.txt`); a byte order mark only if the file is still empty when the first line is written – so appending and `> log.txt 2>&1` stay clean, and a file nothing was written to stays empty |
| a pipe | the code page of the calling console (`\| more`, `$x = & Lupe.exe …`): UTF-8 if it is set to 65001, otherwise that code page (usually OEM 437 or 850). Typographic characters are replaced by similar ASCII ones (`„“` → `"`, `–` → `-`, `…` → `...`), anything else the code page lacks by `?` |

Known pitfalls:

- **A pipe can lose characters.** Content outside the console's code page – Chinese text, emoji, many symbols – arrives as `?`. Redirect into a file for lossless UTF-8, or switch the console to UTF-8 first (`chcp 65001`).
- **PowerShell 7.4 and later** pass the bytes of `& Lupe.exe … > codes.txt` through unchanged. With an OEM console the file then contains OEM text. Use the redirection of cmd (e.g. in a `.cmd` file), or set `[Console]::OutputEncoding = [Text.Encoding]::UTF8` first.
- **`> NUL` does not silence Lupe.** The NUL device looks like a console, so the output still appears in the console window.

## Linux and macOS

Everything above applies, with these differences:

| | Linux | macOS |
|---|---|---|
| Command | `lupe` – the `.deb` puts it in `/usr/bin`, `install.sh` in `~/.local/bin`; from the unpacked folder `./Lupe` | `/Applications/Lupe.app/Contents/MacOS/Lupe` (there is no `lupe` on the `PATH`; an alias helps: `alias lupe=/Applications/Lupe.app/Contents/MacOS/Lupe`) |
| Image formats | PNG, JPEG, BMP, GIF, WebP and ICO – **no TIFF** | PNG, JPEG, HEIC, TIFF, GIF, BMP, WebP and whatever else ImageIO reads |
| Waiting, encoding | The shell waits for Lupe; output is UTF-8 – nothing of [Waiting for the end](#waiting-for-the-end) or the code-page notes applies | same as Linux |
| Help | `lupe --hilfe` shows the commands, examples and image formats for the system it runs on | same |

```sh
lupe lesen ~/Scans/*.jpg --format QR_CODE > codes.txt
case $? in 0) echo found ;; 1) echo nothing ;; *) echo error ;; esac
```

## Start switches

Each of these stands alone; combined with anything else, Lupe answers with an error and exit code 3.

| Switch | Does |
|---|---|
| `--hintergrund` | Starts Lupe in the notification area without a window – used by *Mit Windows starten*. If Lupe already runs, it ends without doing anything. If there is no notification area, the window opens anyway. |
| `--bildschirm` | Starts a screen scan straight away. If Lupe already runs, that Lupe scans and the new start ends. Meant for a shortcut set up in the desktop's own settings where programs may not register global shortcuts (Wayland without the GlobalShortcuts portal, e.g. sway: `bindsym $mod+Shift+q exec lupe --bildschirm`). After the scan Lupe stays in the notification area (or opens its window if there is none). |
| `--demo` | Opens the window with a demo camera, a sample history and sample settings. Nothing is saved, no global shortcut is registered, there is no notification-area icon, and the demo ends with its window. It can run next to a normal Lupe. |
| `--screenshots <folder>` | Renders every page of the demo, the selection overlay and the result notification as PNG files into the folder – light, dark or both (default; choose with `--theme hell\|dunkel\|beide`). Used for this documentation; works without a visible desktop. Exit code 0, or 3 on errors. |

---

<p align="center"><sub>
<a href="installation.md">Installation</a> · <a href="usage.md">Using the app</a> · <a href="settings.md">Settings</a> · <a href="link-rules.md">Link rules</a> · <b>Command line</b> · <a href="how-it-works.md">How it works</a> · <a href="data-and-privacy.md">Data &amp; privacy</a> · <a href="limitations.md">Limitations</a> · <a href="development.md">Development</a><br>
<a href="../README.md">← Back to Lupe</a>
</sub></p>
