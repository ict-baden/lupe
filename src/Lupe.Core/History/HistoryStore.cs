using Lupe.Core.Model;

namespace Lupe.Core.History;

/// <summary>Verlauf der Scans (neueste zuerst), optional als JSON unter %LocalAppData%\Lupe\verlauf.json gespeichert.</summary>
public sealed class HistoryStore
{
    /// <param name="path">Datei; null = <see cref="DefaultPath"/>.</param>
    /// <param name="persist">false = nur im Speicher, nichts auf der Platte (und eine vorhandene Datei wird nicht gelesen).</param>
    public HistoryStore(string? path = null, int limit = 1000, bool persist = true) => throw new NotImplementedException();

    public static string DefaultPath => throw new NotImplementedException();

    public IReadOnlyList<HistoryEntry> Entries => throw new NotImplementedException();
    public event EventHandler? Changed;

    public void Add(HistoryEntry entry) => throw new NotImplementedException();
    public void Remove(HistoryEntry entry) => throw new NotImplementedException();
    public void Clear() => throw new NotImplementedException();
    /// <summary>CSV (UTF-8 mit BOM, Semikolon wie Excel in DE): Zeit;Format;Quelle;Inhalt.</summary>
    public void ExportCsv(string path) => throw new NotImplementedException();
}
