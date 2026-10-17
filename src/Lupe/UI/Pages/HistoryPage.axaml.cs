using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Lupe.Core.Model;
using Lupe.Services;

namespace Lupe.UI.Pages;

/// <summary>Eine Zeile im Verlauf.</summary>
public sealed class HistoryRow(HistoryEntry entry, Uri? link)
{
    public HistoryEntry Entry { get; } = entry;
    public Uri? Link { get; } = link;
    public DateTimeOffset Time => Entry.Time;
    public string TimeText => Labels.Time(Entry.Time);
    public string SourceLabel => Labels.Source(Entry.Source);
    public string SourceGlyph => Labels.SourceGlyph(Entry.Source);
    public string FormatLabel => Labels.Format(Entry.Format);
    public string Text => Entry.Text;
    public string OneLine => Labels.OneLine(Entry.Text);
    public bool HasLink => Link is not null;
    public string LinkText => Link is null ? "" : $"Link: {Link.AbsoluteUri}";

    public bool Matches(string search) =>
        Entry.Text.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || FormatLabel.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || Entry.Format.Contains(search, StringComparison.OrdinalIgnoreCase)
        || SourceLabel.Contains(search, StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>Verlauf: Liste mit Suche, Sortieren, Kopieren, Link öffnen, Löschen, „Alles löschen“ und CSV-Export.</summary>
public partial class HistoryPage : UserControl, IPage
{
    private readonly AppState _state;
    private readonly Debouncer _search = new(TimeSpan.FromMilliseconds(200));
    private readonly Dictionary<string, (Button Button, string Title)> _headers;
    private bool _dirty = true;
    private bool _visible;
    // Gewählte Sortierung (Eigenschaft der Zeile) – null: neueste zuerst wie gespeichert
    private string? _sortBy;
    private bool _descending;

    internal HistoryPage(AppState state)
    {
        InitializeComponent();
        _state = state;
        _headers = new[] { SortTime, SortSource, SortFormat, SortText }.ToDictionary(b => (string)b.Tag!, b => (b, (string)b.Content!));
        state.HistoryChanged += OnHistoryChanged;
        state.SettingsChanged += (old, now) =>
        {
            if (old.KeepHistory != now.KeepHistory || !old.LinkRules.SequenceEqual(now.LinkRules)) OnHistoryChanged();
        };
    }

    public void OnShown()
    {
        _visible = true;
        if (_dirty) Refresh();
    }

    public void OnHidden() => _visible = false;

    private void OnHistoryChanged()
    {
        // Nur neu aufbauen, wenn man die Seite sieht – die Kamera meldet Treffer auch nebenher
        _dirty = true;
        if (_visible) Refresh();
    }

    private void Refresh()
    {
        _dirty = false;
        var selected = (HistoryList.SelectedItem as HistoryRow)?.Entry;
        var all = _state.History.Entries;
        var search = (SearchBox.Text ?? "").Trim();
        IEnumerable<HistoryRow> rows = all.Select(e => new HistoryRow(e, _state.LinkFor(e.Text)))
                                          .Where(r => search.Length == 0 || r.Matches(search));
        rows = Sorted(rows);
        var list = rows.ToList();
        HistoryList.ItemsSource = list;
        if (selected is not null) HistoryList.SelectedItem = list.FirstOrDefault(r => r.Entry == selected);

        CountText.Text = search.Length == 0 ? Labels.Count(all.Count, "Eintrag", "Einträge") : $"{list.Count.ToString("N0", Labels.German)} von {Labels.Count(all.Count, "Eintrag", "Einträgen")}";
        EmptyHint.IsVisible = list.Count == 0;
        EmptyTitle.Text = all.Count == 0 ? "Noch nichts erkannt" : "Nichts gefunden";
        EmptyText.Text = all.Count == 0 ? "Alles, was Lupe per Kamera, Bildschirm oder Datei liest, steht hier." : $"Kein Eintrag enthält „{search}“.";
        ExportButton.IsEnabled = ClearButton.IsEnabled = all.Count > 0;
        OffNotice.IsVisible = !_state.Settings.KeepHistory;
        // Kaputte, gesperrte oder nicht speicherbare Datei: Hinweis bleibt stehen, bis es sich erledigt hat
        var problem = _state.History.Problem;
        ProblemNotice.IsVisible = problem is not null;
        ProblemText.Text = problem ?? "";
        UpdateDetail();
    }

    // ================= Sortieren =================

    private IEnumerable<HistoryRow> Sorted(IEnumerable<HistoryRow> rows)
    {
        if (_sortBy is null) return rows;
        Func<HistoryRow, object> key = _sortBy switch
        {
            "Time" => r => r.Time,
            "SourceLabel" => r => r.SourceLabel,
            "FormatLabel" => r => r.FormatLabel,
            _ => r => r.OneLine,
        };
        var comparer = Comparer<object>.Create((a, b) => a is string sa && b is string sb
            ? StringComparer.CurrentCultureIgnoreCase.Compare(sa, sb)
            : System.Collections.Comparer.Default.Compare(a, b));
        return _descending ? rows.OrderByDescending(key, comparer) : rows.OrderBy(key, comparer);
    }

    private void Sort_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string property }) return;
        _descending = _sortBy == property && !_descending;
        _sortBy = property;
        foreach (var (name, (button, title)) in _headers)
        {
            if (name != _sortBy)
            {
                button.Content = title;
                AutomationProperties.SetName(button, title);
                continue;
            }
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new TextBlock { Text = title });
            var arrow = new TextBlock { Text = _descending ? "\uE70D" : "\uE70E", FontSize = 9, Margin = new Avalonia.Thickness(6, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            arrow.SetResource(TextBlock.FontFamilyProperty, "IconFont");
            header.Children.Add(arrow);
            button.Content = header;
            AutomationProperties.SetName(button, $"{title}, {(_descending ? "absteigend" : "aufsteigend")} sortiert");
        }
        Refresh();
    }

