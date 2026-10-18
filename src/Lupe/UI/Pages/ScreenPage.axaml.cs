using System.Collections.Specialized;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Lupe.Core.Input;
using Lupe.Core.Model;
using Lupe.Core.Resources;

namespace Lupe.UI.Pages;

/// <summary>Bildschirm: großer Knopf „Ausschnitt scannen“, Tastenkürzel, Aktionen beim Treffer, letzte Ergebnisse.</summary>
public partial class ScreenPage : UserControl, IPage
{
    private readonly AppState _state;

    internal ScreenPage(AppState state)
    {
        InitializeComponent();
        _state = state;
        ResultList.ItemsSource = state.ScreenResults;
        state.ScreenResults.CollectionChanged += OnResultsChanged;
        state.HotkeyChanged += Update;
        state.SettingsChanged += (_, _) => Update();
        Update();
        OnResultsChanged(null, null);
    }

    public void OnShown() => Update();
    public void OnHidden() { }

    private void Update()
    {
        var settings = _state.Settings;
        string hotkey;
        try { hotkey = HotkeyText.Format(settings.ScreenHotkey); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { hotkey = ""; }
        HotkeyKeys.ItemsSource = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        bool problem = _state.HotkeyError is not null && !_state.HotkeyOffInDemo;
        HotkeyWarning.IsVisible = problem;
        HotkeyWarningText.Text = problem ? _state.HotkeyError : "";
        HotkeyNote.Text = _state.HotkeyOffInDemo ? Strings.Page_Screen_HotkeyOffInDemo : problem ? Strings.Page_Screen_HotkeyNotAvailableNow : Strings.Page_Screen_HotkeyFromAnyProgram;

        var actions = settings.ScreenActions;
        var parts = new List<string>();
        if (actions.HasFlag(ResultActions.Kopieren)) parts.Add(Strings.Page_Screen_ActionCopy);
        if (actions.HasFlag(ResultActions.Eintippen))
            parts.Add(settings.TypeSuffix is "Enter" or "Tab" ? string.Format(Strings.Page_Screen_ActionTypeThen, settings.TypeSuffix) : Strings.Page_Screen_ActionType);
        if (actions.HasFlag(ResultActions.LinkOeffnen)) parts.Add(Strings.Page_Screen_ActionOpenLink);
        var list = string.Join(Strings.Page_Screen_ActionSeparator, parts);
        if (list.Length > 0) list = char.ToUpper(list[0], CultureInfo.CurrentCulture) + list[1..];
        ActionsCard.Description = parts.Count == 0
            ? Strings.Page_Screen_ActionsNone
            : string.Format(actions.HasFlag(ResultActions.Eintippen) ? Strings.Page_Screen_ActionsSentenceWithType : Strings.Page_Screen_ActionsSentence, list);
    }

    private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs? e) =>
        ResultsEmpty.IsVisible = _state.ScreenResults.Count == 0;

    private void Scan_Click(object? sender, RoutedEventArgs e) => _state.RequestScreenScan();
    private void GoSettings_Click(object? sender, RoutedEventArgs e) => _state.RequestPage("einstellungen");

    public Task PrepareScreenshotAsync()
    {
        Update();
        return Ui.IdleAsync();
    }
}
