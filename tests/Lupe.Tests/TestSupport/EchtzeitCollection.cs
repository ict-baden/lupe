namespace Lupe.Tests.TestSupport;

/// <summary>
/// Tests, die echte Zeit messen (Bildrate, Wiederholungssperre). xUnit führt diese Sammlung allein aus, nachdem die
/// parallelen Tests fertig sind – auf GitHub-Runnern mit wenigen Kernen bremsten die übrigen Tests die Erkennung sonst
/// so stark, dass Zeitfenster verpasst wurden.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EchtzeitCollection
{
    public const string Name = "Echtzeit";
}
