using Lupe.Core.Model;

namespace Lupe.Services;

/// <summary>Was die Aktionen zu einem Code bewirkt haben – und was nicht ging (deutsch, für die Meldung).</summary>
internal sealed record ActionOutcome(ResultActions Done, IReadOnlyList<string> Problems)
{
    public static ActionOutcome None { get; } = new(ResultActions.Keine, []);

    /// <summary>„Kopiert · Eingetippt“ und Probleme, für Hinweise.</summary>
    public string Summary
    {
        get
        {
            var done = Labels.Actions(Done);
            var problems = string.Join(" ", Problems);
            return done.Length == 0 ? problems : problems.Length == 0 ? done : $"{done}. {problems}";
        }
    }
}

/// <summary>Ergebnis einer Meldung neuer Treffer.</summary>
internal sealed record HitReport(IReadOnlyList<DecodedCode> Codes, ActionOutcome Outcome, DateTimeOffset Time)
{
    /// <summary>Mehrere Codes: Die Aktionen warten auf die Auswahl des Benutzers.</summary>
    public bool NeedsChoice => Codes.Count > 1;

    /// <summary>
    /// Die Verlaufseinträge zu <see cref="Codes"/> (gleiche Reihenfolge, null = keiner). Sie gehören zu dieser Meldung – ein
    /// Treffer der Kamera, während die Auswahl eines Bildschirm-Scans offen ist, kommt ihnen so nicht in die Quere. Ein Array,
    /// weil <see cref="Hits.ChooseAsync"/> den Eintrag des gewählten Codes durch den ergänzten ersetzt.
    /// </summary>
    public HistoryEntry?[] Entries { get; init; } = [];

    /// <summary>Der Verlaufseintrag zum Code an Stelle <paramref name="index"/>, sonst null.</summary>
    public HistoryEntry? EntryAt(int index) => index >= 0 && index < Entries.Length ? Entries[index] : null;
}

/// <summary>
/// Die Treffer-Pipeline: Ton, Verlauf und Aktionen laut Einstellungen (Kopieren, Eintippen, Link öffnen).
/// Bei mehreren Codes in einem Bild kommen alle in den Verlauf, die Aktionen laufen erst für den gewählten.
/// </summary>
internal sealed class Hits(AppState state)
{
    /// <summary>
    /// Aktionen, die für eine Quelle gelten. Eintippen beim Bildschirm-Scan (in das Fenster von vorher) und bei der Kamera
    /// (wie ein Handscanner in das Programm vorn); bei Datei und Zwischenablage nicht – dort ist Lupe selbst vorn.
    /// </summary>
    public ResultActions ActionsFor(ScanSource source) => source switch
    {
        ScanSource.Bildschirm => state.Settings.ScreenActions,
        // Wie ein Handscanner: tippt in das Programm, das gerade vorn ist
        ScanSource.Kamera => state.Settings.Actions,
        // Datei und Zwischenablage holt man in Lupe selbst – das Ziel wäre Lupe
        _ => state.Settings.Actions & ~ResultActions.Eintippen,
    };

    /// <summary>
    /// Neue Treffer: Ton, Verlauf und – bei genau einem Code – die Aktionen. <paramref name="typeTarget"/> ist das
    /// Fenster, in das „Eintippen“ schreibt (0 = keins).
    /// </summary>
    public async Task<HitReport> ReportAsync(IReadOnlyList<DecodedCode> codes, ScanSource source, nint typeTarget = 0)
    {
        var time = DateTimeOffset.Now;
        if (codes.Count == 0) return new HitReport(codes, ActionOutcome.None, time);
        if (state.Settings.Sound && !state.Strict) Beep.Play();

        if (codes.Count == 1)
        {
            var outcome = await RunActionsAsync(codes[0], source, typeTarget);
            var single = AddHistory(codes[0], source, time, outcome.Done);
            return new HitReport(codes, outcome, time) { Entries = [single] };
        }

        var entries = codes.Select(code => AddHistory(code, source, time, ResultActions.Keine)).ToArray();
        return new HitReport(codes, ActionOutcome.None, time) { Entries = entries };
    }

    /// <summary>Aus den Codes einer Meldung gewählt: Aktionen ausführen und den Verlaufseintrag des gewählten ergänzen.</summary>
    public async Task<ActionOutcome> ChooseAsync(HitReport report, DecodedCode code, ScanSource source, nint typeTarget = 0)
    {
        var outcome = await RunActionsAsync(code, source, typeTarget);
        // Dieselbe Instanz wie in der Meldung (Auswahlfenster) – wertgleich nur als Notbehelf
        int index = report.Codes.Select((c, i) => ReferenceEquals(c, code) ? i : -1).FirstOrDefault(i => i >= 0, -1);
        if (index < 0) index = report.Codes.Select((c, i) => c == code ? i : -1).FirstOrDefault(i => i >= 0, -1);
        if (report.EntryAt(index) is { } entry && Record(entry, outcome.Done) is { } updated) report.Entries[index] = updated;
        return outcome;
    }

    /// <summary>
    /// Eine Aktion aus einer Ergebniszeile (Knopf „Kopieren“ oder „Link öffnen“ auf den Seiten Kamera, Bildschirm und
    /// Datei, im Hinweis nach dem Bildschirm-Scan) im Verlaufseintrag des Codes vermerken.
    /// </summary>
    public void Record(CodeItem item, ResultActions done)
    {
        if (item.Entry is { } entry && Record(entry, done) is { } updated) item.Entry = updated;
    }

