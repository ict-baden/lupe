using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lupe.Core.Decoding;
using Lupe.Core.Input;
using Lupe.Core.Model;
using Lupe.Core.Settings;
using Lupe.Services;

namespace Lupe.UI.Pages;

/// <summary>
/// Einstellungen: Aktionen (getrennt für Kamera/Datei und Bildschirm), Suffix, Tastenkürzel, Link-Regeln, Formate,
/// Ton, Zielhilfe, Wiederholungssperre, Verlauf, Autostart und Infobereich. Jede Änderung gilt und speichert sofort.
/// </summary>
public partial class SettingsPage : UserControl, IPage
{
    public sealed record RepeatChoice(int Milliseconds, string Label);

    private const string InfoGlyph = "\uE946";
    private const string CheckGlyph = "\uE73E";
    private const string WarningGlyph = "\uE7BA";

    private readonly AppState _state;
    private readonly ObservableCollection<RuleRow> _rules = [];
    private readonly Debouncer _ruleSave = new(TimeSpan.FromMilliseconds(400));
    private readonly Dictionary<string, CheckBox> _formatBoxes = new(StringComparer.OrdinalIgnoreCase);
    private bool _loading;
    private bool _recording;

    internal SettingsPage(AppState state)
    {
        InitializeComponent();
        _state = state;
        RulesList.ItemsSource = _rules;
        BuildFormats();
        state.HotkeyChanged += UpdateHotkeyState;
        state.SettingsChanged += (_, _) => { if (!_loading && IsVisible) Load(); };
        StoreText.Text = state.IsDemo && !state.Strict
            ? "Demo: Änderungen gelten nur bis zum Beenden und werden nicht gespeichert."
            : $"Gespeichert in {SafeSettingsPath()}";
        Load();
    }

    public void OnShown() => Load();

    public void OnHidden()
    {
        // Noch nicht gespeicherte Regeländerung nicht verlieren
        _ruleSave.Flush();
    }

    // ================= Laden =================

