using Lupe.Core.Model;
using Lupe.Core.Settings;

namespace Lupe.Core.History;

/// <summary>Verlauf der Scans (neueste zuerst), optional als JSON unter %LocalAppData%\Lupe\verlauf.json gespeichert.</summary>
/// <remarks>
/// <para>
/// Threadsicher: Ändern geht über eine Sperre, <see cref="Entries"/> liefert ohne Sperre eine unveränderliche Momentaufnahme.
/// Nach jeder Änderung wird die ganze Datei atomar neu geschrieben (Temp-Datei + Ersetzen) – bei höchstens einigen tausend
/// Einträgen sind das Millisekunden, und es geht nie etwas verloren, wenn die App hart beendet wird.
/// </para>
/// <para>
/// Erwartbare Dateiprobleme werfen keine Ausnahme: Eine kaputte Datei ergibt einen leeren Verlauf und einen Hinweis in
/// <see cref="Problem"/>; sie bleibt unangetastet, bis der nächste Eintrag dazukommt. Ist die Datei gerade nicht lesbar
/// (gesperrt, keine Berechtigung), wird sie nicht überschrieben – bei der nächsten Änderung wird erneut gelesen und
/// zusammengeführt. Scheitert das Speichern, bleiben die Einträge im Speicher und <see cref="Problem"/> sagt warum.
/// </para>
/// </remarks>
public sealed class HistoryStore
{
    private readonly Lock _gate = new();
    /// <summary>Neueste zuerst; nur unter <see cref="_gate"/> anfassen.</summary>
    private readonly List<HistoryEntry> _entries = [];
    private IReadOnlyList<HistoryEntry> _snapshot = Array.Empty<HistoryEntry>();
    private string? _problem;
    /// <summary>Die Datei war beim Start nicht lesbar; vor dem nächsten Schreiben erneut lesen statt überschreiben.</summary>
    private bool _reloadPending;

    /// <param name="path">Datei; null = <see cref="DefaultPath"/>.</param>
    /// <param name="limit">Höchstzahl der Einträge (mindestens 1); ältere fallen weg.</param>
    /// <param name="persist">false = nur im Speicher, nichts auf der Platte (und eine vorhandene Datei wird nicht gelesen).</param>
    public HistoryStore(string? path = null, int limit = 1000, bool persist = true)
    {
        FilePath = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? DefaultPath : path);
        Limit = Math.Max(1, limit);
        Persist = persist;
        if (!persist) return;

