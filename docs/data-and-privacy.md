<sub><a href="../README.md">Lupe</a> › <a href="../README.md#documentation">Documentation</a> › Data &amp; privacy</sub>

# Data & privacy

What Lupe looks at, what it keeps where, and how to get rid of it. In short: pictures stay in memory, results stay on your computer, and nothing goes to the internet. The main part describes Windows; [Linux and macOS](#linux-and-macos) has the paths and the few differences there.

**Contents:** [What Lupe looks at](#what-lupe-looks-at) · [What is stored where](#what-is-stored-where) · [The history file](#the-history-file) · [CSV export](#csv-export) · [Clipboard and typing](#clipboard-and-typing) · [Registry](#registry) · [Network connections](#network-connections) · [Removing everything](#removing-everything) · [Linux and macOS](#linux-and-macos)

## What Lupe looks at

| Source | When | Kept |
|---|---|---|
| Camera | only while the page *Camera* is visible and the camera is started | frames in memory only, never saved |
| Screen | only after the shortcut, the button or the menu entry *Scan region* | one still image of all monitors in memory, discarded after the scan; only the rectangle you draw is searched |
| Image files | files you open, drop or pass to `read` | read only, never modified |
| Clipboard | only when you click *From clipboard* or press <kbd>Ctrl</kbd>+<kbd>V</kbd> in Lupe | read only |

Lupe **never keeps a picture** – no camera frame, no screenshot, no copy of a file. On Windows and X11 the screenshot never touches the disk. On **macOS** and under **Wayland** the system hands the screenshot over as a PNG file (macOS: `screencapture` into the user's temporary folder; Wayland: the desktop portal, often in `~/Pictures`), which Lupe reads and deletes right away. (The developer switch `--screenshots` writes images of the built-in demo, not of your screen.)

## What is stored where

Everything lives **per user** in `%LocalAppData%\Lupe` – usually `C:\Users\<name>\AppData\Local\Lupe`. The folder inherits the permissions of your profile: you, *SYSTEM* and the administrators of the PC. *About › Your data › Open folder* opens it.

| File | Content |
|---|---|
| `einstellungen.json` | the settings, including your link rules and the identifier of the chosen camera – see [Settings › The settings file](settings.md#the-settings-file) |
| `verlauf.json` | the history, if *Save history* is on – see below |
| `einstellungen.json.defekt-YYYYMMDD-hhmmss` | only if a broken settings file had to be replaced – the unchanged old file |
| `*.tmp` | for a moment while a file is written atomically; removed afterwards |

Earlier pre-release versions kept two options in `oberflaeche.json`; Lupe moves them into the settings and deletes that file.

Each Windows user has their own settings and history. The demo (`--demo`) saves nothing at all; the command line reads no settings and writes no history.

## The history file

`verlauf.json` holds, per scanned code, the time (with the time-zone offset), the content, the format, the source and what was done with it – newest first, UTF-8:

```json
{
  "Version": 1,
  "Entries": [
    {
      "Time": "2026-10-14T10:36:00+02:00",
      "Text": "https://example.org/rechnung/2026-118",
      "Format": "QR_CODE",
      "Source": "Bildschirm",
      "ActionsDone": "Kopieren, Eintippen"
    }
  ]
}
```

It does **not** contain pictures, file names, window titles or the name of the program you typed into.

- **Size:** as set under *At most … entries* (default 1,000, up to 10,000); older ones drop out.
- **Writing:** after every change the whole file is written anew – to a temporary file, flushed to disk, then swapped in. A crash or a full disk never leaves half a file.
- **Switched off** (*Save history*): after a confirmation the file is **deleted**, because scanned contents can be confidential. The entries stay visible until Lupe quits. If the file is locked at that moment, Lupe says so and tries again at the next start; until then, delete it by hand.
- **Deleting entries:** single entries with *Delete* or <kbd>Del</kbd> on the page *History*, all of them with *Delete all*. The file is rewritten immediately.
- **Broken file** (not valid JSON): the history starts empty and Lupe says so. The file stays untouched until the next code is added – **then it is replaced**, without a backup. Single unreadable entries are skipped; the others are kept.
- **Locked file** (another program, no permission): Lupe does not overwrite it. At the next change it reads it again and merges.
- **Cannot be saved** (disk full): the entries stay in memory until Lupe quits, and a warning says why.

## CSV export

*History › Export as CSV …* writes the whole history – independent of the search box – as a CSV file the way Excel in German-speaking countries expects it – with German column names and source names, whatever the language of the interface: UTF-8 with byte order mark, semicolons, Windows line ends.

```text
Zeit;Format;Quelle;Inhalt
14.10.2026 10:40:00;CODE_128;Kamera;"'+41 56 000 00 00; ""Test"""
14.10.2026 10:36:00;QR_CODE;Bildschirm;https://example.org/rechnung/2026-118
```

| Column | Content |
|---|---|
| `Zeit` | local time, `dd.MM.yyyy HH:mm:ss` |
| `Format` | the format name as in `--format`, e.g. `QR_CODE` |
| `Quelle` | `Kamera`, `Bildschirm`, `Datei` or `Zwischenablage` |
| `Inhalt` | the content |

What was done with a code (*ActionsDone*) is not exported.

**CSV injection protection.** A field that starts – also after spaces – with `=`, `+`, `-`, `@`, a tab or a line break (or the full-width forms of these characters) gets an apostrophe `'` in front, so that Excel and LibreOffice show it as text instead of running it as a formula. A QR code with `=HYPERLINK(…)` therefore cannot act when the file is opened. The apostrophe is part of the exported value, as in the example above. Fields containing a semicolon, quotation mark, line break or leading/trailing space are quoted.

## Clipboard and typing

- ***Copy*** puts the content on the Windows clipboard, where it stays after Lupe quits until something else is copied. If Windows' clipboard history (<kbd>Win</kbd>+<kbd>V</kbd>) or clipboard sync across devices is switched on, Windows keeps or syncs it like any other copied text – that is a Windows setting, not Lupe's.
- ***Type*** types the content into another program. Whatever that program does with typed text – save it, send it – happens there.
- ***Open link*** passes the link to your default browser; from then on the browser's privacy rules apply.

## Registry

Lupe reads and writes very little:

| Key | Access | Why |
|---|---|---|
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `Lupe` | read on the settings page; written only when you switch *Start with Windows* on or off | autostart for your user |
| `HKCU\…\CurrentVersion\Themes\Personalize`, value `AppsUseLightTheme` | read | to follow the Windows light/dark setting |
| `…\CapabilityAccessManager\ConsentStore\webcam` (HKLM and HKCU) and the camera policies under `HKLM\SOFTWARE\Policies\Microsoft` | read, only when the camera is refused | to name the Windows switch that blocks the camera |
| `HKLM\SOFTWARE\ICT-Baden\Lupe` | written by the MSI | marker for the Start menu shortcut |

## Network connections

**None.** Lupe contains no network code: no telemetry, no update check, no licence check, no cloud decoding. The only things that reach the internet are links you open – automatically through a link rule with *Open link* ticked, or by clicking *Open link* or *Open in browser* – and those are opened by your browser, not by Lupe.

## Removing everything

1. Switch off *Settings › Start with Windows* (removes the autostart entry).
2. Quit Lupe: notification-area icon › *Quit*.
3. Delete `%LocalAppData%\Lupe` – settings and history.
4. Uninstall the MSI, or delete `Lupe.exe`.

Uninstalling alone leaves steps 1 and 3 undone – see [Installation › Updates and uninstalling](installation.md#updates-and-uninstalling).

## Linux and macOS

What Lupe looks at and what the history contains is the same as on Windows. Settings and history are readable only by your user: new folders are created with mode `0700`, the files with `0600`. A CSV export goes where you choose and is not restricted this way: a new file follows your umask, an existing file you overwrite keeps its permissions.

| | Linux | macOS |
|---|---|---|
| Settings | `~/.config/lupe/einstellungen.json` (or `$XDG_CONFIG_HOME/lupe`) | `~/Library/Application Support/Lupe/einstellungen.json` |
| History | `~/.local/share/lupe/verlauf.json` (or `$XDG_DATA_HOME/lupe`) | `~/Library/Application Support/Lupe/verlauf.json` |
| Keyboard permission (Wayland) | `~/.config/lupe/remotedesktop-token` – the desktop portal's token, so it does not ask again at every start | – |
| Autostart | `~/.config/autostart/lupe.desktop` | `~/Library/LaunchAgents/de.ict-baden.lupe.plist` |
| One Lupe per session | `lupe-<user>.lock` and the socket `lupe-<user>.sock` in `$XDG_RUNTIME_DIR` (without it: the temporary folder) | the same two files in the user's temporary folder |
| Temporary files | the hit sound `treffer.wav` in a new folder `lupe-…` of the temporary folder, removed when Lupe quits; under Wayland the portal's screenshot PNG, deleted after reading | the hit sound as on Linux; the screenshot PNG `lupe-<process>-….png` in the user's temporary folder, deleted after reading |
| Licence texts | if the package has no `THIRD-PARTY-NOTICES.txt` or `LICENSE.txt` next to the program, Lupe writes a copy to `Lupe/` in the temporary folder before opening it | same |

There is no registry. **Removing everything:** switch off autostart in the settings, quit Lupe, delete the folders and files above, then remove the program (`sudo apt remove ictbaden-lupe`, `~/.local/opt/lupe/install.sh --remove` (`--entfernen` for an installation of 0.1.0), or move `Lupe.app` to the bin). On macOS also remove Lupe from *System Settings › Privacy & Security* (Camera, Screen Recording, Accessibility) if you like.

---

<p align="center"><sub>
<a href="installation.md">Installation</a> · <a href="usage.md">Using the app</a> · <a href="settings.md">Settings</a> · <a href="link-rules.md">Link rules</a> · <a href="command-line.md">Command line</a> · <a href="how-it-works.md">How it works</a> · <b>Data &amp; privacy</b> · <a href="limitations.md">Limitations</a> · <a href="development.md">Development</a><br>
<a href="../README.md">← Back to Lupe</a>
</sub></p>
