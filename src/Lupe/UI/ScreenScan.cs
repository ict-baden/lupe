// SPDX-License-Identifier: MIT
using Avalonia.Controls;
using Lupe.Core.Decoding;
using Lupe.Core.Model;
using Lupe.Core.Platform;
using Lupe.Core.Resources;
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

    /// <param name="external">
    /// Ausgelöst von außen (<c>lupe --bildschirm</c> über ein Tastenkürzel des Desktops): Getippt wird in das Fenster mit dem
    /// Fokus, auch wenn das System nicht sagt, welches vorn ist (Wayland) – dort ist „Lupe-Fenster aktiv“ über XWayland
    /// nicht verlässlich.
    /// </param>
    public async Task RunAsync(bool external = false)
    {
        // Tastenkürzel während eines laufenden Scans (Overlay offen) nicht doppelt auslösen
        if (_busy) return;
        _busy = true;
        try { await RunCoreAsync(external); }
        finally { _busy = false; }
    }

    private async Task RunCoreAsync(bool external)
    {
        var main = mainWindow();
        var active = LupePlatform.Current.ActiveWindow;
        var foreground = active.Foreground();
        // Weiß das System nicht, welches Fenster vorn ist (Wayland): Kam der Scan aus Lupe, ist ihr Fenster aktiv
        bool fromLupe = foreground == 0 ? !external && main is { IsActive: true } : active.IsOwnWindow(foreground);
        // Wayland: kein Fenster bekannt – getippt wird dann in das Fenster, das nach der Auswahl den Fokus hat
        nint typeTarget = fromLupe ? 0 : TypeTargets.From(active, foreground, lupeActive: false);
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
                // Ausblenden ohne Animation (MainWindow), trotzdem ein Bild abwarten, bis es wirklich weg ist
                await Native.WaitForHiddenAsync();
            }
            await ScanAsync(main, hidden, fromLupe, typeTarget);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && !state.Strict)
        {
            // Was auch schiefgeht: Das ausgeblendete Lupe-Fenster kommt wieder, und die Meldung erscheint dort, wo man
            // sie sieht – im Fenster nur, wenn der Scan von dort kam
            Restore(main, hidden, activate: fromLupe);
            var message = string.Format(Strings.App_Scan_Failed, ex.Message);
            if (fromLupe && main is { IsVisible: true }) state.Report(NoticeKind.Error, message);
            else new ResultToast(state, null, Strings.App_Scan_NotPossible, "", message, null).Present();
        }
    }

    private async Task ScanAsync(MainWindow? main, bool hidden, bool fromLupe, nint typeTarget)
    {
        PixelRect area;
        IReadOnlyList<MonitorArea> monitors;
        PixelImage? desktop;
        string? refused = null;
        try
        {
            area = ScreenCapture.VirtualScreen;
            monitors = ScreenCapture.Monitors();
            // TryCapture statt Capture: Lässt Windows kein Bildschirmfoto zu, wird das gemeldet – kein schwarzes Standbild
            desktop = await Task.Run(() => ScreenCapture.TryCapture(area, out var image, out refused) ? image : null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            CaptureFailed(string.Format(Strings.App_Scan_CaptureFailed, ex.Message));
            return;
        }
        if (desktop is null)
        {
            CaptureFailed(refused ?? Strings.App_Scan_CaptureNotPossible);
            return;
        }

        void CaptureFailed(string message)
        {
            Restore(main, hidden, activate: true);
            if (main is { IsVisible: true }) state.Report(NoticeKind.Error, message);
            else new ResultToast(state, null, Strings.App_Scan_NotPossible, "", message, null).Present();
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
        string? unavailable = null;
        if (crop is not null)
        {
            var decoder = state.CreateDecoder();
            try { codes = await Task.Run(() => decoder.Decode(crop)); }
            catch (DecoderUnavailableException ex) { unavailable = ex.Message; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { failure = ex.Message; }
        }

        // Fenster wieder da – ohne Fokus, wenn gleich in ein anderes Fenster getippt wird
        Restore(main, hidden, activate: fromLupe);
        // Ergebnis im Fenster melden, wenn der Scan von dort kam und es zu sehen ist – sonst (Tastenkürzel, Infobereich)
        // als Hinweis unten rechts
        bool inWindow = fromLupe && main is { IsVisible: true } && main.WindowState != WindowState.Minimized;

        if (unavailable is not null)
        {
            // Die Erkennung lädt auf diesem System nicht (Meldung mit Fehlercode): ein Fehler, kein „Kein Code gefunden“
            if (inWindow) state.Report(NoticeKind.Error, unavailable);
            else
            {
                ReturnFocus(typeTarget);
                new ResultToast(state, null, Strings.App_Scan_NotPossible, "", unavailable, monitor).Present();
            }
            return;
        }

        if (codes.Count == 0)
        {
            var title = Strings.App_Scan_NoCode;
            var detail = failure is null ? Strings.App_Scan_NoCodeDetail : string.Format(Strings.App_Scan_RecognitionFailed, failure);
            if (inWindow) state.Report(NoticeKind.Warning, string.Format(Strings.App_Scan_NoCodeTip, detail));
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
            if (result == CodeChoiceWindow.Outcome.Cancelled || code is null)
            {
                if (result == CodeChoiceWindow.Outcome.CopyAll)
                {
                    var error = await state.Hits.CopyAllAsync(report);
                    AddRecent(report);
                    Finish(inWindow, typeTarget, monitor, null, string.Format(Strings.App_Scan_CopiedAll, codes.Count), error is null ? ResultActions.Kopieren : ResultActions.Keine, error ?? "");
                }
                else
                {
                    AddRecent(report);
                    ReturnFocus(typeTarget);
                }
                return;
            }
            chosen = code;
            outcome = await state.Hits.ChooseAsync(report, code, ScanSource.Bildschirm, typeTarget);
        }
        AddRecent(report);

        // Dieselbe Zeile wie oben in „Zuletzt vom Bildschirm“: Kopieren im Hinweis und in der Liste landen im selben Verlaufseintrag
        var index = codes.Select((c, i) => ReferenceEquals(c, chosen) ? i : -1).FirstOrDefault(i => i >= 0, 0);
        var item = index < state.ScreenResults.Count && ReferenceEquals(state.ScreenResults[index].Code, chosen)
            ? state.ScreenResults[index]
            : new CodeItem(chosen, ScanSource.Bildschirm, report.Time, state.LinkFor(chosen.Text), entry: report.EntryAt(index));
        var titleText = codes.Count > 1 ? string.Format(Strings.App_Scan_Chosen, item.FormatLabel, codes.Count) : string.Format(Strings.App_Scan_Recognized, item.FormatLabel);
        Finish(inWindow, typeTarget, monitor, item, titleText, outcome.Done, string.Join(" ", outcome.Problems));
    }

    /// <summary>Ergebnis zeigen: in Lupe als Meldung, sonst als Hinweis unten rechts (ohne Fokus).</summary>
    private void Finish(bool inWindow, nint typeTarget, MonitorArea? monitor, CodeItem? item, string title, ResultActions actions, string problems)
    {
        var done = Labels.Actions(actions);
        if (inWindow)
        {
            var text = item is null ? title : string.Format(Strings.App_Scan_ResultWithContent, title, Labels.OneLine(item.Text, 80));
            if (done.Length > 0) text = string.Format(Strings.App_Scan_ResultWithDone, text, done);
            if (problems.Length > 0) text = string.Format(Strings.App_Scan_ResultWithProblems, text, problems);
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
        if (actions.HasFlag(ResultActions.Kopieren)) parts.Add(Strings.App_Choice_ActionCopied);
        if (actions.HasFlag(ResultActions.Eintippen) && canType) parts.Add(Strings.App_Choice_ActionTyped);
        if (actions.HasFlag(ResultActions.LinkOeffnen)) parts.Add(Strings.App_Choice_ActionLinkOpened);
        return parts.Count switch
        {
            0 => "",
            1 => parts[0],
            _ => string.Format(Strings.App_List_And, string.Join(Strings.App_List_Separator, parts[..^1]), parts[^1]),
        };
    }

    /// <summary>Die Codes des Scans oben in „Zuletzt vom Bildschirm“ – mit ihren Verlaufseinträgen (Kopieren dort wird vermerkt).</summary>
    private void AddRecent(HitReport report)
    {
        for (int i = report.Codes.Count - 1; i >= 0; i--)
        {
            var code = report.Codes[i];
            state.ScreenResults.Insert(0, new CodeItem(code, ScanSource.Bildschirm, report.Time, state.LinkFor(code.Text), entry: report.EntryAt(i)));
        }
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
        if (target == 0) return;
        _ = LupePlatform.Current.ActiveWindow.ActivateAsync(target);
    }

    private static MonitorArea? MonitorAt(IReadOnlyList<MonitorArea> monitors, int x, int y) =>
        monitors.FirstOrDefault(m => x >= m.Bounds.X && x < m.Bounds.X + m.Bounds.Width && y >= m.Bounds.Y && y < m.Bounds.Y + m.Bounds.Height)
        ?? monitors.FirstOrDefault(m => m.IsPrimary)
        ?? monitors.FirstOrDefault();
}
