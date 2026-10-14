using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
            state.Report(NoticeKind.Error, $"{failure}: {ex.Message}");
        }
    }

    /// <summary>Wartet, bis WPF Layout und Rendern erledigt hat (für Screenshots).</summary>
    public static async Task IdleAsync()
    {
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    public static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            int n = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(current, i);
                if (child is T t) return t;
                queue.Enqueue(child);
            }
        }
        return null;
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
    /// <summary>Bindet <see cref="CodeCommands"/> an ein Element (Seite oder Fenster).</summary>
    public static void Bind(UIElement target, AppState state)
    {
        target.CommandBindings.Add(new CommandBinding(CodeCommands.Copy,
            (_, e) => { if (e.Parameter is CodeItem item) Copy(state, item.Text); }));
        target.CommandBindings.Add(new CommandBinding(CodeCommands.OpenLink,
            (_, e) => { if (e.Parameter is CodeItem { Link: { } link }) Open(state, link); },
            (_, e) => e.CanExecute = e.Parameter is CodeItem { HasLink: true }));
    }

    public static void Copy(AppState state, string text)
    {
        if (Shell.Copy(text) is { } error) state.Report(NoticeKind.Error, error);
        else state.Report(NoticeKind.Success, $"Kopiert: {Labels.OneLine(text, 90)}");
    }

    public static void Open(AppState state, Uri link)
    {
        if (Shell.OpenLink(link) is { } error) state.Report(NoticeKind.Warning, error);
    }
}
