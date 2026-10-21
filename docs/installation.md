<sub><a href="../README.md">Lupe</a> › <a href="../README.md#documentation">Documentation</a> › Installation</sub>

# Installation

Which file to download, what it needs, how to let Lupe use the camera, and how to start it with Windows or remove it again. Linux and macOS have their own sections at the end.

**Contents:** [Requirements](#requirements) · [Downloads](#downloads) · [MSI installer](#msi-installer) · [Single exe and ZIP](#single-exe-and-zip) · [First start](#first-start) · [Camera access in Windows](#camera-access-in-windows) · [Start with Windows](#start-with-windows) · [Running as administrator](#running-as-administrator) · [Updates and uninstalling](#updates-and-uninstalling) · [Linux](#linux) · [macOS](#macos)

> [!IMPORTANT]
> **Pre-release.** Lupe is built and tested in GitHub Actions on every push to `main` (and for pull requests and `v*` tags), but there is no stable release yet. Program and MSI are **not code-signed**; Windows SmartScreen may warn when you start them.

## Requirements

| Topic | Requirement |
|---|---|
| **Operating system** | 64-bit Windows 10 version 1809 (build 17763) or later, or Windows 11. Windows builds are produced for `win-x64`; Linux and macOS: see [Linux](#linux) and [macOS](#macos). |
| **.NET** | None for the MSI and for `Lupe.exe` – both carry the .NET 10 runtime. The ZIP variant needs the **.NET 10 Runtime**. |
| **Camera** (optional) | Any webcam that Windows offers to apps through `Windows.Media.Capture` with a colour image. Pure infrared cameras (Windows Hello) are not listed. Windows editions “N” and “KN” need the **Media Feature Pack** for the camera. |
| **Image formats** | PNG, JPEG, BMP, GIF, TIFF, ICO and JPEG XR work out of the box. WebP, HEIC/HEIF, AVIF and JPEG XL work when the matching image extension from the Microsoft Store is installed. |
| **Language** | English, German, Spanish, French or Chinese (simplified): follows the display language of the system (English for any other language), switchable under *Settings › Language* or preset with `"Language": "de"` (`en`, `de`, `es`, `fr`, `zh-Hans`) in a distributed `einstellungen.json`. Spanish, French and Chinese are machine translations, not yet reviewed. |

## Downloads

All files are attached to the release [**latest**](https://github.com/ict-baden/lupe/releases/tag/latest) – on the repository page under *Releases* on the right – which GitHub Actions updates on every push to `main`. The file names stay the same, so these links always deliver the newest build:

| File | Runtime | Use it for |
|---|---|---|
| [`Lupe-x64.msi`](https://github.com/ict-baden/lupe/releases/download/latest/Lupe-x64.msi) | included | Installing for all users of a PC, also through software distribution (Intune, GPO). |
| [`Lupe.exe`](https://github.com/ict-baden/lupe/releases/download/latest/Lupe.exe) | included | Running without installation, e.g. from a user folder or a USB stick. |
| [`Lupe-win-x64-framework.zip`](https://github.com/ict-baden/lupe/releases/download/latest/Lupe-win-x64-framework.zip) | .NET 10 Runtime | A small download where the runtime is already installed. |

Builds from `main` carry versions like `0.1.0-ci.12` (12 = the number of the build run); the release title names the version of the current files. Tagged versions (`v1.2.3`) get their own release, where the MSI and the ZIP carry the version in their names (e.g. `Lupe-1.2.3-x64.msi`) and `Lupe.exe` keeps its name.

For scripted downloads, e.g. to package the MSI for Intune, the links work directly – with `curl`, `Invoke-WebRequest` or the [GitHub CLI](https://cli.github.com/):

```powershell
Invoke-WebRequest https://github.com/ict-baden/lupe/releases/download/latest/Lupe-x64.msi -OutFile Lupe-x64.msi
gh release download latest -R ict-baden/lupe -p Lupe-x64.msi --clobber
```

Check the file against [`SHA256SUMS.txt`](https://github.com/ict-baden/lupe/releases/download/latest/SHA256SUMS.txt) of the same release (`Get-FileHash Lupe-x64.msi`).

## MSI installer

The MSI installs per machine:

- program files into `C:\Program Files\Lupe`,
- one Start menu entry *Lupe* (*QR- und Barcodes lesen*),
- **no** autostart, no service, no scheduled task. Starting with Windows is a per-user choice inside the app – see [Start with Windows](#start-with-windows).

```powershell
# As administrator. msiexec returns at once when started directly from PowerShell –
# Start-Process -Wait waits for it, .ExitCode is 0 on success (3010: restart required).
Start-Process msiexec.exe '/i Lupe-x64.msi' -Wait                              # interactive
(Start-Process msiexec.exe '/i Lupe-x64.msi /qn' -Wait -PassThru).ExitCode     # silent, e.g. for software distribution
```

To uninstall silently, address the installed product rather than an MSI file: every build has its own product code, so `msiexec /x` with a newer download than the installed one fails with error 1605. Lupe's upgrade code stays the same for all builds, and Windows Installer finds the installed product by it – from 32-bit and 64-bit PowerShell alike (Intune runs scripts 32-bit by default):

```powershell
# As administrator
$installer = New-Object -ComObject WindowsInstaller.Installer
foreach ($product in $installer.RelatedProducts('{B5E63BC4-65DB-46E9-B6B1-5A3167B381D1}')) {
    (Start-Process msiexec.exe "/x $product /qn" -Wait -PassThru).ExitCode    # 0 = removed
}
```

The installer closes a running Lupe before it replaces the files. It installs no settings – they are per user; to pre-configure them, see [Settings › Rolling out settings](settings.md#rolling-out-settings).

## Single exe and ZIP

**`Lupe.exe`** is the whole program in one file – runtime and native libraries are embedded. Copy it anywhere and start it. Because it is not signed, SmartScreen may show *Windows protected your PC*; *More info* › *Run anyway* starts it.

**The ZIP** contains a smaller, framework-dependent build and a copy of the project's `README.md` (its images and links point into the repository and only work on GitHub). Extract it and start `Lupe.exe`; Windows asks for the .NET 10 Runtime if it is missing.

All variants behave the same: same features, same command line, same settings and history under `%LocalAppData%\Lupe` of the user who runs it – see [Data & privacy](data-and-privacy.md).

## First start

Lupe opens on the page *Camera* and registers the global shortcut <kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>Q</kbd> for the screen scan. The card at the top left shows the state:

| Card shows | Meaning |
|---|---|
| *Win+Shift+Q*, below it *scans a screen region* | The shortcut is registered and works in every program. |
| *not available – see Settings* | Another program already uses this combination. Choose a different one under *Settings › Keyboard shortcut* – see [Settings](settings.md#screen-scan). |

Only **one Lupe runs per Windows session.** Starting it a second time brings the existing window to the front instead of opening another one. On a terminal server, every signed-in user has their own Lupe.

When you close the window, Lupe keeps running in the notification area so that the shortcut stays available. The first time, a notification says so (*Lupe is still running*). To quit, right-click the Lupe icon in the notification area and choose *Quit*. If you prefer closing to quit, switch off *Keep running in the notification area when closed* in the settings.

## Camera access in Windows

Windows decides whether desktop programs may use the camera. If access is blocked, Lupe does not show a generic error but names the switch that is off, with the path *Settings → Privacy & security → Camera* (in German Windows *Einstellungen → Datenschutz und Sicherheit → Kamera*):

| Switch in Windows (English / German) | Needed | Lupe's message when it is off |
|---|---|---|
| *Camera access* / *Kamerazugriff* | on – changing it needs administrator rights | *Camera access is turned off for this device. …* |
| *Let apps access your camera* / *Zugriff von Apps auf die Kamera* | on | *Camera access for apps is turned off. …* |
| *Let desktop apps access your camera* / *Desktop-Apps den Zugriff auf die Kamera erlauben* | on | *Windows does not allow desktop apps to access the camera. …* |
| Lupe in the list of desktop apps below | not blocked | *Windows has revoked Lupe's camera access. …* |
| Group policy (*AllowCamera* = 0 or *LetAppsAccessCamera* = Force Deny) | not set | *The camera is blocked on this device by policy. Contact your IT department.* |

Lupe only **reads** these settings to explain the problem; it never changes them. If none of them is off, the message suggests checking a hardware camera switch or a cover.

Other camera problems get their own message as well, for example when another program such as Teams or Zoom holds the camera (*is being used by another program*), when it was unplugged, or when Windows lacks the media features (*Media Feature Pack*). If the camera cannot be opened exclusively – typically because another program holds it – Lupe tries once more in shared read-only mode before it reports the problem. Details: [How it works › Camera](how-it-works.md#camera).

> [!NOTE]
> The camera code was developed without a real webcam and is tested against a simulated frame source only – see [Limitations](limitations.md#not-tested-on-real-hardware).

## Start with Windows

*Settings › Startup and notification area › Start with Windows* makes Lupe start at sign-in, invisible in the notification area, with the shortcut ready.

- It is **per user**: Lupe writes the value `Lupe` under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` with the content `"<path>\Lupe.exe" --hintergrund`. No administrator rights are needed, and the MSI never sets it.
- The entry points to the `Lupe.exe` that was running when you switched it on. If you move or rename `Lupe.exe`, switch the setting off and on again – the same applies if a copy in another folder should be the one that starts.
- With `--hintergrund` Lupe shows no window. If the notification area is not available, it opens the window anyway so that it is not running invisibly and unreachable.
- A Lupe started as `dotnet Lupe.dll` cannot register itself and says so (*The path of Lupe.exe is unknown – autostart was not set up.*).

## Running as administrator

Lupe does not request administrator rights (manifest level `asInvoker`) and does not need them. The one exception is **typing into programs that run as administrator**: Windows blocks keyboard input from a normal program into an elevated window (User Interface Privilege Isolation). Lupe detects this, types nothing and says so in the result notification: *Cannot type – the window in the foreground runs with higher privileges than Lupe (e.g. as administrator) …*

If you regularly type into an elevated program, start Lupe with *Run as administrator* as well. Note that the autostart entry starts Lupe without elevation.

## Updates and uninstalling

- **MSI:** install the newer MSI over the old one. Builds from `main` replace each other – in either direction: Windows Installer compares only the first three parts of the version (`0.1.0`), and the package allows the same version to be installed again, so an older build from `main` also installs over a newer one. A release with a higher version replaces them as well. Only an MSI whose first three parts are *lower* than those of the installed version is refused (*Eine neuere Version von Lupe ist bereits installiert.*).
- **Exe and ZIP:** replace the files. Keep the path if *Start with Windows* is on.
- **Settings and history** stay where they are and are read by the new version.
- **Uninstalling** (*Settings › Apps › Installed apps › Lupe* – on Windows 10 *Apps & features* –, or silently as shown under [MSI installer](#msi-installer)) removes the program files and the Start menu entry. It does **not** remove:
  - `%LocalAppData%\Lupe` of each user (settings and history) – delete that folder to remove them;
  - the autostart entry of users who switched on *Start with Windows*. Switch it off before uninstalling, or delete the value `Lupe` under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`.

## Linux

Lupe runs on X11 and Wayland desktops, x64 and arm64, on distributions with glibc 2.31 or later (Ubuntu 20.04, Debian 11 and newer; RHEL 9). That is the minimum CI enforces for every shipped library and tests: each build reads a code from the command line on Ubuntu 20.04, Ubuntu 22.04 and Debian 12. Older systems are not tested. Both packages include .NET. The `.deb` needs ICU 66 or newer (`libicu66` on Ubuntu 20.04, `libicu67` on Debian 11).

| File | Install |
|---|---|
| [`lupe_amd64.deb`](https://github.com/ict-baden/lupe/releases/download/latest/lupe_amd64.deb) | Debian, Ubuntu and derivatives (x64): `sudo apt install ./lupe_amd64.deb`. Installs to `/opt/lupe`, the command `lupe` and a menu entry; `sudo apt remove ictbaden-lupe` removes it (the package was called `lupe` before; installing the new one replaces it). |
| [`Lupe-linux-x64.tar.gz`](https://github.com/ict-baden/lupe/releases/download/latest/Lupe-linux-x64.tar.gz), [`Lupe-linux-arm64.tar.gz`](https://github.com/ict-baden/lupe/releases/download/latest/Lupe-linux-arm64.tar.gz) | Any distribution with glibc 2.31 or later: `tar -xzf Lupe-linux-x64.tar.gz && lupe/install.sh`. Installs for the current user only (no root) into `~/.local/opt/lupe`, with the command `lupe` in `~/.local/bin` and a menu entry. `./Lupe` inside the folder also runs it without installing. Remove with `~/.local/opt/lupe/install.sh --remove`. |

Tagged releases name the package `lupe_1.2.3_amd64.deb`. The libraries Lupe needs are part of every desktop installation; on a minimal system install `libx11-6 libxtst6 libxrandr2 libxi6 libice6 libsm6 libfontconfig1` and ICU, e.g. `libicu72` on Debian 12 (`apt-cache search --names-only '^libicu[0-9]+$'` shows the name; the `.deb` pulls them in). Without ICU Lupe does not start; without fontconfig it cannot read image files. If Lupe reports `LUP-0001`, the recognition library cannot be loaded – see [Error codes](error-codes.md#lup-0001).

- **Camera:** your user must be in the group `video` – `sudo usermod -aG video $USER`, then sign out and in again. Lupe says so on the page *Camera* when the device is there but not readable.
- **X11:** the screen scan, typing (XTest) and the shortcut work without asking. If another program holds the shortcut, Lupe reports it as taken.
- **Wayland:** the screen scan, typing and the shortcut go through `xdg-desktop-portal`. The desktop asks once for permission to take screenshots, to control the keyboard (*Remote Desktop*) and to register the shortcut; it may choose different keys. GNOME before 48 and some compositors (sway, other wlroots desktops) have no GlobalShortcuts portal – bind a shortcut to `lupe --bildschirm` in the desktop's own settings (sway: `bindsym $mod+Shift+q exec lupe --bildschirm`), or start the scan from the tray menu or the window. The window needs XWayland; without it Lupe ends with a message (`lupe lesen` still works). See [Limitations › Linux and macOS](limitations.md#linux-and-macos).
- **Tray icon:** needs a StatusNotifier host. On GNOME install the extension *AppIndicator and KStatusNotifierItem Support*; without it, closing the window still keeps Lupe running, but there is no icon to bring it back – start `lupe` again.
- **Start with the session:** the autostart setting writes `~/.config/autostart/lupe.desktop`.
- **Data:** settings in `~/.config/lupe`, history in `~/.local/share/lupe` (or the `XDG_CONFIG_HOME` / `XDG_DATA_HOME` folders you set).

## macOS

Lupe runs on macOS 12 or later. Download [`Lupe-macos-arm64.zip`](https://github.com/ict-baden/lupe/releases/download/latest/Lupe-macos-arm64.zip) for Apple Silicon (M1 and later) or [`Lupe-macos-x64.zip`](https://github.com/ict-baden/lupe/releases/download/latest/Lupe-macos-x64.zip) for Intel Macs, unzip it and move `Lupe.app` to *Applications*. .NET is included. Unzip with the Finder (double-click) or `ditto -x -k Lupe-macos-arm64.zip .` – the command-line tool `unzip` drops the extended attributes that carry the signatures of the bundled .NET files, and macOS then refuses the app as damaged.

- **Gatekeeper:** the app is only ad-hoc signed, not yet signed with a Developer ID and not notarised, so macOS refuses the first start with *“Lupe.app” cannot be opened* (or *Apple could not verify …*). Once per download:
  1. Open `Lupe.app` once and close the message with *Done* / *OK*.
  2. Open *System Settings › Privacy & Security*, scroll to *Security* – there is a line about Lupe with the button ***Open Anyway***. Click it and confirm with your password or Touch ID.
  3. Start Lupe again and click *Open*.

  On macOS 12 to 14, *right-click `Lupe.app` → Open* and confirming also works; macOS 15 and later only offer the way above. Alternatively, in Terminal: `xattr -dr com.apple.quarantine /Applications/Lupe.app`. The CI is prepared to sign and notarise Lupe as soon as ICT-Baden has an Apple Developer ID; then this step disappears.
- **Permissions** – macOS asks for each one the first time Lupe needs it; when one is missing, Lupe shows a hint with a button to the right page in *System Settings › Privacy & Security*:
  - *Camera* for the page *Camera*,
  - *Screen Recording* for the screen scan – after allowing it, quit and start Lupe again,
  - *Accessibility* for typing.

  Because each build has a new ad-hoc signature, macOS treats an updated `Lupe.app` as a different app: after an update, grant *Screen Recording* and *Accessibility* again (remove the old *Lupe* entry with − and add the new one). This ends with the Developer ID signature.
- **Shortcut:** the default is <kbd>⌘</kbd>+<kbd>⌥</kbd>+<kbd>Q</kbd>; <kbd>⌘</kbd>+<kbd>⇧</kbd>+<kbd>Q</kbd> is *Log Out* on macOS.
- **Start at login:** the autostart setting writes the LaunchAgent `~/Library/LaunchAgents/de.ict-baden.lupe.plist`.
- **Data:** settings and history in `~/Library/Application Support/Lupe`.
- **Uninstalling:** quit Lupe, delete `Lupe.app`, and if you like the folder above and the LaunchAgent.

---

<p align="center"><sub>
<b>Installation</b> · <a href="usage.md">Using the app</a> · <a href="settings.md">Settings</a> · <a href="link-rules.md">Link rules</a> · <a href="command-line.md">Command line</a> · <a href="how-it-works.md">How it works</a> · <a href="data-and-privacy.md">Data &amp; privacy</a> · <a href="limitations.md">Limitations</a> · <a href="development.md">Development</a><br>
<a href="../README.md">← Back to Lupe</a>
</sub></p>
