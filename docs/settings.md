<sub><a href="../README.md">Lupe</a> › <a href="../README.md#documentation">Documentation</a> › Settings</sub>

# Settings

Every setting on the page *Settings*, its default and where it applies – and the settings file behind it.

**Contents:** [Overview](#overview) · [On a detected code](#on-a-detected-code) · [Screen scan](#screen-scan) · [Link rules](#link-rules) · [Formats](#formats) · [Camera](#camera) · [History](#history) · [Startup and notification area](#startup-and-notification-area) · [The settings file](#the-settings-file) · [Rolling out settings](#rolling-out-settings) · [Broken or locked file](#broken-or-locked-file)

## Overview

Changes apply and are saved **immediately**; there is no *Save* button. Settings are per Windows user.

| Section | Setting | Default | Key in the settings file |
|---|---|---|---|
| *On a detected code* | *Camera, file and clipboard* | *Copy* | `Actions` |
| | *Screen region* | *Copy* | `ScreenActions` |
| | *After typing* | *Nothing* | `TypeSuffix` |
| | *Typing speed* | *Normal (20 ms)* | `TypeDelayMs` |
| *Screen scan* | *Keyboard shortcut* | *Win+Shift+Q* | `ScreenHotkey` |
| *Link rules* | rule table | one rule: `^https://\S+$`, no prefix | `LinkRules` |
| *Formats* | *Recognize all formats* | on | `Formats` |
| *Camera* | *Sound on detection* | on | `Sound` |
| | *Aim guide* | on | `AimHelp` |
| | *Repeat lock* | 3 seconds | `RepeatSuppressMs` |
| | camera (chosen on the page *Camera*) | the first camera | `Camera` |
| *History* | *Save history* | on | `KeepHistory` |
| | *At most … entries* | 1,000 | `HistoryLimit` |
| *Startup and notification area* | *Start with Windows* | off | – (Windows registry, see below) |
| | *Keep running in the notification area when closed* | on | `KeepRunningInTray` |

## On a detected code

What happens with a recognised code. The actions are set **separately for two groups of sources**:

| Group | Offers | Default |
|---|---|---|
| ***Camera, file and clipboard*** | *Copy*, *Type* (camera only), *Open link* | *Copy* |
| ***Screen region*** | *Copy*, *Type*, *Open link* | *Copy* |

- ***Copy*** puts the content on the clipboard.
- ***Type*** writes the content as keyboard input. For the **screen scan** it goes into the window that was active before the scan – only when the scan was started with the shortcut from another program ([details](usage.md#the-screen-scan)). For the **camera** Lupe works like a hand scanner and types into the program that is in front when the code is recognised; Lupe can run behind it or minimised ([details](usage.md#camera)). A file or the clipboard is never typed – there, Lupe itself is in front.
- ***Open link*** opens the content in the default browser when a [link rule](link-rules.md) matches. Without a matching rule nothing opens. Independent of this setting, every matching result has an *Open link* button.
- No action ticked: the result is shown and added to the history, nothing else.

The actions run for **one** code. When a picture contains several, the camera and the page *File* run no action, and the screen scan asks which code to take.

***After typing*** – a key sent after the typed text: *Nothing*, *Enter* or *Tab*. *Enter* submits a search field or a form, *Tab* moves to the next field.

***Typing speed*** – the pause between two typed characters, like the character delay of a hand scanner: *Instant* (0 ms), *Fast (10 ms)*, *Normal (20 ms)* (default) or *Slow (40 ms)*; in the settings file `TypeDelayMs`, 0 to 500. Notepad on Windows 11 loses characters with *Instant* – it reads a typed character only when it processes it and then sees the last one sent. A 50-character link takes about one second at 20 ms ([details](how-it-works.md#typing)).

## Screen scan

***Keyboard shortcut*** – the global shortcut for the screen scan. Default: *Win+Shift+Q* (<kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>Q</kbd>), built like Windows' own <kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd>.

- **Change it:** click into the field (*Press the combination …*) and press the new combination. While you record, the current shortcut is switched off so that pressing it lands in the field instead of starting a scan. <kbd>Esc</kbd> cancels, <kbd>Tab</kbd> leaves the field. *Default* restores *Win+Shift+Q*.
- **Allowed:** a key together with <kbd>Ctrl</kbd>, <kbd>Alt</kbd> or <kbd>Win</kbd> (<kbd>Shift</kbd> may be added). Alone, only <kbd>F1</kbd>–<kbd>F24</kbd>, <kbd>Print Screen</kbd> and <kbd>Pause</kbd> are accepted – a shortcut on a normal letter would stop you from typing that letter anywhere (*Please use Ctrl, Alt or Win*).
- **Status:** below the field, *… is ready – even when the window is closed.*, or the reason why it could not be registered, typically *… is already in use by another program.* Then choose another combination.

> [!WARNING]
> **Shortcuts with <kbd>Ctrl</kbd>+<kbd>Alt</kbd> collide with <kbd>AltGr</kbd>.** Windows reports <kbd>AltGr</kbd> as <kbd>Ctrl</kbd>+<kbd>Alt</kbd>, so on a German keyboard a global <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Q</kbd> would swallow <kbd>AltGr</kbd>+<kbd>Q</kbd> = `@`. The settings show a warning for such shortcuts (*… may collide with AltGr characters on German keyboards …*), naming the character if the keyboard layout has one on that key. Lupe protects you anyway: if the shortcut arrives while the right <kbd>Alt</kbd> key (<kbd>AltGr</kbd>) is held and the layout of the window in front produces a character for it, Lupe types that character instead of starting a scan. With the left <kbd>Alt</kbd> key, the shortcut works as usual. A shortcut with <kbd>Win</kbd> avoids the question entirely – that is why the default uses it.

## Link rules

*Link rules* – which codes become links and how. Default: one active rule, pattern `^https://\S+$` without prefix: a code that is exactly one `https://` address is its own link. Plain `http://` addresses are not covered by the default rule.

The table has, per rule, *Active*, *Regular expression*, *Prefix (empty = code is the link)*, buttons to move the rule up or down (earlier rules are checked first) and to delete it, and *Add rule* below. *Code to try out* shows the resulting link for any text you type in, or *No rule matches – no link.*

How links are built, examples and the safety checks: **[Link rules](link-rules.md)**.

## Formats

***Recognize all formats*** – on by default. Switched off, all 22 formats are listed, with only the common ones ticked at first: QR Code, Data Matrix, EAN-13, EAN-8, Code 128, Code 39, ITF and UPC-A. At least one format has to stay ticked; ticking all of them is the same as *Recognize all formats*.

Fewer formats mean a faster search and fewer chances for a false hit in pictures with a lot of text (*faster and fewer false detections in images with a lot of text*). The selection applies to the camera, the screen scan, files and the clipboard. The command line has its own option, `--format`, and ignores this setting.

## Camera

| Setting | Default | Meaning |
|---|---|---|
| ***Sound on detection*** | on | A short beep when a code is recognised. Although listed under *Camera*, it applies to every source. The play button next to it (*Play sound*) plays it. |
| ***Aim guide*** | on | Corners in the middle of the camera picture showing where to hold the code. Also switchable directly on the page *Camera*. |
| ***Repeat lock*** | 3 seconds | How long the camera does not report the same code (same content and format) again – counted from the **last** time it was seen. A code that stays in view is therefore reported once. Choices: 1, 2, 3, 5, 10 or 30 seconds, or 1 minute. |
| camera | the first camera | The camera chosen in the list on the page *Camera* is remembered. |

## History

| Setting | Default | Meaning |
|---|---|---|
| ***Save history*** | on | Keeps the history in `%LocalAppData%\Lupe\verlauf.json`, only for you. |
| ***At most … entries*** | 1,000 | Older entries drop out. Allowed: 10 to 10,000. |

**Switching *Save history* off** asks first (*Stop saving the history?*) and then **deletes the file** `verlauf.json` – scanned contents can be confidential and should not stay on disk. The entries remain visible until Lupe quits. If you want to keep them, export them first (*History › Export as CSV …*). If the file cannot be deleted at that moment, Lupe says so and tries again at the next start. Switching it back on writes the entries of the current session into a new file.

## Startup and notification area

| Setting | Default | Meaning |
|---|---|---|
| ***Start with Windows*** | off | Lupe starts at sign-in, invisible in the notification area, with the shortcut ready. Stored as a per-user autostart entry in the Windows registry, not in the settings file – see [Installation › Start with Windows](installation.md#start-with-windows). Not available in the demo. |
| ***Keep running in the notification area when closed*** | on | Closing the window only hides it; Lupe and the shortcut stay available. Quit with *Quit* in the notification-area menu. Switched off, closing the window quits Lupe – and the shortcut is gone. |

## The settings file

Path: `%LocalAppData%\Lupe\einstellungen.json` – shown at the bottom of the page, with *Open folder*. It is created at the first change – closing the window for the first time counts, because Lupe remembers that it has shown the note *Lupe is still running*. With all defaults the file looks like this:

```json
{
  "Actions": "Kopieren",
  "ScreenActions": "Kopieren",
  "TypeSuffix": "",
  "TypeDelayMs": 20,
  "LinkRules": [
    {
      "Pattern": "^https://\\S+$",
      "Prefix": "",
      "Enabled": true
    }
  ],
  "Formats": [],
  "ScreenHotkey": {
    "Ctrl": false,
    "Alt": false,
    "Shift": true,
    "Win": true,
    "Key": 81
  },
  "Sound": true,
  "AimHelp": true,
  "RepeatSuppressMs": 3000,
  "Camera": "",
  "KeepHistory": true,
  "HistoryLimit": 1000,
  "KeepRunningInTray": true,
  "TrayHintShown": false
}
```

| Key | Values |
|---|---|
| `Actions`, `ScreenActions` | `Keine`, or a comma-separated combination of `Kopieren`, `Eintippen`, `LinkOeffnen`, e.g. `"Kopieren, Eintippen"`. `Eintippen` in `Actions` has no effect. |
| `TypeSuffix` | `""`, `"Enter"` or `"Tab"` |
| `LinkRules` | list of `Pattern` (.NET regular expression), `Prefix`, `Enabled` |
| `Formats` | `[]` = all, otherwise names such as `"QR_CODE"`, `"EAN_13"` – see [Supported formats](../README.md#supported-formats). Names Lupe does not know (or no longer reads, such as `IMB`) are dropped when the file is loaded |
| `ScreenHotkey` | `Ctrl`, `Alt`, `Shift`, `Win` and `Key` – a Windows virtual-key code (`81` = Q) |
| `RepeatSuppressMs` | milliseconds, 0 to 3,600,000 |
| `Camera` | the camera's identifier, `""` = the first camera |
| `HistoryLimit` | 1 to 10,000 (the page accepts 10 to 10,000) |
| `TrayHintShown` | internal: the note *Lupe is still running* has been shown once |

Reading is tolerant: missing keys get their default, unknown keys are ignored, a single unusable value falls back to its default without losing the others, and in lists only the unusable elements are dropped. Out-of-range numbers are clamped, unknown format names removed, a shortcut without a usable key (or a plain letter without <kbd>Ctrl</kbd>, <kbd>Alt</kbd> or <kbd>Win</kbd>) is replaced by the default. A link rule with an invalid regular expression stays in the table, marked with the error, but is not applied.

Lupe reads the file when it starts and writes the whole file on every change. **Edit it by hand only while Lupe is not running** – otherwise the next change in the app overwrites your edit. Writing is atomic: Lupe writes a temporary file next to it, flushes it to disk and then replaces the old file, so a crash or a full disk never leaves half a file.

## Rolling out settings

There is no machine-wide or policy configuration: Lupe reads its settings only from `%LocalAppData%\Lupe\einstellungen.json` of the user who runs it, and the MSI installs none. To pre-configure users – link rules, actions, the shortcut – place an `einstellungen.json` there before Lupe starts, for example with a logon script, Group Policy preferences or an Intune script. The file may contain only the keys you want to set; the others get their defaults. Replacing an existing file overwrites the user's own changes. `%LocalAppData%` is not part of a roaming profile, so the file has to be on every PC the user signs in to.

## Broken or locked file

Lupe never silently overwrites a settings file it could not read properly:

| Situation | What Lupe does |
|---|---|
| **Broken** – not valid JSON, or not an object | Starts with the defaults and says so. Before the next save, the file is copied unchanged to `einstellungen.json.defekt-YYYYMMDD-hhmmss` next to it. If that copy fails, nothing is saved. |
| **Partly unusable** – single values of the wrong type | Uses the defaults for those values and names them (*Default value used for: …*). Before the next save, the original is backed up the same way. |
| **Not readable** – locked by another program, no permission | Starts with the defaults but does **not** write the file. Lupe tries to read it again every 5 seconds for about two minutes. Once it is readable, your changes from this session are applied field by field on top of its content; what you did not change stays as it was. A last attempt is made when Lupe quits. If there is no history file yet, the history stays in memory until then – Lupe cannot know whether saving it is allowed. |
| **Cannot be written** – disk full, no permission | The change applies until Lupe quits; the reason is shown. |

Problems appear as *Problem with the settings file* at the top of the page and as a message when Lupe starts.

---

<p align="center"><sub>
<a href="installation.md">Installation</a> · <a href="usage.md">Using the app</a> · <b>Settings</b> · <a href="link-rules.md">Link rules</a> · <a href="command-line.md">Command line</a> · <a href="how-it-works.md">How it works</a> · <a href="data-and-privacy.md">Data &amp; privacy</a> · <a href="limitations.md">Limitations</a> · <a href="development.md">Development</a><br>
<a href="../README.md">← Back to Lupe</a>
</sub></p>
