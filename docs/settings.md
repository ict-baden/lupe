<sub><a href="../README.md">Lupe</a> › <a href="../README.md#documentation">Documentation</a> › Settings</sub>

# Settings

Every setting on the page *Einstellungen*, its default and where it applies – and the settings file behind it.

**Contents:** [Overview](#overview) · [On a hit](#on-a-hit) · [Screen scan](#screen-scan) · [Link rules](#link-rules) · [Formats](#formats) · [Camera](#camera) · [History](#history) · [Start and notification area](#start-and-notification-area) · [The settings file](#the-settings-file) · [Rolling out settings](#rolling-out-settings) · [Broken or locked file](#broken-or-locked-file)

## Overview

Changes apply and are saved **immediately**; there is no *Save* button. Settings are per Windows user.

| Section | Setting | Default | Key in the settings file |
|---|---|---|---|
| *Bei einem Treffer* | *Kamera, Datei und Zwischenablage* | *Kopieren* | `Actions` |
| | *Bildschirmausschnitt* | *Kopieren* | `ScreenActions` |
| | *Nach dem Eintippen* | *Nichts* | `TypeSuffix` |
| *Bildschirm-Scan* | *Tastenkürzel* | *Win+Umschalt+Q* | `ScreenHotkey` |
| *Link-Regeln* | rule table | one rule: `^https://\S+$`, no prefix | `LinkRules` |
| *Formate* | *Alle Formate erkennen* | on | `Formats` |
| *Kamera* | *Ton bei Treffer* | on | `Sound` |
| | *Zielhilfe* | on | `AimHelp` |
| | *Wiederholungssperre* | 3 seconds | `RepeatSuppressMs` |
| | camera (chosen on the page *Kamera*) | the first camera | `Camera` |
| *Verlauf* | *Verlauf speichern* | on | `KeepHistory` |
| | *Höchstens … Einträge* | 1,000 | `HistoryLimit` |
| *Start und Infobereich* | *Mit Windows starten* | off | – (Windows registry, see below) |
| | *Beim Schließen im Infobereich weiterlaufen* | on | `KeepRunningInTray` |

## On a hit

*Bei einem Treffer* – what happens with a recognised code. The actions are set **separately for two groups of sources**:

| Group | Offers | Default |
|---|---|---|
| ***Kamera, Datei und Zwischenablage*** | *Kopieren*, *Link öffnen* | *Kopieren* |
| ***Bildschirmausschnitt*** | *Kopieren*, *Eintippen*, *Link öffnen* | *Kopieren* |

- ***Kopieren*** (copy) puts the content on the clipboard.
- ***Eintippen*** (type) exists only for the screen scan: there, the window that was active before the scan is known, and Lupe types into it. With the camera, a file or the clipboard, Lupe itself is the active window – *Eintippen gibt es hier nicht – Lupe selbst ist dann das aktive Fenster.* Even for the screen scan, typing only happens when the scan was started with the shortcut from another program ([details](usage.md#the-screen-scan)).
- ***Link öffnen*** (open link) opens the content in the default browser when a [link rule](link-rules.md) matches. Without a matching rule nothing opens. Independent of this setting, every matching result has a *Link öffnen* button.
- No action ticked: the result is shown and added to the history, nothing else.

The actions run for **one** code. When a picture contains several, the camera and the page *Datei* run no action, and the screen scan asks which code to take.

***Nach dem Eintippen*** (after typing) – a key sent after the typed text: *Nichts* (nothing), *Enter* or *Tab*. *Enter* submits a search field or a form, *Tab* moves to the next field.

## Screen scan

***Tastenkürzel*** – the global shortcut for the screen scan. Default: *Win+Umschalt+Q* (<kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>Q</kbd>), built like Windows' own <kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd>.

- **Change it:** click into the field (*Kombination drücken …*) and press the new combination. While you record, the current shortcut is switched off so that pressing it lands in the field instead of starting a scan. <kbd>Esc</kbd> cancels, <kbd>Tab</kbd> leaves the field. *Standard* restores *Win+Umschalt+Q*.
- **Allowed:** a key together with <kbd>Ctrl</kbd>, <kbd>Alt</kbd> or <kbd>Win</kbd> (<kbd>Shift</kbd> may be added). Alone, only <kbd>F1</kbd>–<kbd>F24</kbd>, <kbd>Print Screen</kbd> (*Druck*) and <kbd>Pause</kbd> are accepted – a shortcut on a normal letter would stop you from typing that letter anywhere (*Bitte mit Strg, Alt oder Win*).
- **Status:** below the field, *… ist bereit – auch wenn das Fenster geschlossen ist.* (ready, also with the window closed), or the reason why it could not be registered, typically *… ist schon von einem anderen Programm belegt.* (already used by another program). Then choose another combination.

> [!WARNING]
> **Shortcuts with <kbd>Ctrl</kbd>+<kbd>Alt</kbd> collide with <kbd>AltGr</kbd>.** Windows reports <kbd>AltGr</kbd> as <kbd>Ctrl</kbd>+<kbd>Alt</kbd>, so on a German keyboard a global <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Q</kbd> would swallow <kbd>AltGr</kbd>+<kbd>Q</kbd> = `@`. The settings show a warning for such shortcuts (*… kann auf deutschen Tastaturen mit AltGr-Zeichen kollidieren …*), naming the character if the keyboard layout has one on that key. Lupe protects you anyway: if the shortcut arrives while the right <kbd>Alt</kbd> key (<kbd>AltGr</kbd>) is held and the layout of the window in front produces a character for it, Lupe types that character instead of starting a scan. With the left <kbd>Alt</kbd> key, the shortcut works as usual. A shortcut with <kbd>Win</kbd> avoids the question entirely – that is why the default uses it.

## Link rules

*Link-Regeln* – which codes become links and how. Default: one active rule, pattern `^https://\S+$` without prefix: a code that is exactly one `https://` address is its own link. Plain `http://` addresses are not covered by the default rule.

The table has, per rule, *Aktiv* (active), *Regulärer Ausdruck* (regular expression), *Präfix (leer = Code ist der Link)* (prefix, empty = the code is the link), buttons to move the rule up or down (earlier rules are checked first) and to delete it, and *Regel hinzufügen* (add rule) below. *Code zum Ausprobieren* (code to try) shows the resulting link for any text you type in, or *Keine Regel passt – kein Link.*

How links are built, examples and the safety checks: **[Link rules](link-rules.md)**.

## Formats

***Alle Formate erkennen*** (recognise all formats) – on by default. Switched off, all 17 formats are listed, with only the common ones ticked at first: QR Code, Data Matrix, EAN-13, EAN-8, Code 128, Code 39, ITF and UPC-A. At least one format has to stay ticked; ticking all of them is the same as *Alle Formate erkennen*.

Fewer formats mean a faster search and fewer chances for a false hit in pictures with a lot of text (*schneller und weniger Fehlerkennungen bei Bildern mit viel Text*). The selection applies to the camera, the screen scan, files and the clipboard. The command line has its own option, `--format`, and ignores this setting.

## Camera

| Setting | Default | Meaning |
|---|---|---|
| ***Ton bei Treffer*** (sound on hit) | on | A short beep when a code is recognised. Although listed under *Kamera*, it applies to every source. The play button next to it (*Ton anhören*) plays it. |
| ***Zielhilfe*** (aim help) | on | Corners in the middle of the camera picture showing where to hold the code. Also switchable directly on the page *Kamera*. |
| ***Wiederholungssperre*** (repeat suppression) | 3 seconds | How long the camera does not report the same code (same content and format) again – counted from the **last** time it was seen. A code that stays in view is therefore reported once. Choices: 1, 2, 3, 5, 10 or 30 seconds, or 1 minute. |
| camera | the first camera | The camera chosen in the list on the page *Kamera* is remembered. |

## History

| Setting | Default | Meaning |
|---|---|---|
| ***Verlauf speichern*** (save history) | on | Keeps the history in `%LocalAppData%\Lupe\verlauf.json`, only for you. |
| ***Höchstens … Einträge*** (at most … entries) | 1,000 | Older entries drop out. Allowed: 10 to 10,000. |

**Switching *Verlauf speichern* off** asks first (*Den Verlauf nicht mehr speichern?*) and then **deletes the file** `verlauf.json` – scanned contents can be confidential and should not stay on disk. The entries remain visible until Lupe quits. If you want to keep them, export them first (*Verlauf › Als CSV exportieren …*). If the file cannot be deleted at that moment, Lupe says so and tries again at the next start. Switching it back on writes the entries of the current session into a new file.

## Start and notification area

| Setting | Default | Meaning |
|---|---|---|
| ***Mit Windows starten*** (start with Windows) | off | Lupe starts at sign-in, invisible in the notification area, with the shortcut ready. Stored as a per-user autostart entry in the Windows registry, not in the settings file – see [Installation › Start with Windows](installation.md#start-with-windows). Not available in the demo. |
| ***Beim Schließen im Infobereich weiterlaufen*** (keep running in the notification area when closed) | on | Closing the window only hides it; Lupe and the shortcut stay available. Quit with *Beenden* in the notification-area menu. Switched off, closing the window quits Lupe – and the shortcut is gone. |

## The settings file

Path: `%LocalAppData%\Lupe\einstellungen.json` – shown at the bottom of the page, with *Ordner öffnen* (open folder). It is created at the first change – closing the window for the first time counts, because Lupe remembers that it has shown the note *Lupe läuft weiter*. With all defaults the file looks like this:

```json
{
  "Actions": "Kopieren",
  "ScreenActions": "Kopieren",
  "TypeSuffix": "",
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
| `Formats` | `[]` = all, otherwise names such as `"QR_CODE"`, `"EAN_13"` – see [Supported formats](../README.md#supported-formats) |
| `ScreenHotkey` | `Ctrl`, `Alt`, `Shift`, `Win` and `Key` – a Windows virtual-key code (`81` = Q) |
| `RepeatSuppressMs` | milliseconds, 0 to 3,600,000 |
| `Camera` | the camera's identifier, `""` = the first camera |
| `HistoryLimit` | 1 to 10,000 (the page accepts 10 to 10,000) |
| `TrayHintShown` | internal: the note *Lupe läuft weiter* has been shown once |

Reading is tolerant: missing keys get their default, unknown keys are ignored, a single unusable value falls back to its default without losing the others, and in lists only the unusable elements are dropped. Out-of-range numbers are clamped, unknown format names removed, a shortcut without a usable key (or a plain letter without <kbd>Ctrl</kbd>, <kbd>Alt</kbd> or <kbd>Win</kbd>) is replaced by the default. A link rule with an invalid regular expression stays in the table, marked with the error, but is not applied.

Lupe reads the file when it starts and writes the whole file on every change. **Edit it by hand only while Lupe is not running** – otherwise the next change in the app overwrites your edit. Writing is atomic: Lupe writes a temporary file next to it, flushes it to disk and then replaces the old file, so a crash or a full disk never leaves half a file.

## Rolling out settings

There is no machine-wide or policy configuration: Lupe reads its settings only from `%LocalAppData%\Lupe\einstellungen.json` of the user who runs it, and the MSI installs none. To pre-configure users – link rules, actions, the shortcut – place an `einstellungen.json` there before Lupe starts, for example with a logon script, Group Policy preferences or an Intune script. The file may contain only the keys you want to set; the others get their defaults. Replacing an existing file overwrites the user's own changes. `%LocalAppData%` is not part of a roaming profile, so the file has to be on every PC the user signs in to.

## Broken or locked file

Lupe never silently overwrites a settings file it could not read properly:

| Situation | What Lupe does |
|---|---|
| **Broken** – not valid JSON, or not an object | Starts with the defaults and says so. Before the next save, the file is copied unchanged to `einstellungen.json.defekt-YYYYMMDD-hhmmss` next to it. If that copy fails, nothing is saved. |
| **Partly unusable** – single values of the wrong type | Uses the defaults for those values and names them (*Standardwert verwendet für: …*). Before the next save, the original is backed up the same way. |
| **Not readable** – locked by another program, no permission | Starts with the defaults but does **not** write the file. Lupe tries to read it again every 5 seconds for about two minutes. Once it is readable, your changes from this session are applied field by field on top of its content; what you did not change stays as it was. A last attempt is made when Lupe quits. If there is no history file yet, the history stays in memory until then – Lupe cannot know whether saving it is allowed. |
| **Cannot be written** – disk full, no permission | The change applies until Lupe quits; the reason is shown. |

Problems appear as *Problem mit der Einstellungsdatei* at the top of the page and as a message when Lupe starts.

---

<p align="center"><sub>
<a href="installation.md">Installation</a> · <a href="usage.md">Using the app</a> · <b>Settings</b> · <a href="link-rules.md">Link rules</a> · <a href="command-line.md">Command line</a> · <a href="how-it-works.md">How it works</a> · <a href="data-and-privacy.md">Data &amp; privacy</a> · <a href="limitations.md">Limitations</a> · <a href="development.md">Development</a><br>
<a href="../README.md">← Back to Lupe</a>
</sub></p>