        var result = HistoryFile.Read(FilePath);
        _problem = result.Problem;
        _reloadPending = result.Status == HistoryReadStatus.Unreadable;
        if (result.Entries.Count > 0)
        {
            _entries.AddRange(SortNewestFirst(result.Entries));
            TrimToLimit();
        }
        _snapshot = _entries.ToArray().AsReadOnly();
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "Lupe", "verlauf.json");

    /// <summary>Vollständiger Pfad der Verlaufsdatei (auch wenn <see cref="Persist"/> aus ist).</summary>
    public string FilePath { get; }

    /// <summary>Höchstzahl der Einträge.</summary>
    public int Limit { get; }

    /// <summary>Ob auf der Platte gespeichert wird.</summary>
    public bool Persist { get; }

    /// <summary>
    /// Hinweis für die Oberfläche auf Deutsch, z. B. „Der gespeicherte Verlauf … ist beschädigt …“ oder „… konnte nicht
    /// gespeichert werden …“; null, wenn alles in Ordnung ist. Ein erfolgreiches Speichern setzt ihn zurück.
    /// </summary>
    public string? Problem => Volatile.Read(ref _problem);

    /// <summary>Momentaufnahme, neueste zuerst; ändert sich nicht mehr, auch wenn danach Einträge dazukommen.</summary>
    public IReadOnlyList<HistoryEntry> Entries => Volatile.Read(ref _snapshot);

    /// <summary>Nach jeder Änderung – auf dem Thread, der geändert hat (nicht unbedingt der UI-Thread).</summary>
    public event EventHandler? Changed;

    /// <summary>Fügt einen Eintrag ein (nach Zeit einsortiert, gleich alte: der neue zuerst) und speichert.</summary>
    public void Add(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            RetryPendingLoad();
            var index = 0;
            while (index < _entries.Count && _entries[index].Time > entry.Time) index++;
            _entries.Insert(index, entry);
            TrimToLimit();
            Commit();
        }
        OnChanged();
    }

    /// <summary>Entfernt den Eintrag (dieselbe Instanz, sonst den ersten wertgleichen); nicht vorhanden = nichts passiert.</summary>
    public void Remove(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        bool changed;
        lock (_gate)
        {
            changed = RetryPendingLoad();
            var index = _entries.FindIndex(e => ReferenceEquals(e, entry));
            if (index < 0) index = _entries.IndexOf(entry);
            if (index >= 0)
            {
                _entries.RemoveAt(index);
                changed = true;
            }
            if (changed) Commit();
        }
        if (changed) OnChanged();
    }

    /// <summary>Löscht alles – auch eine kaputte oder gerade nicht lesbare Datei wird durch einen leeren Verlauf ersetzt.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _reloadPending = false;
            _entries.Clear();
            Commit();
        }
        OnChanged();
    }

    /// <summary>CSV (UTF-8 mit BOM, Semikolon wie Excel in DE): Zeit;Format;Quelle;Inhalt.</summary>
    /// <remarks>
    /// Neueste zuerst, Zeit in Ortszeit, Felder korrekt in Anführungszeichen, Formel-Anfänge (= + - @ …) mit ' entschärft.
    /// Atomar geschrieben. Klappt es nicht (Ordner fehlt, keine Berechtigung, Datei in Excel offen), kommt eine
    /// <see cref="IOException"/> mit deutscher Meldung.
    /// </remarks>
    public void ExportCsv(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = HistoryCsv.Build(Entries);
        try
        {
            AtomicFile.Write(path, bytes);
        }
        catch (Exception ex) when (FileProblems.IsExpected(ex) || ex is ArgumentException)
        {
            throw new IOException($"Der Verlauf konnte nicht nach „{path}“ exportiert werden ({FileProblems.Describe(ex)}).", ex);
        }
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Liest eine beim Start nicht lesbare Datei erneut und führt sie mit dem Speicher zusammen. true = Einträge kamen dazu.</summary>
    private bool RetryPendingLoad()
    {
        if (!_reloadPending) return false;
        var result = HistoryFile.Read(FilePath);
        if (result.Status == HistoryReadStatus.Unreadable)
        {
            Volatile.Write(ref _problem, result.Problem);
            return false;
        }

        _reloadPending = false;
        Volatile.Write(ref _problem, result.Problem);
        if (result.Entries.Count == 0) return false;

        var merged = SortNewestFirst([.. _entries, .. result.Entries]);
        _entries.Clear();
        _entries.AddRange(merged);
        TrimToLimit();
        return true;
    }

    /// <summary>Veröffentlicht die neue Momentaufnahme und speichert (außer die Datei ist noch nicht wieder lesbar).</summary>
    private void Commit()
    {
        Volatile.Write(ref _snapshot, _entries.ToArray().AsReadOnly());
        if (!Persist || _reloadPending) return;
        try
        {
            HistoryFile.Write(FilePath, _entries);
            Volatile.Write(ref _problem, null);
        }
        catch (Exception ex) when (FileProblems.IsExpected(ex))
        {
            Volatile.Write(ref _problem,
                $"Der Verlauf konnte nicht in „{FilePath}“ gespeichert werden ({FileProblems.Describe(ex)}). "
                + "Die Einträge bleiben erhalten, solange Lupe läuft; beim nächsten Eintrag wird es erneut versucht.");
        }
    }

    private void TrimToLimit()
    {
        if (_entries.Count > Limit) _entries.RemoveRange(Limit, _entries.Count - Limit);
    }

    /// <summary>Stabil: gleich alte Einträge behalten ihre Reihenfolge.</summary>
    private static List<HistoryEntry> SortNewestFirst(IEnumerable<HistoryEntry> entries) =>
        entries.OrderByDescending(e => e.Time).ToList();
}
