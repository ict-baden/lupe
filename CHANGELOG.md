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
- Oberfläche auf Englisch, Deutsch, Spanisch, Französisch und Chinesisch (vereinfacht), folgt der Systemsprache, umschaltbar. Spanisch, Französisch und Chinesisch sind maschinell vorübersetzt und noch nicht von Muttersprachlern gegengelesen.
- Info-Seite mit Version, Build, Lizenz, Drittanbieter-Lizenzen, Webseite, Impressum, Datenschutz und Support; die Lizenz öffnet lokal statt im Browser.
- `lupe --version` und `lupe --info` (Version, Lizenz, Links); englische Namen für Befehle und Schalter (`read`, `--background`, `--screen`, `--help`, `--theme light|dark|both`). Hilfe und Info-Seite nennen den Befehl je System (`Lupe.exe`, `lupe`, unter macOS das Programm in `Lupe.app`) und die dort lesbaren Bildformate.

### Geändert
- Linux-Paket heißt jetzt `ictbaden-lupe` (vorher `lupe`) und ersetzt das alte Paket beim Installieren.
- Jedes Paket enthält jetzt `LICENSE.txt`; das `.deb` hat eine `copyright`-Datei.
- Paketangaben standardmäßig Englisch: Menüeintrag unter Linux (mit de, es, fr, zh_CN), Beschreibung des `.deb`, `README.txt` neben `LIESMICH.txt` im tar.gz; die Kamera-Abfrage von macOS in allen fünf Sprachen; `CFBundleVersion` nur noch Zahlen.
- CI prüft jede mitgelieferte native Bibliothek (glibc, macOS-Mindestversion, VC++-Laufzeit) und liest mit jedem Paket einen Code, unter Linux auch auf Ubuntu 20.04/22.04 und Debian 12, unter Windows auch nach einer MSI-Installation.
- macOS: Signatur mit Developer ID und Notarisierung sind vorbereitet; sie laufen, sobald die Zugangsdaten in den Secrets stehen.
- Kamera: Beim Eintippen werden Zeilenumbrüche in einem Code als Leerzeichen getippt – ein Code vor der Kamera kann im Programm vorn kein Enter mehr drücken. Die eingestellte Taste nach dem Tippen kommt weiter.
- Linux und macOS: Einstellungen und Verlauf nur noch für den eigenen Benutzer lesbar (Ordner 0700, Dateien 0600).
- Befehlszeile: auch C1-Steuerzeichen, Zeilentrenner U+2028/U+2029 und Zeichen zur Schreibrichtung werden maskiert.
- Release: `Lupe-sbom.cdx.json`, eine Stückliste (CycloneDX) aller Bestandteile.
- Verlauf: breitere Zeitspalte (Datum wurde abgeschnitten).

### Behoben
- Linux: Auf Debian 12, Ubuntu 22.04 und anderen Systemen mit glibc unter 2.38 meldete jeder Scan „Kein Code gefunden“, danach brach Lupe ab. Die native zxing-cpp-Bibliothek wird jetzt selbst gebaut (glibc 2.28, x64 und arm64), das `.deb` nennt die nötige glibc-Version.
- macOS: Die zxing-cpp-Bibliothek verlangte macOS 26 (Apple Silicon) bzw. 15 (Intel); jetzt wie die App selbst macOS 12.
- Windows: Die zxing-cpp-Bibliothek brauchte die VC++-Laufzeit, die kein Paket mitbrachte; jetzt mit statisch gebundener Laufzeit.
- Linux: Die Tondatei bei einem Treffer lag unter festem Namen im gemeinsamen /tmp; ein anderer Benutzer konnte dort einen Symlink anlegen, den Lupe überschrieb. Jetzt in einem eigenen, neu angelegten Ordner. Ebenso liegt der Socket für „nur eine Lupe“ im Laufzeitordner des Benutzers statt in /tmp und nimmt nur Verbindungen desselben Benutzers an.
- Start ohne Kulturdaten (`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`) brach mit „en is an invalid culture identifier“ ab.
- Lädt die Erkennungsbibliothek nicht, meldet Lupe das jetzt als Fehler mit Code `LUP-0001` (Befehlszeile: Exitcode 3, Oberfläche: Hinweis beim Start und bei jedem Scan) statt „Kein Code gefunden“, und stürzt nicht mehr ab.
