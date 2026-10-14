using System.Windows;
using Lupe.Core.Model;
using Lupe.Core.Screen;
using Lupe.Interop;
using Lupe.Services;

namespace Lupe.UI;

/// <summary>
/// Der Bildschirm-Scan: Lupe-Fenster ausblenden → Standbild aller Monitore → Auswahl-Overlay → Erkennung im
/// Ausschnitt → Treffer-Pipeline. „Eintippen“ geht in das Fenster, das vor dem Scan vorn war; kam der Scan aus Lupe
/// selbst (Knopf), wird nicht getippt – das Ziel wäre sonst irgendein Fenster.
/// </summary>
internal sealed class ScreenScan(AppState state, Func<MainWindow?> mainWindow)
{
    private const int MaxRecent = 8;
    private bool _busy;

    public bool IsBusy => _busy;

    public async Task RunAsync()
    {
        // Tastenkürzel während eines laufenden Scans (Overlay offen) nicht doppelt auslösen
        if (_busy) return;
        _busy = true;
        try { await RunCoreAsync(); }
        finally { _busy = false; }
    }

    private async Task RunCoreAsync()
    {
        var main = mainWindow();
        var foreground = Native.GetForegroundWindow();
        bool fromLupe = Native.IsOwnWindow(foreground);
        nint typeTarget = fromLupe ? 0 : foreground;
        bool hidden = false;
        try
        {
            // Ein Hinweis vom letzten Scan (immer oben) gehört nicht ins Standbild
            bool toastClosed = ResultToast.CloseOpen();
            if (main is { IsVisible: true } && main.WindowState != WindowState.Minimized)
            {
                main.Hide();
                hidden = true;
            }
            if (hidden || toastClosed)
            {
                // Ausblenden ohne Animation (MainWindow), trotzdem ein Bild abwarten, bis DWM es wirklich entfernt hat
                await Task.Delay(40);
                Native.DwmFlush();
                await Task.Delay(40);
            }
            await ScanAsync(main, hidden, fromLupe, typeTarget);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && !state.Strict)
        {
            // Was auch schiefgeht: Das ausgeblendete Lupe-Fenster kommt wieder, und die Meldung erscheint dort, wo man
            // sie sieht – im Fenster nur, wenn der Scan von dort kam
            Restore(main, hidden, activate: fromLupe);
            var message = $"Der Bildschirm-Scan ist fehlgeschlagen: {ex.Message}";
            if (fromLupe && main is { IsVisible: true }) state.Report(NoticeKind.Error, message);
            else new ResultToast(state, null, "Bildschirm-Scan nicht möglich", "", message, null).Present();
        }
    }

