using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lupe.Core.Model;
using Lupe.Services;
using Microsoft.Win32;

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

/// <summary>Verlauf: Liste mit Suche, Kopieren, Link öffnen, Löschen, „Alles löschen“ und CSV-Export.</summary>
public partial class HistoryPage : UserControl, IPage
{
    private readonly AppState _state;
    private readonly Debouncer _search = new(TimeSpan.FromMilliseconds(200));
    private bool _dirty = true;
    private bool _visible;

    internal HistoryPage(AppState state)
    {
        InitializeComponent();
        _state = state;
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
        var search = SearchBox.Text.Trim();
        var rows = all.Select(e => new HistoryRow(e, _state.LinkFor(e.Text)))
                      .Where(r => search.Length == 0 || r.Matches(search))
                      .ToList();
        HistoryList.ItemsSource = rows;
        if (selected is not null) HistoryList.SelectedItem = rows.FirstOrDefault(r => r.Entry == selected);

        CountText.Text = search.Length == 0 ? Labels.Count(all.Count, "Eintrag", "Einträge") : $"{rows.Count.ToString("N0", Labels.German)} von {Labels.Count(all.Count, "Eintrag", "Einträgen")}";
        EmptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = all.Count == 0 ? "Noch nichts erkannt" : "Nichts gefunden";
        EmptyText.Text = all.Count == 0 ? "Alles, was Lupe per Kamera, Bildschirm oder Datei liest, steht hier." : $"Kein Eintrag enthält „{search}“.";
        ExportButton.IsEnabled = ClearButton.IsEnabled = all.Count > 0;
        OffNotice.Visibility = _state.Settings.KeepHistory ? Visibility.Collapsed : Visibility.Visible;
        // Kaputte, gesperrte oder nicht speicherbare Datei: Hinweis bleibt stehen, bis es sich erledigt hat
        var problem = _state.History.Problem;
        ProblemNotice.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
        ProblemText.Text = problem ?? "";
        UpdateDetail();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => _search.Run(Refresh);

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDetail();

    private void UpdateDetail()
    {
        var row = HistoryList.SelectedItem as HistoryRow;
        DetailBar.IsEnabled = row is not null;
        if (row is null)
        {
            DetailTitle.Text = "Kein Eintrag gewählt";
            DetailText.Text = "Eintrag anklicken: kopieren, Link öffnen oder löschen. Doppelklick kopiert.";
            OpenLinkButton.Visibility = Visibility.Visible;
            OpenLinkButton.IsEnabled = false;
            return;
        }
        var done = Labels.Actions(row.Entry.ActionsDone);
        DetailTitle.Text = row.OneLine;
        DetailTitle.ToolTip = row.Text;
        DetailText.Text = $"{row.FormatLabel} · {row.SourceLabel} · {row.TimeText}" + (done.Length > 0 ? $" · {done}" : "") + (row.Link is null ? "" : $" · {row.Link.AbsoluteUri}");
        OpenLinkButton.IsEnabled = row.HasLink;
        OpenLinkButton.ToolTip = row.HasLink ? row.Link!.AbsoluteUri : "Keine Link-Regel passt zu diesem Inhalt";
        MenuOpenLink.IsEnabled = row.HasLink;
    }

    // ================= Aktionen =================

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryRow row) CodeActions.Copy(_state, row.Text);
    }

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryRow { Link: { } link }) CodeActions.Open(_state, link);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
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
        if (HistoryList.Items.Count > 0)
        {
            HistoryList.SelectedIndex = Math.Min(index, HistoryList.Items.Count - 1);
            (HistoryList.ItemContainerGenerator.ContainerFromIndex(HistoryList.SelectedIndex) as ListViewItem)?.Focus();
        }
    }

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Copy_Click(sender, e);

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { Delete_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { Copy_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Enter) { OpenLink_Click(sender, e); e.Handled = true; }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        int count = _state.History.Entries.Count;
        if (count == 0) return;
        var answer = MessageBox.Show(Window.GetWindow(this),
            $"Den ganzen Verlauf löschen ({Labels.Count(count, "Eintrag", "Einträge")})? Das lässt sich nicht rückgängig machen.",
            "Verlauf löschen", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        try { _state.History.Clear(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _state.Report(NoticeKind.Error, $"Der Verlauf ließ sich nicht löschen: {ex.Message}");
            return;
        }
        Refresh();
        _state.Report(NoticeKind.Success, "Verlauf gelöscht.");
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Verlauf als CSV exportieren",
            Filter = "CSV für Excel (*.csv)|*.csv",
            FileName = $"Lupe-Verlauf-{DateTime.Now:yyyy-MM-dd}.csv",
            AddExtension = true,
            DefaultExt = ".csv",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            _state.History.ExportCsv(dialog.FileName);
            _state.Report(NoticeKind.Success, $"Exportiert: {dialog.FileName}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _state.Report(NoticeKind.Error, $"Der Export ist fehlgeschlagen: {ex.Message}");
        }
    }

    private void GoSettings_Click(object sender, RoutedEventArgs e) => _state.RequestPage("einstellungen");

    public Task PrepareScreenshotAsync()
    {
        SearchBox.Text = "";
        Refresh();
        HistoryList.SelectedIndex = 1;
        return Ui.IdleAsync();
    }
}
