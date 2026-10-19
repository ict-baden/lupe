// SPDX-License-Identifier: MIT
namespace Lupe.Tests.TestSupport;

/// <summary>
/// Tests, die echte Zeit (Bildrate, Wiederholungssperre) oder den Speicher des Testprozesses messen. xUnit führt diese
/// Sammlung allein aus, nachdem die parallelen Tests fertig sind – auf GitHub-Runnern mit wenigen Kernen bremsten die
/// übrigen Tests die Erkennung sonst so stark, dass Zeitfenster verpasst wurden, und ihr Speicher zählte mit.
/// Auch für Tests, die auf den Thread-Pool warten (Timer, Takt der Demo-Kamera, Erkennung der Scan-Schleife): Die parallelen
/// Erkennungstests stauten den Pool auf dem macOS-Runner (3 Kerne) so, dass ein Task.Delay(100) über 10 s dauerte.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EchtzeitCollection
{
    public const string Name = "Echtzeit";
}