    private async Task ScanAsync(MainWindow? main, bool hidden, bool fromLupe, nint typeTarget)
    {
        PixelRect area;
        IReadOnlyList<MonitorArea> monitors;
        PixelImage desktop;
        try
        {
            area = ScreenCapture.VirtualScreen;
            monitors = ScreenCapture.Monitors();
            desktop = await Task.Run(() => ScreenCapture.Capture(area));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Restore(main, hidden, activate: true);
            var message = $"Das Bildschirmfoto ist fehlgeschlagen: {ex.Message}";
            if (main is { IsVisible: true }) state.Report(NoticeKind.Error, message);
            else new ResultToast(state, null, "Bildschirm-Scan nicht möglich", "", message, null).Present();
            return;
        }

        var selection = await new SelectionOverlay(desktop, area, monitors).SelectAsync();
        if (selection is not { } chosenArea)
        {
            Restore(main, hidden, activate: fromLupe);
            ReturnFocus(typeTarget);
            return;
        }

        int centerX = chosenArea.X + chosenArea.Width / 2, centerY = chosenArea.Y + chosenArea.Height / 2;
        var monitor = MonitorAt(monitors, centerX, centerY);
        var crop = Images.Crop(desktop, chosenArea.X - area.X, chosenArea.Y - area.Y, chosenArea.Width, chosenArea.Height);
        IReadOnlyList<DecodedCode> codes = [];
        string? failure = null;
        if (crop is not null)
        {
            var decoder = state.CreateDecoder();
            try { codes = await Task.Run(() => decoder.Decode(crop)); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { failure = ex.Message; }
        }

        // Fenster wieder da – ohne Fokus, wenn gleich in ein anderes Fenster getippt wird
        Restore(main, hidden, activate: fromLupe);
        // Ergebnis im Fenster melden, wenn der Scan von dort kam und es zu sehen ist – sonst (Tastenkürzel, Infobereich)
        // als Hinweis unten rechts
        bool inWindow = fromLupe && main is { IsVisible: true } && main.WindowState != WindowState.Minimized;

        if (codes.Count == 0)
        {
            const string title = "Kein Code gefunden";
            var detail = failure is null ? "Im Ausschnitt hat Lupe keinen QR- oder Barcode erkannt." : $"Die Erkennung ist fehlgeschlagen: {failure}";
            if (inWindow) state.Report(NoticeKind.Warning, $"{detail} Tipp: Rechteck etwas größer ziehen oder die Ansicht vergrößern.");
            else
            {
                ReturnFocus(typeTarget);
                new ResultToast(state, null, title, "", failure is null ? "" : detail, monitor).Present();
            }
            return;
        }

        var report = await state.Hits.ReportAsync(codes, ScanSource.Bildschirm, typeTarget);
        var outcome = report.Outcome;
        var chosen = codes[0];
        if (report.NeedsChoice)
        {
            var items = codes.Select((c, i) => new CodeItem(c, ScanSource.Bildschirm, report.Time, state.LinkFor(c.Text), i + 1)).ToList();
            var window = new CodeChoiceWindow(items, ActionsText(typeTarget != 0), (centerX, centerY), monitor);
            var (result, code) = await window.ChooseAsync();
            AddRecent(codes, report.Time);
            if (result == CodeChoiceWindow.Outcome.Cancelled || code is null)
            {
                if (result == CodeChoiceWindow.Outcome.CopyAll)
                {
                    var error = state.Hits.CopyAll(codes);
                    Finish(inWindow, typeTarget, monitor, null, $"{codes.Count} Codes zeilenweise kopiert", error is null ? ResultActions.Kopieren : ResultActions.Keine, error ?? "");
                }
                else ReturnFocus(typeTarget);
                return;
            }
            chosen = code;
            outcome = await state.Hits.ChooseAsync(report, code, ScanSource.Bildschirm, typeTarget);
        }
        else AddRecent(codes, report.Time);

        var item = new CodeItem(chosen, ScanSource.Bildschirm, report.Time, state.LinkFor(chosen.Text));
        var titleText = codes.Count > 1 ? $"{item.FormatLabel} gewählt ({codes.Count} gefunden)" : $"{item.FormatLabel} erkannt";
        Finish(inWindow, typeTarget, monitor, item, titleText, outcome.Done, string.Join(" ", outcome.Problems));
    }

    /// <summary>Ergebnis zeigen: in Lupe als Meldung, sonst als Hinweis unten rechts (ohne Fokus).</summary>
    private void Finish(bool inWindow, nint typeTarget, MonitorArea? monitor, CodeItem? item, string title, ResultActions actions, string problems)
    {
        var done = Labels.Actions(actions);
        if (inWindow)
        {
            var text = item is null ? title : $"{title}: {Labels.OneLine(item.Text, 80)}";
            if (done.Length > 0) text += $" – {done}";
            if (problems.Length > 0) text += $". {problems}";
            state.Report(problems.Length > 0 ? NoticeKind.Warning : NoticeKind.Success, text);
            return;
        }
        // Wurde nicht getippt, dem vorherigen Fenster den Fokus zurückgeben (das Overlay hatte ihn)
        if (!actions.HasFlag(ResultActions.Eintippen) && !actions.HasFlag(ResultActions.LinkOeffnen)) ReturnFocus(typeTarget);
        new ResultToast(state, item, title, done, problems, monitor).Present();
    }

    private string ActionsText(bool canType)
    {
        var actions = state.Hits.ActionsFor(ScanSource.Bildschirm);
        var parts = new List<string>();
        if (actions.HasFlag(ResultActions.Kopieren)) parts.Add("kopiert");
        if (actions.HasFlag(ResultActions.Eintippen) && canType) parts.Add("in das vorherige Fenster getippt");
        if (actions.HasFlag(ResultActions.LinkOeffnen)) parts.Add("als Link geöffnet (wenn eine Regel passt)");
        return parts.Count switch
        {
            0 => "",
            1 => parts[0],
            _ => string.Join(", ", parts[..^1]) + " und " + parts[^1],
        };
    }

    private void AddRecent(IReadOnlyList<DecodedCode> codes, DateTimeOffset time)
    {
        foreach (var code in codes.Reverse())
            state.ScreenResults.Insert(0, new CodeItem(code, ScanSource.Bildschirm, time, state.LinkFor(code.Text)));
        while (state.ScreenResults.Count > MaxRecent) state.ScreenResults.RemoveAt(state.ScreenResults.Count - 1);
    }

    /// <summary>Das für den Scan ausgeblendete Fenster wieder zeigen (mehrfach aufrufbar).</summary>
    private static void Restore(Window? main, bool hidden, bool activate)
    {
        if (main is null || !hidden || main.IsVisible) return;
        main.ShowActivated = activate;
        main.Show();
        main.ShowActivated = true;
        if (activate) main.Activate();
    }

    private static void ReturnFocus(nint target)
    {
        if (target != 0 && Native.IsWindow(target)) Native.SetForegroundWindow(target);
    }

    private static MonitorArea? MonitorAt(IReadOnlyList<MonitorArea> monitors, int x, int y) =>
        monitors.FirstOrDefault(m => x >= m.Bounds.X && x < m.Bounds.X + m.Bounds.Width && y >= m.Bounds.Y && y < m.Bounds.Y + m.Bounds.Height)
        ?? monitors.FirstOrDefault(m => m.IsPrimary)
        ?? monitors.FirstOrDefault();
}