    /// <summary>Ergänzt <see cref="HistoryEntry.ActionsDone"/> an Ort und Stelle; der neue Eintrag, oder null, wenn nichts zu tun war.</summary>
    private HistoryEntry? Record(HistoryEntry entry, ResultActions done)
    {
        if (done == ResultActions.Keine || (entry.ActionsDone & done) == done) return null;
        var updated = entry with { ActionsDone = entry.ActionsDone | done };
        try
        {
            // Nicht mehr im Verlauf (gelöscht, über das Limit gefallen): dann gibt es nichts nachzutragen
            return state.History.Replace(entry, updated) ? updated : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            state.Report(NoticeKind.Warning, $"Verlauf: {ex.Message}");
            return null;
        }
    }

    /// <summary>Die Aktionen laut Einstellungen für einen Code. Reihenfolge: kopieren, tippen, zuletzt der Browser (er nimmt den Fokus).</summary>
    public async Task<ActionOutcome> RunActionsAsync(DecodedCode code, ScanSource source, nint typeTarget)
    {
        if (state.Strict) return ActionOutcome.None;
        var wanted = ActionsFor(source);
        var done = ResultActions.Keine;
        var problems = new List<string>();

        if (wanted.HasFlag(ResultActions.Kopieren))
        {
            if (Shell.Copy(code.Text) is { } error) problems.Add(error);
            else done |= ResultActions.Kopieren;
        }
        if (wanted.HasFlag(ResultActions.Eintippen))
        {
            if (typeTarget == 0) problems.Add(source == ScanSource.Kamera
                ? "Nicht eingetippt: Lupe ist gerade das aktive Fenster – erst das Zielprogramm anklicken, Lupe kann dahinter oder minimiert weiterlaufen."
                : "Nicht eingetippt: Der Scan kam aus Lupe selbst – Eintippen geht über das Tastenkürzel aus einem anderen Programm.");
            else if (await Typing.TypeIntoAsync(typeTarget, code.Text, state.Settings.TypeSuffix) is { } error) problems.Add(error);
            else done |= ResultActions.Eintippen;
        }
        if (wanted.HasFlag(ResultActions.LinkOeffnen) && state.LinkFor(code.Text) is { } link)
        {
            if (Shell.OpenLink(link) is { } error) problems.Add(error);
            else done |= ResultActions.LinkOeffnen;
        }
        return new ActionOutcome(done, problems);
    }

    /// <summary>Alle Codes zeilenweise kopieren. Fehlermeldung oder null.</summary>
    public string? CopyAll(IEnumerable<DecodedCode> codes) => Shell.Copy(string.Join(Environment.NewLine, codes.Select(c => c.Text)));

    /// <summary>Alle Codes einer Ergebnisliste zeilenweise kopieren und das in ihren Verlaufseinträgen vermerken. Fehlermeldung oder null.</summary>
    public string? CopyAll(IReadOnlyList<CodeItem> items)
    {
        var error = CopyAll(items.Select(i => i.Code));
        if (error is null) foreach (var item in items) Record(item, ResultActions.Kopieren);
        return error;
    }

    /// <summary>Alle Codes einer Meldung zeilenweise kopieren (Auswahlfenster) und das im Verlauf vermerken. Fehlermeldung oder null.</summary>
    public string? CopyAll(HitReport report)
    {
        var error = CopyAll(report.Codes);
        if (error is null)
            for (int i = 0; i < report.Entries.Length; i++)
                if (report.Entries[i] is { } entry && Record(entry, ResultActions.Kopieren) is { } updated) report.Entries[i] = updated;
        return error;
    }

    private HistoryEntry? AddHistory(DecodedCode code, ScanSource source, DateTimeOffset time, ResultActions done)
    {
        var entry = new HistoryEntry { Time = time, Text = code.Text, Format = code.Format, Source = source, ActionsDone = done };
        try { state.History.Add(entry); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            state.Report(NoticeKind.Warning, $"Verlauf: {ex.Message}");
            return null;
        }
        return entry;
    }
}

/// <summary>Ein Code für Listen und Ergebnisleisten (Kamera, Bildschirm, Datei, Auswahl).</summary>
public sealed class CodeItem
{
    internal CodeItem(DecodedCode code, ScanSource source, DateTimeOffset time, Uri? link, int number = 0, HistoryEntry? entry = null)
    {
        Code = code;
        Source = source;
        Time = time;
        Link = link;
        Number = number;
        Entry = entry;
    }

    public DecodedCode Code { get; }

    /// <summary>
    /// Der Verlaufseintrag dieses Treffers (null: keiner, z. B. weil er sich nicht anlegen ließ). Kopieren oder Link öffnen aus
    /// der Zeile wird darin vermerkt; danach zeigt die Eigenschaft auf den ergänzten Eintrag.
    /// </summary>
    internal HistoryEntry? Entry { get; set; }
    public ScanSource Source { get; }
    public DateTimeOffset Time { get; }
    public Uri? Link { get; }
    /// <summary>Nummer der Markierung im Bild (1, 2, …), 0 = keine.</summary>
    public int Number { get; }

    public string Text => Code.Text;
    public string Format => Code.Format;
    public string FormatLabel => Labels.Format(Code.Format);
    public string OneLine => Labels.OneLine(Code.Text);
    public bool HasLink => Link is not null;
    public string LinkText => Link?.AbsoluteUri ?? "";
    public string NumberText => Number > 0 ? Number.ToString(Labels.German) : "";
    public string TimeText => Labels.ShortTime(Time, DateTimeOffset.Now);
    public string SourceLabel => Labels.Source(Source);
    public string ToolTipText => Link is null ? Code.Text : $"{Code.Text}\n→ {Link.AbsoluteUri}";
}
