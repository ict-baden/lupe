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
}

/// <summary>
/// Die Treffer-Pipeline: Ton, Verlauf und Aktionen laut Einstellungen (Kopieren, Eintippen, Link öffnen).
/// Bei mehreren Codes in einem Bild kommen alle in den Verlauf, die Aktionen laufen erst für den gewählten.
/// </summary>
internal sealed class Hits(AppState state)
{
    // Verlaufseinträge mehrerer Codes, deren Aktionen noch ausstehen – nach der Auswahl wird der Eintrag ersetzt
    private readonly Dictionary<DecodedCode, HistoryEntry> _pending = [];

    /// <summary>Aktionen, die für eine Quelle gelten. Eintippen nur beim Bildschirm-Scan: Sonst ist Lupe selbst vorn.</summary>
    public ResultActions ActionsFor(ScanSource source) => source == ScanSource.Bildschirm
        ? state.Settings.ScreenActions
        : state.Settings.Actions & ~ResultActions.Eintippen;

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
            AddHistory(codes[0], source, time, outcome.Done);
            return new HitReport(codes, outcome, time);
        }

        _pending.Clear();
        foreach (var code in codes) _pending[code] = AddHistory(code, source, time, ResultActions.Keine);
        return new HitReport(codes, ActionOutcome.None, time);
    }

    /// <summary>Aus mehreren Codes gewählt: Aktionen ausführen und den Verlaufseintrag ergänzen.</summary>
    public async Task<ActionOutcome> ChooseAsync(DecodedCode code, ScanSource source, nint typeTarget = 0)
    {
        var outcome = await RunActionsAsync(code, source, typeTarget);
        if (outcome.Done != ResultActions.Keine && _pending.Remove(code, out var entry))
        {
            try
            {
                state.History.Remove(entry);
                state.History.Add(entry with { ActionsDone = outcome.Done });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                state.Report(NoticeKind.Warning, $"Verlauf: {ex.Message}");
            }
        }
        return outcome;
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
            if (typeTarget == 0) problems.Add("Nicht eingetippt: Der Scan kam aus Lupe selbst – Eintippen geht über das Tastenkürzel aus einem anderen Programm.");
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

    private HistoryEntry AddHistory(DecodedCode code, ScanSource source, DateTimeOffset time, ResultActions done)
    {
        var entry = new HistoryEntry { Time = time, Text = code.Text, Format = code.Format, Source = source, ActionsDone = done };
        try { state.History.Add(entry); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            state.Report(NoticeKind.Warning, $"Verlauf: {ex.Message}");
        }
        return entry;
    }
}

/// <summary>Ein Code für Listen und Ergebnisleisten (Kamera, Bildschirm, Datei, Auswahl).</summary>
public sealed class CodeItem
{
    public CodeItem(DecodedCode code, ScanSource source, DateTimeOffset time, Uri? link, int number = 0)
    {
        Code = code;
        Source = source;
        Time = time;
        Link = link;
        Number = number;
    }

    public DecodedCode Code { get; }
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
