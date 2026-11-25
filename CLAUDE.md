# CLAUDE.md – Lupe

Lupe: Liest QR- und Barcodes per Webcam, Bildschirmausschnitt, Datei oder Zwischenablage.

Projekt der ICT Baden GmbH. Diese Datei ist die Arbeitsanleitung für Claude Code. Regeln für Beiträge stehen in `CONTRIBUTING.md`, Bauen, Testen, CI und Releases in `docs/development.md`. „Richtlinie NN“ meint die internen, nicht öffentlichen Richtlinien der ICT Baden GmbH; alles Nötige steht in den beiden Dateien.

## Bauen und testen

```powershell
dotnet build Lupe.sln -c Release
dotnet test Lupe.sln -c Release
dotnet run --project src/Lupe -- --demo
```

Unter Linux und macOS gibt es nur das Ziel `net10.0`: `dotnet test tests/Lupe.Tests -c Release -f net10.0`.

## Aufbau

- `src/Lupe` – Oberfläche (Avalonia) und Befehlszeile
- `src/Lupe.Core` – Logik ohne Oberfläche und ohne Systemaufrufe, Sprachdateien in `Resources/`
- `src/Lupe.Platform.Windows`, `.Linux`, `.MacOS` – Systemanbindung je Plattform (siehe `docs/architecture-platforms.md`)
- `tests/Lupe.Tests`
- `installer/` – MSI (WiX 5), `packaging/linux`, `packaging/macos` – `.deb`, `tar.gz`, `Lupe.app`
- `docs/` – Doku, Bilder in `docs/assets/`

## Besonderheiten

- Tests mit Webcam, sichtbarem Desktop oder Windows-Eigenheiten überspringen sich selbst, wo das fehlt.
- Echtzeit-Tests der Kamera-Schleife laufen in der Sammlung „Echtzeit“ allein. Lang laufende Schleifen und Barrieren in Tests auf eigene Threads (`TestSupport/OwnThreads`), nicht auf den Thread-Pool: Der macOS-Runner hat nur 3 Kerne.
- CI (`.github/workflows/build.yml`): nach Richtlinie 04 bauen Pushs und PRs nur den Linux-Teil (der das Windows-Ziel der Tests auch übersetzt, aber nicht ausführt); Windows, macOS, ARM, Pakete und Release „latest“ gibt es nachts (wenn `main` neu ist), von Hand, mit `[voll]` in der Commit-Nachricht und bei PRs mit Label `voll-bauen` oder Pfaden aus `FULL_PATHS` (Job `gate`). Artifacts nur kurz aufbewahren (`retention-days`), der Speicher im Free-Plan ist klein.
- Die macOS-Kamera läuft über einen eigenen Swift-Helfer (`src/Lupe.Platform.MacOS/Native`). Auf einem echten Mac (Apple Silicon) sind bisher Start mit Fenster, Demo-Kamera und Befehlszeile geprüft; Bildschirm-Scan, Eintippen, Kürzel und echte Kamera dort noch nicht.
- Dateinamen im rollierenden Release „latest“ sind fest (Links in Doku, Softwareverteilung und Lesezeichen), nicht umbenennen. Tag-Releases tragen die Version im Namen, außer `Lupe.exe`.

## Regeln

- Commit-Nachrichten auf Deutsch.
- Kein Text fest im Code: jeden neuen Text in alle Sprachdateien eintragen (en, de, es, fr, zh-Hans; es, fr und zh-Hans maschinell übersetzt, Gegenlesen durch Muttersprachler steht aus), Fachbegriffe aus `docs/glossary.csv`. Unsichere Übersetzung: englischen Text eintragen, mit `TODO-übersetzen` markieren. Regeln in Richtlinie 13.
- Jede `.cs`-Datei beginnt mit `// SPDX-License-Identifier: MIT`.
- `CHANGELOG.md` ist englisch. Vor jedem Release den Abschnitt „Unreleased“ zur Version machen (`## [x.y.z] – Datum`): Der Release-Job nimmt genau diesen Abschnitt als Release-Text und bricht ohne ihn ab. Vor dem Tag den Text einmal lokal erzeugen: `GITHUB_REPOSITORY=ict-baden/lupe GITHUB_SHA=x pwsh .github/scripts/release-notes.ps1 -Tag vX.Y.Z -Version X.Y.Z -Suffix -X.Y.Z` (ohne lokales pwsh: im Container `mcr.microsoft.com/dotnet/sdk:10.0`, der hat pwsh und läuft auch auf Apple Silicon). In PowerShell-Texten keine typografischen Anführungszeichen – CI prüft das (`.github/scripts/ps-syntax.*`). Die Download-Tabellen in `README.md` und `docs/installation.md` nennen die Dateien der aktuellen Version mit Nummer: vor dem Tag auf die neue Version ziehen. Nach dem Tag `<Version>` in `src/Lupe/Lupe.csproj` (und `ProductVersion` in `installer/Lupe.Installer.wixproj`) auf die nächste Patch-Version heben, damit Builds von `main` nicht älter heißen als das Release.
