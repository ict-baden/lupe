namespace Lupe.Tests.TestSupport;

/// <summary>
/// Tests, die echte Zeit (Bildrate, Wiederholungssperre) oder den Speicher des Testprozesses messen. xUnit führt diese
/// Sammlung allein aus, nachdem die parallelen Tests fertig sind – auf GitHub-Runnern mit wenigen Kernen bremsten die
/// übrigen Tests die Erkennung sonst so stark, dass Zeitfenster verpasst wurden, und ihr Speicher zählte mit.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EchtzeitCollection
{
    public const string Name = "Echtzeit";
}