    // ================= Auswahl =================

    private void Search_TextChanged(object? sender, TextChangedEventArgs e) => _search.Run(Refresh);

    private void List_SelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateDetail();

    private void UpdateDetail()
    {
        var row = HistoryList.SelectedItem as HistoryRow;
        DetailBar.IsEnabled = row is not null;
        if (row is null)
        {
            DetailTitle.Text = "Kein Eintrag gewählt";
            DetailText.Text = "Eintrag anklicken: kopieren, Link öffnen oder löschen. Doppelklick kopiert.";
            OpenLinkButton.IsEnabled = false;
            return;
        }
        var done = Labels.Actions(row.Entry.ActionsDone);
        DetailTitle.Text = row.OneLine;
        ToolTip.SetTip(DetailTitle, row.Text);
        DetailText.Text = $"{row.FormatLabel} · {row.SourceLabel} · {row.TimeText}" + (done.Length > 0 ? $" · {done}" : "") + (row.Link is null ? "" : $" · {row.Link.AbsoluteUri}");
        OpenLinkButton.IsEnabled = row.HasLink;
        ToolTip.SetTip(OpenLinkButton, row.HasLink ? row.Link!.AbsoluteUri : "Keine Link-Regel passt zu diesem Inhalt");
        MenuOpenLink.IsEnabled = row.HasLink;
    }

    // ================= Aktionen =================

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryRow row) await CodeActions.CopyAsync(_state, row.Text);
    }

    private void OpenLink_Click(object? sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryRow { Link: { } link }) CodeActions.Open(_state, link);
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryRow row) return;
        int index = HistoryList.SelectedIndex;
        try { _state.History.Remove(row.Entry); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _state.Report(NoticeKind.Error, $"Der Eintrag ließ sich nicht löschen: {ex.Message}");
            return;
        }
        Refresh();
        if (HistoryList.ItemCount > 0)
        {
            HistoryList.SelectedIndex = Math.Min(index, HistoryList.ItemCount - 1);
            HistoryList.ContainerFromIndex(HistoryList.SelectedIndex)?.Focus();
        }
    }

    private void List_DoubleTapped(object? sender, TappedEventArgs e) => Copy_Click(sender, e);

    private void List_KeyDown(object? sender, KeyEventArgs e)
    {
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.Key is Key.Delete or Key.Back && e.KeyModifiers == KeyModifiers.None) { Delete_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.C && e.KeyModifiers == command) { Copy_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Enter) { OpenLink_Click(sender, e); e.Handled = true; }
    }

    private async void Clear_Click(object? sender, RoutedEventArgs e)
    {
        int count = _state.History.Entries.Count;
        if (count == 0) return;
        bool yes = await Dialogs.ConfirmAsync(this, "Verlauf löschen",
            $"Den ganzen Verlauf löschen ({Labels.Count(count, "Eintrag", "Einträge")})? Das lässt sich nicht rückgängig machen.");
        if (!yes) return;
        try { _state.History.Clear(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _state.Report(NoticeKind.Error, $"Der Verlauf ließ sich nicht löschen: {ex.Message}");
            return;
        }
        Refresh();
        _state.Report(NoticeKind.Success, "Verlauf gelöscht.");
    }

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanSave: true } storage) return;
        IStorageFile? file;
        try
        {
            file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Verlauf als CSV exportieren",
                SuggestedFileName = $"Lupe-Verlauf-{DateTime.Now:yyyy-MM-dd}.csv",
                DefaultExtension = "csv",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType("CSV für Excel (*.csv)") { Patterns = ["*.csv"], MimeTypes = ["text/csv"], AppleUniformTypeIdentifiers = ["public.comma-separated-values-text"] }],
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _state.Report(NoticeKind.Error, $"Der Dialog zum Speichern ließ sich nicht zeigen: {ex.Message}");
            return;
        }
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            _state.History.ExportCsv(path);
            _state.Report(NoticeKind.Success, $"Exportiert: {path}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _state.Report(NoticeKind.Error, $"Der Export ist fehlgeschlagen: {ex.Message}");
        }
    }

    private void GoSettings_Click(object? sender, RoutedEventArgs e) => _state.RequestPage("einstellungen");

    public Task PrepareScreenshotAsync()
    {
        SearchBox.Text = "";
        Refresh();
        HistoryList.SelectedIndex = 1;
        return Ui.IdleAsync();
    }
}