    private void Load()
    {
        _loading = true;
        try
        {
            var s = _state.Settings;
            ActCopy.IsChecked = s.Actions.HasFlag(ResultActions.Kopieren);
            ActLink.IsChecked = s.Actions.HasFlag(ResultActions.LinkOeffnen);
            ScrCopy.IsChecked = s.ScreenActions.HasFlag(ResultActions.Kopieren);
            ScrType.IsChecked = s.ScreenActions.HasFlag(ResultActions.Eintippen);
            ScrLink.IsChecked = s.ScreenActions.HasFlag(ResultActions.LinkOeffnen);
            SuffixBox.SelectedItem = SuffixBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.TypeSuffix) ?? SuffixBox.Items[0];
            SuffixBox.IsEnabled = s.ScreenActions.HasFlag(ResultActions.Eintippen);

            HotkeyBox.Text = FormatHotkey(s.ScreenHotkey);
            UpdateHotkeyState();

            if (!_ruleSave.IsPending)
            {
                _rules.Clear();
                foreach (var rule in s.LinkRules) _rules.Add(Watch(new RuleRow { Pattern = rule.Pattern, Prefix = rule.Prefix, Enabled = rule.Enabled }));
                ValidateRules();
            }

            AllFormatsToggle.IsChecked = s.Formats.Count == 0;
            FormatsCard.Visibility = s.Formats.Count == 0 || _formatBoxes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            foreach (var (name, box) in _formatBoxes)
                box.IsChecked = s.Formats.Count == 0 || s.Formats.Contains(name, StringComparer.OrdinalIgnoreCase);

            SoundToggle.IsChecked = s.Sound;
            AimToggle.IsChecked = s.AimHelp;
            FillRepeat(s.RepeatSuppressMs);
            KeepToggle.IsChecked = s.KeepHistory;
            LimitBox.Text = s.HistoryLimit.ToString(Labels.German);

            AutostartToggle.IsChecked = !_state.IsDemo && Autostart.IsEnabled;
            AutostartToggle.IsEnabled = !_state.IsDemo;
            AutostartCard.ToolTip = _state.IsDemo ? "In der Demo nicht verfügbar" : null;
            TrayToggle.IsChecked = _state.Preferences.KeepRunningInTray;
        }
        finally
        {
            _loading = false;
        }
    }

    private static string SafeSettingsPath()
    {
        try { return SettingsStore.DefaultPath; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return @"%LocalAppData%\Lupe\einstellungen.json"; }
    }

    // ================= Aktionen =================

    private void Actions_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        static ResultActions Of(CheckBox? copy, CheckBox? type, CheckBox? link) =>
            (copy?.IsChecked == true ? ResultActions.Kopieren : 0)
            | (type?.IsChecked == true ? ResultActions.Eintippen : 0)
            | (link?.IsChecked == true ? ResultActions.LinkOeffnen : 0);
        var actions = Of(ActCopy, null, ActLink);
        var screen = Of(ScrCopy, ScrType, ScrLink);
        SuffixBox.IsEnabled = screen.HasFlag(ResultActions.Eintippen);
        _state.UpdateSettings(s => s with { Actions = actions, ScreenActions = screen });
    }

    private void Suffix_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SuffixBox.SelectedItem is not ComboBoxItem { Tag: string suffix }) return;
        _state.UpdateSettings(s => s with { TypeSuffix = suffix });
    }

    // ================= Tastenkürzel =================

    private void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _recording = true;
        HotkeyBox.Text = "Kombination drücken …";
    }

    private void HotkeyBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _recording = false;
        HotkeyBox.Text = FormatHotkey(_state.Settings.ScreenHotkey);
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // Tab und Esc behalten ihre Bedeutung (weiter, abbrechen)
        if (key == Key.Tab) return;
        e.Handled = true;
        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var mods = Keyboard.Modifiers;
        bool win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
        bool ctrl = mods.HasFlag(ModifierKeys.Control), alt = mods.HasFlag(ModifierKeys.Alt), shift = mods.HasFlag(ModifierKeys.Shift);
        bool standalone = key is >= Key.F1 and <= Key.F24 or Key.PrintScreen or Key.Pause;
        if (!ctrl && !alt && !win && !standalone)
        {
            HotkeyBox.Text = "Bitte mit Strg, Alt oder Win";
            return;
        }
        var hotkey = new Hotkey(ctrl, alt, shift, win, KeyInterop.VirtualKeyFromKey(key));
        _state.UpdateSettings(s => s with { ScreenHotkey = hotkey });
        HotkeyBox.Text = FormatHotkey(hotkey);
        _recording = false;
    }

    private void HotkeyDefault_Click(object sender, RoutedEventArgs e)
    {
        _state.UpdateSettings(s => s with { ScreenHotkey = Hotkey.Default });
        HotkeyBox.Text = FormatHotkey(Hotkey.Default);
    }

    private void UpdateHotkeyState()
    {
        bool problem = _state.HotkeyError is not null;
        HotkeyStateIcon.Text = problem ? WarningGlyph : _state.HotkeyActive ? CheckGlyph : InfoGlyph;
        HotkeyStateIcon.SetResourceReference(TextBlock.ForegroundProperty,
            problem ? "SystemFillColorCautionBrush" : _state.HotkeyActive ? "SystemFillColorSuccessBrush" : "TextFillColorSecondaryBrush");
        HotkeyStateText.Text = _state.HotkeyError
            ?? (_state.HotkeyActive ? $"{FormatHotkey(_state.Settings.ScreenHotkey)} ist bereit – auch wenn das Fenster geschlossen ist." : "Das Tastenkürzel ist noch nicht angemeldet.");
    }

    private static string FormatHotkey(Hotkey hotkey)
    {
        try { return HotkeyText.Format(hotkey); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return "?"; }
    }

    // ================= Link-Regeln =================

    private RuleRow Watch(RuleRow row)
    {
        row.PropertyChanged += (_, e) =>
        {
            if (_loading || e.PropertyName == nameof(RuleRow.Error)) return;
            _ruleSave.Run(SaveRules);
        };
        return row;
    }

    private void SaveRules()
    {
        ValidateRules();
        // Leere Zeilen (gerade hinzugefügt) nicht speichern; ungültige schon – LinkRules überspringt sie, und die
        // Eingabe geht beim nächsten Start nicht verloren
        var rules = _rules.Where(r => r.Pattern.Trim().Length > 0).Select(r => r.ToRule()).ToList();
        _loading = true;
        try { _state.UpdateSettings(s => s with { LinkRules = rules }); }
        finally { _loading = false; }
        UpdateRuleTest();
    }

    private void ValidateRules()
    {
        foreach (var row in _rules)
        {
            if (row.Pattern.Trim().Length == 0)
            {
                row.Error = row.Prefix.Trim().Length == 0 ? null : "Bitte einen regulären Ausdruck eingeben.";
                continue;
            }
            try { row.Error = LinkRules.Validate(row.ToRule()); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { row.Error = ex.Message; }
        }
    }

    private void RuleAdd_Click(object sender, RoutedEventArgs e)
    {
        _rules.Add(Watch(new RuleRow()));
        // Fokus ins neue Ausdrucksfeld
        Dispatcher.BeginInvoke(() =>
        {
            if (RulesList.ItemContainerGenerator.ContainerFromIndex(_rules.Count - 1) is DependencyObject container
                && Ui.FindChild<TextBox>(container) is { } box)
                box.Focus();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void RuleDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RuleRow row) return;
        _rules.Remove(row);
        SaveRules();
    }

    private void RuleUp_Click(object sender, RoutedEventArgs e) => MoveRule(sender, -1);
    private void RuleDown_Click(object sender, RoutedEventArgs e) => MoveRule(sender, +1);

    private void MoveRule(object sender, int delta)
    {
        if ((sender as FrameworkElement)?.DataContext is not RuleRow row) return;
        int index = _rules.IndexOf(row), target = index + delta;
        if (index < 0 || target < 0 || target >= _rules.Count) return;
        _rules.Move(index, target);
        SaveRules();
    }

    private void RuleTest_TextChanged(object sender, TextChangedEventArgs e) => UpdateRuleTest();

    private void UpdateRuleTest()
    {
        var text = RuleTestBox.Text.Trim();
        if (text.Length == 0)
        {
            RuleTestResult.Text = "";
            return;
        }
        var link = _state.LinkFor(text);
        RuleTestResult.Text = link is null ? "Keine Regel passt – kein Link." : $"→ {link.AbsoluteUri}";
        RuleTestResult.ToolTip = link?.AbsoluteUri;
    }

    // ================= Formate =================

    private void BuildFormats()
    {
        IReadOnlyList<string> all;
        try { all = BarcodeDecoder.AllFormats; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { all = []; }
        foreach (var name in all)
        {
            var box = new CheckBox { Content = Labels.Format(name), ToolTip = name, Width = 200, Margin = new Thickness(0, 2, 8, 2) };
            box.Click += Format_Click;
            _formatBoxes[name] = box;
            FormatsPanel.Children.Add(box);
        }
    }

    private void AllFormats_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (AllFormatsToggle.IsChecked == true)
        {
            _state.UpdateSettings(s => s with { Formats = [] });
            FormatsCard.Visibility = Visibility.Collapsed;
            return;
        }
        // Ausgeschaltet: mit den üblichen Formaten beginnen, der Rest lässt sich dazunehmen
        string[] common = ["QR_CODE", "DATA_MATRIX", "EAN_13", "EAN_8", "CODE_128", "CODE_39", "ITF", "UPC_A"];
        var start = _formatBoxes.Keys.Where(k => common.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
        if (start.Count == 0) start = [.. _formatBoxes.Keys];
        _state.UpdateSettings(s => s with { Formats = start });
        Load();
    }

    private void Format_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var chosen = _formatBoxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
        if (chosen.Count == 0)
        {
            // Leer hieße „alle“ – das wäre das Gegenteil dessen, was gemeint ist
            ((CheckBox)sender).IsChecked = true;
            _state.Report(NoticeKind.Info, "Mindestens ein Format muss bleiben. Für alle Formate oben „Alle Formate erkennen“ einschalten.");
            return;
        }
        if (chosen.Count == _formatBoxes.Count) chosen = [];
        _state.UpdateSettings(s => s with { Formats = chosen });
        if (chosen.Count == 0) Load();
    }

    // ================= Kamera und Verlauf =================

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool sound = SoundToggle.IsChecked == true, aim = AimToggle.IsChecked == true, keep = KeepToggle.IsChecked == true;
        _state.UpdateSettings(s => s with { Sound = sound, AimHelp = aim, KeepHistory = keep });
    }

    private void SoundTest_Click(object sender, RoutedEventArgs e) => Beep.Play();

    private void FillRepeat(int current)
    {
        var choices = new List<RepeatChoice>
        {
            new(1000, "1 Sekunde"), new(2000, "2 Sekunden"), new(3000, "3 Sekunden"), new(5000, "5 Sekunden"),
            new(10_000, "10 Sekunden"), new(30_000, "30 Sekunden"), new(60_000, "1 Minute"),
        };
        if (choices.All(c => c.Milliseconds != current))
            choices.Add(new(current, current % 1000 == 0 ? $"{current / 1000} Sekunden" : $"{current.ToString("N0", Labels.German)} ms"));
        RepeatBox.ItemsSource = choices.OrderBy(c => c.Milliseconds).ToList();
        RepeatBox.SelectedItem = ((List<RepeatChoice>)RepeatBox.ItemsSource).First(c => c.Milliseconds == current);
    }

    private void Repeat_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || RepeatBox.SelectedItem is not RepeatChoice choice) return;
        _state.UpdateSettings(s => s with { RepeatSuppressMs = choice.Milliseconds });
    }

    private void Limit_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplyLimit();
    }

    private void Limit_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => ApplyLimit();

    private void ApplyLimit()
    {
        var text = LimitBox.Text.Replace(".", "", StringComparison.Ordinal).Replace("’", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        if (!int.TryParse(text, System.Globalization.NumberStyles.Integer, Labels.German, out var limit) || limit < 10 || limit > 100_000)
        {
            _state.Report(NoticeKind.Warning, "Bitte eine Zahl von 10 bis 100 000 eingeben.");
            LimitBox.Text = _state.Settings.HistoryLimit.ToString(Labels.German);
            return;
        }
        LimitBox.Text = limit.ToString(Labels.German);
        if (limit != _state.Settings.HistoryLimit) _state.UpdateSettings(s => s with { HistoryLimit = limit });
    }

    // ================= Start =================

    private void Autostart_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || _state.IsDemo) return;
        bool on = AutostartToggle.IsChecked == true;
        if (Autostart.Set(on) is { } error)
        {
            _state.Report(NoticeKind.Error, error);
            AutostartToggle.IsChecked = Autostart.IsEnabled;
        }
    }

    private void Tray_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool on = TrayToggle.IsChecked == true;
        _state.UpdatePreferences(p => p with { KeepRunningInTray = on });
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(SafeSettingsPath()) ?? "";
        if (_state.IsDemo || folder.Length == 0 || !Directory.Exists(folder))
        {
            _state.Report(NoticeKind.Info, "Der Ordner existiert noch nicht – Lupe legt ihn beim ersten Speichern an.");
            return;
        }
        if (Shell.ShowInExplorer(folder) is { } error) _state.Report(NoticeKind.Warning, error);
    }

    public Task PrepareScreenshotAsync()
    {
        Load();
        RuleTestBox.Text = "4012345000009";
        Scroller.ScrollToTop();
        return Ui.IdleAsync();
    }
}
