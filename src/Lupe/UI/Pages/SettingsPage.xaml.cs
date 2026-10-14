using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lupe.Core.Decoding;
using Lupe.Core.History;
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
    /// <summary>Kleinste Größe des Verlaufs, die die Oberfläche anbietet (die Datei erlaubt ab 1).</summary>
    private const int MinLimit = 10;

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
        state.SettingsProblemChanged += UpdateProblem;
        StoreText.Text = state.IsDemo && !state.Strict
            ? "Demo: Änderungen gelten nur bis zum Beenden und werden nicht gespeichert."
            // Screenshots: ohne den Benutzernamen dieses Rechners
            : state.Strict ? @"Gespeichert in %LocalAppData%\Lupe\einstellungen.json" : $"Gespeichert in {SafeSettingsPath()}";
        Load();
    }

    public void OnShown() => Load();

    public void OnHidden()
    {
        // Noch nicht gespeicherte Regeländerung nicht verlieren
        _ruleSave.Flush();
        // Seite verlassen oder Fenster zu, während ein Tastenkürzel aufgenommen wird: das gespeicherte wieder anmelden
        StopRecording();
    }

    // ================= Laden =================

    private void Load()
    {
        _loading = true;
        try
        {
            var s = _state.Settings;
            UpdateProblem();
            ActCopy.IsChecked = s.Actions.HasFlag(ResultActions.Kopieren);
            ActLink.IsChecked = s.Actions.HasFlag(ResultActions.LinkOeffnen);
            ScrCopy.IsChecked = s.ScreenActions.HasFlag(ResultActions.Kopieren);
            ScrType.IsChecked = s.ScreenActions.HasFlag(ResultActions.Eintippen);
            ScrLink.IsChecked = s.ScreenActions.HasFlag(ResultActions.LinkOeffnen);
            SuffixBox.SelectedItem = SuffixBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.TypeSuffix) ?? SuffixBox.Items[0];
            SuffixBox.IsEnabled = s.ScreenActions.HasFlag(ResultActions.Eintippen);

            if (!_recording) HotkeyBox.Text = FormatHotkey(s.ScreenHotkey);
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
            TrayToggle.IsChecked = s.KeepRunningInTray;
        }
        finally
        {
            _loading = false;
        }
    }

    private void UpdateProblem()
    {
        ProblemNotice.Visibility = _state.SettingsProblem is null ? Visibility.Collapsed : Visibility.Visible;
        ProblemText.Text = _state.SettingsProblem ?? "";
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

    private void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e) => StartRecording();

    private void HotkeyBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => StopRecording();

    /// <summary>Noch einmal ins Feld klicken (es hat den Fokus schon): erneut aufnehmen.</summary>
    private void HotkeyBox_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (HotkeyBox.IsKeyboardFocusWithin && !_recording) StartRecording();
    }

    /// <summary>
    /// Aufnahme beginnt: Das globale Kürzel wird solange abgemeldet – sonst fängt Windows die Kombination ab und startet
    /// einen Bildschirm-Scan, statt sie an das Feld zu geben (z. B. wenn man das bisherige Kürzel noch einmal drückt).
    /// </summary>
    private void StartRecording()
    {
        if (_recording) return;
        _recording = true;
        HotkeyBox.Text = "Kombination drücken …";
        _state.SuspendHotkey(true);
        UpdateHotkeyState();
    }

    /// <summary>Aufnahme vorbei (aufgenommen, Esc, Fokus weg, Seite verlassen): das geltende Kürzel wieder anmelden.</summary>
    private void StopRecording()
    {
        if (!_recording) return;
        _recording = false;
        HotkeyBox.Text = FormatHotkey(_state.Settings.ScreenHotkey);
        _state.SuspendHotkey(false);
        UpdateHotkeyState();
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
        // Fertig – das neue Kürzel gleich anmelden (der Fokus bleibt im Feld; ein Klick nimmt erneut auf)
        StopRecording();
    }

    private void HotkeyDefault_Click(object sender, RoutedEventArgs e)
    {
        _state.UpdateSettings(s => s with { ScreenHotkey = Hotkey.Default });
        HotkeyBox.Text = FormatHotkey(Hotkey.Default);
    }

    private void UpdateHotkeyState()
    {
        bool problem = _state.HotkeyError is not null && !_state.HotkeyOffInDemo && !_recording;
        bool ready = _state.HotkeyActive && !_state.HotkeyOffInDemo && !_recording;
        HotkeyStateIcon.Text = problem ? WarningGlyph : ready ? CheckGlyph : InfoGlyph;
        HotkeyStateIcon.SetResourceReference(TextBlock.ForegroundProperty,
            problem ? "SystemFillColorCautionBrush" : ready ? "SystemFillColorSuccessBrush" : "TextFillColorSecondaryBrush");
        HotkeyStateText.Text = _recording
            ? "Während der Aufnahme ist das Tastenkürzel abgemeldet – die Kombination landet hier im Feld. Esc bricht ab."
            : _state.HotkeyOffInDemo
                ? "In der Demo ist das Tastenkürzel nicht angemeldet – so kommt es einer echten Lupe nicht in die Quere."
                : _state.HotkeyError
                  ?? (ready ? $"{FormatHotkey(_state.Settings.ScreenHotkey)} ist bereit – auch wenn das Fenster geschlossen ist." : "Das Tastenkürzel ist noch nicht angemeldet.");

        // Strg+Alt+…: auf deutschen Tastaturen dieselben Tasten wie AltGr (Strg+Alt+Q = AltGr+Q = „@“)
        string? altGr = null;
        try { altGr = HotkeyText.AltGrWarning(_state.Settings.ScreenHotkey); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
        AltGrHint.Visibility = altGr is null ? Visibility.Collapsed : Visibility.Visible;
        AltGrHintText.Text = altGr ?? "";
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
        if (!keep && _state.Settings.KeepHistory && !ConfirmHistoryOff())
        {
            KeepToggle.IsChecked = true;
            return;
        }
        _state.UpdateSettings(s => s with { Sound = sound, AimHelp = aim, KeepHistory = keep });
    }

    /// <summary>
    /// „Verlauf speichern“ aus: Die gespeicherte verlauf.json wird gelöscht (gescannte Inhalte können vertraulich sein) –
    /// vorher fragen. Gibt es keine Datei (Demo, noch nichts gespeichert), ist nichts zu fragen.
    /// </summary>
    private bool ConfirmHistoryOff()
    {
        if (_state.IsDemo) return true;
        bool fileExists;
        try { fileExists = File.Exists(_state.History.FilePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { fileExists = true; }
        if (!fileExists) return true;

        int count = _state.History.Entries.Count;
        var answer = MessageBox.Show(Window.GetWindow(this),
            "Den Verlauf nicht mehr speichern?\n\n"
            + $"Die Datei {_state.History.FilePath}" + (count > 0 ? $" mit {Labels.Count(count, "Eintrag", "Einträgen")}" : "")
            + " wird dabei gelöscht, damit gescannte Inhalte nicht auf der Platte bleiben. Das lässt sich nicht rückgängig machen.\n\n"
            + "Die Einträge bleiben hier sichtbar, bis Lupe beendet wird. Wer sie behalten möchte, exportiert sie vorher "
            + "(Verlauf → Als CSV exportieren …).",
            "Verlauf nicht mehr speichern", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        return answer == MessageBoxResult.Yes;
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
        if (!int.TryParse(text, System.Globalization.NumberStyles.Integer, Labels.German, out var limit) || limit < MinLimit || limit > HistoryStore.MaxLimit)
        {
            _state.Report(NoticeKind.Warning, $"Bitte eine Zahl von {MinLimit} bis {HistoryStore.MaxLimit.ToString("N0", Labels.German)} eingeben.");
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
        _state.UpdateSettings(s => s with { KeepRunningInTray = on });
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
