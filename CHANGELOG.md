# Änderungen

Alle wichtigen Änderungen an Lupe. Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [Semantic Versioning](https://semver.org/lang/de/).

## [Unveröffentlicht]

Bisher nur Builds im Release „latest“ (`0.1.0-ci.N`), noch keine Version mit Tag.

### Neu
- Codes lesen per Webcam, Bildschirmausschnitt (Win+Umschalt+Q, auf dem Mac ⌘⌥Q), Bilddatei und Zwischenablage, Erkennung mit zxing-cpp.
- Ergebnis kopieren, in das Programm vorn eintippen oder per Link-Regel als Link öffnen; Kamera als Handscanner.
- Verlauf mit CSV-Export, Einstellungen als JSON, Autostart, Infobereich.
- Befehlszeile mit dokumentierten Exitcodes, `--demo` und `--screenshots`.
- Windows, Linux (X11 und Wayland, x64 und arm64) und macOS (Apple Silicon und Intel), Oberfläche mit Avalonia.
- Pakete: MSI, Einzeldatei-Exe, ZIP mit Framework, `.deb`, `tar.gz`, `Lupe.app`; `SHA256SUMS.txt` im Release.
- Oberfläche auf Englisch und Deutsch, folgt der Systemsprache, umschaltbar.
- Info-Seite mit Version, Build, Lizenz, Drittanbieter-Lizenzen, Webseite, Impressum, Datenschutz und Support; die Lizenz öffnet lokal statt im Browser.
- `lupe --version` und `lupe --info` (Version, Lizenz, Links); englische Namen für Befehle und Schalter (`read`, `--background`, `--screen`, `--help`, `--theme light|dark|both`). Hilfe und Info-Seite nennen den Befehl je System (`Lupe.exe`, `lupe`, unter macOS das Programm in `Lupe.app`) und die dort lesbaren Bildformate.

### Geändert
- Linux-Paket heißt jetzt `ictbaden-lupe` (vorher `lupe`) und ersetzt das alte Paket beim Installieren.
- Jedes Paket enthält jetzt `LICENSE.txt`; das `.deb` hat eine `copyright`-Datei.
- CI prüft jede mitgelieferte native Bibliothek (glibc, macOS-Mindestversion, VC++-Laufzeit) und liest mit jedem Paket einen Code, unter Linux auch auf Ubuntu 20.04/22.04 und Debian 12, unter Windows auch nach einer MSI-Installation.
- macOS: Signatur mit Developer ID und Notarisierung sind vorbereitet; sie laufen, sobald die Zugangsdaten in den Secrets stehen.

### Behoben
- Linux: Auf Debian 12, Ubuntu 22.04 und anderen Systemen mit glibc unter 2.38 meldete jeder Scan „Kein Code gefunden“, danach brach Lupe ab. Die native zxing-cpp-Bibliothek wird jetzt selbst gebaut (glibc 2.28, x64 und arm64), das `.deb` nennt die nötige glibc-Version.
- macOS: Die zxing-cpp-Bibliothek verlangte macOS 26 (Apple Silicon) bzw. 15 (Intel); jetzt wie die App selbst macOS 12.
- Windows: Die zxing-cpp-Bibliothek brauchte die VC++-Laufzeit, die kein Paket mitbrachte; jetzt mit statisch gebundener Laufzeit.
- Lädt die Erkennungsbibliothek nicht, meldet Lupe das jetzt als Fehler mit Code `LUP-0001` (Befehlszeile: Exitcode 3, Oberfläche: Hinweis beim Start und bei jedem Scan) statt „Kein Code gefunden“, und stürzt nicht mehr ab.
