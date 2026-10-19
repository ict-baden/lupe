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
- Info-Seite mit Version, Build, Lizenz, Drittanbieter-Lizenzen, Datenschutz und Support.

### Geändert
- Linux-Paket heißt jetzt `ictbaden-lupe` (vorher `lupe`) und ersetzt das alte Paket beim Installieren.
