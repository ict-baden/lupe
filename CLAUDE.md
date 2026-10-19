# CLAUDE.md – Lupe

Lupe: Liest QR- und Barcodes per Webcam, Bildschirmausschnitt, Datei oder Zwischenablage.

Projekt von ICT-Baden. Es gelten die Richtlinien in [`ict-baden/richtlinien`](https://github.com/ict-baden/richtlinien). Stand und Zuständigkeit stehen in `ict-baden/ict-baden-projekte` (`projekte.json`, `notizen/lupe.md`, `in-arbeit.md`).

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
- CI (`.github/workflows/build.yml`): Windows, Linux und macOS, Release „latest“ bei jedem Push auf `main`. Artifacts nur kurz aufbewahren (`retention-days`), der Speicher im Free-Plan ist klein.
- Die macOS-Kamera läuft über einen eigenen Swift-Helfer (`src/Lupe.Platform.MacOS/Native`). Der macOS-Build ist noch nicht auf einem echten Mac gelaufen.
- Release-Dateinamen sind fest (Links der Kollegen auf „latest“), nicht umbenennen.

## Regeln

- Commit-Nachrichten auf Deutsch.
- Kein Text fest im Code: jeden neuen Text in alle Sprachdateien eintragen (bisher en, de; es, fr, zh-Hans folgen vor dem ersten öffentlichen Release), Fachbegriffe aus `ict-baden/richtlinien/sprachen/glossar.csv`. Unsichere Übersetzung: englischen Text eintragen, mit `TODO-übersetzen` markieren. Regeln in Richtlinie 13.
- Jede `.cs`-Datei beginnt mit `// SPDX-License-Identifier: MIT`.
- Nach Arbeit an diesem Projekt: Verlauf in `ict-baden-projekte/notizen/lupe.md` und Logbuch dort ergänzen.
- Vor jedem Release: Abschnitt „Unveröffentlicht“ im `CHANGELOG.md` pflegen.
