using Avalonia.Controls;
using Avalonia.Threading;
using Lupe.Core.Resources;
using Lupe.Services;

namespace Lupe.UI;

/// <summary>Eine Seite rechts im Hauptfenster.</summary>
internal interface IPage
{
    /// <summary>Seite wird sichtbar (auch: Fenster wieder eingeblendet).</summary>
    void OnShown();

    /// <summary>Seite wird verlassen oder das Fenster ausgeblendet (Kamera aus).</summary>
    void OnHidden();

    /// <summary>Definierten Zustand für <c>--screenshots</c> herstellen und warten, bis alles geladen ist.</summary>
    Task PrepareScreenshotAsync();
}

internal static class Ui
{
    /// <summary>Eigenschaft an eine Ressource binden (folgt Hell/Dunkel) – wie SetResourceReference in WPF.</summary>
    public static void SetResource(this Avalonia.StyledElement element, Avalonia.AvaloniaProperty property, string key) =>
        element.Bind(property, element.GetResourceObservable(key));

    /// <summary>
    /// Startet Arbeit aus einem Ereignis heraus. Fehler werden als Meldung gezeigt statt abzustürzen – außer im
    /// Screenshot-Modus, dort sollen sie den Lauf sichtbar abbrechen.
    /// </summary>
    public static async void Run(AppState state, Func<Task> work, string failure)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (!state.Strict)
        {
            state.Report(NoticeKind.Error, string.Format(Strings.App_Ui_Failure, failure, ex.Message));
        }
    }

    /// <summary>Wartet, bis Layout und anstehende Arbeit erledigt sind (für Screenshots).</summary>
    public static async Task IdleAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }
}

/// <summary>Führt eine Aktion erst aus, wenn eine Weile keine neue Eingabe kam (Suchfelder, Link-Regeln).</summary>
internal sealed class Debouncer
{
    private readonly DispatcherTimer _timer;
    private Action? _action;

    public Debouncer(TimeSpan delay)
    {
        _timer = new DispatcherTimer { Interval = delay };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            _action?.Invoke();
        };
    }

    public void Run(Action action)
    {
        _action = action;
        _timer.Stop();
        _timer.Start();
    }

    public bool IsPending => _timer.IsEnabled;

    /// <summary>Ausstehende Aktion sofort ausführen (z. B. beim Verlassen der Seite).</summary>
    public void Flush()
    {
        if (!_timer.IsEnabled) return;
        _timer.Stop();
        _action?.Invoke();
    }
}

/// <summary>Kopieren und Link öffnen aus Ergebniszeilen – mit Meldung statt Ausnahme.</summary>
internal static class CodeActions
{
    public static async Task CopyAsync(AppState state, CodeItem item)
    {
        if (await CopyAsync(state, item.Text)) state.Hits.Record(item, Core.Model.ResultActions.Kopieren);
    }

    public static void Open(AppState state, CodeItem item)
    {
        if (item.Link is { } link && Open(state, link)) state.Hits.Record(item, Core.Model.ResultActions.LinkOeffnen);
    }

    /// <summary>Kopiert und meldet es; false bei einem Fehler (dann mit Fehlermeldung).</summary>
    public static async Task<bool> CopyAsync(AppState state, string text)
    {
        if (await Shell.CopyAsync(text) is { } error)
        {
            state.Report(NoticeKind.Error, error);
            return false;
        }
        state.Report(NoticeKind.Success, string.Format(Strings.App_Notice_Copied, Labels.OneLine(text, 90)));
        return true;
    }

    /// <summary>Öffnet den Link im Browser; false bei einem Fehler (dann mit Hinweis).</summary>
    public static bool Open(AppState state, Uri link)
    {
        if (Shell.OpenLink(link) is not { } error) return true;
        state.Report(NoticeKind.Warning, error);
        return false;
    }
}
