<sub><a href="../README.md">Lupe</a> › <a href="../README.md#documentation">Documentation</a> › Installation</sub>

# Installation

Which file to download, what it needs, how to let Lupe use the camera, and how to start it with Windows or remove it again.

**Contents:** [Requirements](#requirements) · [Downloads](#downloads) · [MSI installer](#msi-installer) · [Single exe and ZIP](#single-exe-and-zip) · [First start](#first-start) · [Camera access in Windows](#camera-access-in-windows) · [Start with Windows](#start-with-windows) · [Running as administrator](#running-as-administrator) · [Updates and uninstalling](#updates-and-uninstalling)

> [!IMPORTANT]
> **Pre-release.** Lupe is built and tested in GitHub Actions on every push to `main` (and for pull requests and `v*` tags), but there is no stable release yet. Program and MSI are **not code-signed**; Windows SmartScreen may warn when you start them.

## Requirements

| Topic | Requirement |
|---|---|
| **Operating system** | 64-bit Windows 10 version 1809 (build 17763) or later, or Windows 11. Builds are produced for `win-x64` only. |
| **.NET** | None for the MSI and for `Lupe.exe` – both carry the .NET 10 runtime. The ZIP variant needs the **.NET 10 Desktop Runtime**. |
| **Camera** (optional) | Any webcam that Windows offers to apps through `Windows.Media.Capture` with a colour image. Pure infrared cameras (Windows Hello) are not listed. Windows editions “N” and “KN” need the **Media Feature Pack** for the camera. |
| **Image formats** | PNG, JPEG, BMP, GIF, TIFF, ICO and JPEG XR work out of the box. WebP, HEIC/HEIF, AVIF and JPEG XL work when the matching image extension from the Microsoft Store is installed. |
| **Language** | The user interface and the command-line output are German, regardless of the Windows display language. |

## Downloads

All files are attached to the pre-release [**latest**](https://github.com/ict-baden/lupe/releases/tag/latest), which GitHub Actions rebuilds on every push to `main`. Tagged versions (`v1.2.3`) get their own release with the same files.

| File | Runtime | Use it for |
|---|---|---|
| `Lupe-<version>-x64.msi` | included | Installing for all users of a PC, also through software distribution (Intune, GPO). |
| `Lupe.exe` | included | Running without installation, e.g. from a user folder or a USB stick. |
| `Lupe-<version>-win-x64-framework.zip` | .NET 10 Desktop Runtime | A small download where the runtime is already installed. |

Builds from `main` carry versions like `0.1.0-ci.12` (12 = the number of the build run); releases from a tag carry the tag's version.

## MSI installer

The MSI installs per machine:

- program files into `C:\Program Files\Lupe`,
- one Start menu entry *Lupe* (*QR- und Barcodes lesen*),
- **no** autostart, no service, no scheduled task. Starting with Windows is a per-user choice inside the app – see [Start with Windows](#start-with-windows).

```powershell
# As administrator. The file name contains the version of the build.
msiexec /i Lupe-0.1.0-ci.12-x64.msi            # interactive
msiexec /i Lupe-0.1.0-ci.12-x64.msi /qn        # silent, e.g. for software distribution
msiexec /x Lupe-0.1.0-ci.12-x64.msi /qn        # uninstall
```

The installer closes a running Lupe before it replaces the files. It installs no settings – they are per user; to pre-configure them, see [Settings › Rolling out settings](settings.md#rolling-out-settings).

## Single exe and ZIP

**`Lupe.exe`** is the whole program in one file – runtime and native libraries are embedded. Copy it anywhere and start it. Because it is not signed, SmartScreen may show *Windows protected your PC*; *More info* › *Run anyway* starts it.

**The ZIP** contains a smaller, framework-dependent build and a copy of the project's `README.md` (its images and links point into the repository and only work on GitHub). Extract it and start `Lupe.exe`; Windows asks for the .NET 10 Desktop Runtime if it is missing.

All variants behave the same: same features, same command line, same settings and history under `%LocalAppData%\Lupe` of the user who runs it – see [Data & privacy](data-and-privacy.md).

## First start

Lupe opens on the page *Kamera* and registers the global shortcut <kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>Q</kbd> for the screen scan. The card at the top left shows the state:

| Card shows | Meaning |
|---|---|
| *Win+Umschalt+Q*, below it *scannt einen Ausschnitt* | The shortcut is registered and works in every program. (*Umschalt* is German for <kbd>Shift</kbd>.) |
| *belegt – siehe Einstellungen* | Another program already uses this combination. Choose a different one under *Einstellungen › Tastenkürzel* – see [Settings](settings.md#screen-scan). |

Only **one Lupe runs per Windows session.** Starting it a second time brings the existing window to the front instead of opening another one. On a terminal server, every signed-in user has their own Lupe.

When you close the window, Lupe keeps running in the notification area so that the shortcut stays available. The first time, a notification says so (*Lupe läuft weiter*). To quit, right-click the Lupe icon in the notification area and choose *Beenden*. If you prefer closing to quit, switch off *Beim Schließen im Infobereich weiterlaufen* in the settings.

## Camera access in Windows

Windows decides whether desktop programs may use the camera. If access is blocked, Lupe does not show a generic error but names the switch that is off – in German, with the path *Einstellungen → Datenschutz und Sicherheit → Kamera*. In English Windows that is *Settings › Privacy & security › Camera*:

| Switch in Windows (English / German) | Needed | Lupe's message when it is off |
|---|---|---|
| *Camera access* / *Kamerazugriff* | on – changing it needs administrator rights | *Der Kamerazugriff ist für dieses Gerät ausgeschaltet. …* |
| *Let apps access your camera* / *Zugriff von Apps auf die Kamera* | on | *Der Kamerazugriff für Apps ist ausgeschaltet. …* |
| *Let desktop apps access your camera* / *Desktop-Apps den Zugriff auf die Kamera erlauben* | on | *Windows erlaubt Desktop-Apps keinen Kamerazugriff. …* |
| Lupe in the list of desktop apps below | not blocked | *Windows hat Lupe den Kamerazugriff entzogen. …* |
| Group policy (*AllowCamera* = 0 or *LetAppsAccessCamera* = Force Deny) | not set | *Die Kamera ist auf diesem Gerät per Richtlinie gesperrt. Wenden Sie sich an Ihre IT-Abteilung.* |

Lupe only **reads** these settings to explain the problem; it never changes them. If none of them is off, the message suggests checking a hardware camera switch or a cover.

Other camera problems get their own message as well, for example when another program such as Teams or Zoom holds the camera (*wird gerade von einem anderen Programm benutzt*), when it was unplugged, or when Windows lacks the media features (*Media Feature Pack*). If the camera cannot be opened exclusively – typically because another program holds it – Lupe tries once more in shared read-only mode before it reports the problem. Details: [How it works › Camera](how-it-works.md#camera).

> [!NOTE]
> The camera code was developed without a real webcam and is tested against a simulated frame source only – see [Limitations](limitations.md#not-tested-on-real-hardware).

## Start with Windows

*Einstellungen › Start und Infobereich › Mit Windows starten* makes Lupe start at sign-in, invisible in the notification area, with the shortcut ready.

- It is **per user**: Lupe writes the value `Lupe` under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` with the content `"<path>\Lupe.exe" --hintergrund`. No administrator rights are needed, and the MSI never sets it.
- The entry points to the `Lupe.exe` that was running when you switched it on. If you move or rename `Lupe.exe`, switch the setting off and on again – the same applies if a copy in another folder should be the one that starts.
- With `--hintergrund` Lupe shows no window. If the notification area is not available, it opens the window anyway so that it is not running invisibly and unreachable.
- A Lupe started as `dotnet Lupe.dll` cannot register itself and says so (*Der Pfad von Lupe.exe ist nicht bekannt – Autostart nicht eingerichtet.*).

## Running as administrator

Lupe does not request administrator rights (manifest level `asInvoker`) and does not need them. The one exception is **typing into programs that run as administrator**: Windows blocks keyboard input from a normal program into an elevated window (User Interface Privilege Isolation). Lupe detects this, types nothing and says so in the result notification: *Eintippen nicht möglich – das Fenster im Vordergrund läuft mit höheren Rechten als Lupe (z. B. als Administrator) …*

If you regularly type into an elevated program, start Lupe with *Run as administrator* as well. Note that the autostart entry starts Lupe without elevation.

## Updates and uninstalling

- **MSI:** install the newer MSI over the old one. Builds from `main` replace each other (Windows Installer compares only the first three parts of the version, and the package allows same-version upgrades); a release with a higher version replaces them as well. Installing an *older* version over a newer one is refused (*Eine neuere Version von Lupe ist bereits installiert.*).
- **Exe and ZIP:** replace the files. Keep the path if *Mit Windows starten* is on.
- **Settings and history** stay where they are and are read by the new version.
- **Uninstalling** removes the program files and the Start menu entry. It does **not** remove:
  - `%LocalAppData%\Lupe` of each user (settings and history) – delete that folder to remove them;
  - the autostart entry of users who switched on *Mit Windows starten*. Switch it off before uninstalling, or delete the value `Lupe` under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`.

---

<p align="center"><sub>
<b>Installation</b> · <a href="usage.md">Using the app</a> · <a href="settings.md">Settings</a> · <a href="link-rules.md">Link rules</a> · <a href="command-line.md">Command line</a> · <a href="how-it-works.md">How it works</a> · <a href="data-and-privacy.md">Data &amp; privacy</a> · <a href="limitations.md">Limitations</a> · <a href="development.md">Development</a><br>
<a href="../README.md">← Back to Lupe</a>
</sub></p>
