// SPDX-License-Identifier: MIT
using Avalonia.Controls;
using Avalonia.Interactivity;
using Lupe.Core.History;
using Lupe.Core.Input;
using Lupe.Core.Resources;
using Lupe.Core.Settings;
using Lupe.Services;

namespace Lupe.UI.Pages;

/// <summary>Info: Version, Herausgeber, Lizenz (noch offen), Repo, Erkennung und Oberfläche mit Lizenztexten, Daten, Befehlszeile, Tastatur.</summary>
public partial class InfoPage : UserControl, IPage
{
    private readonly AppState _state;

    internal InfoPage(AppState state)
    {
        InitializeComponent();
        _state = state;
        VersionText.Text = string.Format(Strings.Page_Info_Version, AppInfo.Version, Environment.Version.ToString(3));
        if (OperatingSystem.IsWindows())
        {
            CommandExample.Text = "Lupe.exe lesen C:\\Scans\\etikett.png --format QR_CODE";
            CommandHelp.Text = string.Format(Strings.Page_Info_CommandHelp, "Lupe.exe --hilfe");
            DataCard.Description = string.Format(Strings.Page_Info_DataText, "%LocalAppData%\\Lupe");
        }
        else
        {
            CommandIntro.Text = Strings.Page_Info_CommandIntro;
            CommandExample.Text = "lupe lesen ~/Scans/etikett.png --format QR_CODE";
            CommandHelp.Text = string.Format(Strings.Page_Info_CommandHelp, "lupe --hilfe");
            DataCard.Description = DataFolderText();
        }
        if (state.IsDemo && !state.Strict) DataCard.Description = Strings.Page_Info_DataDemo;
        DataButton.IsEnabled = !state.IsDemo || state.Strict;
    }

    public void OnShown()
    {
        string hotkey;
        try { hotkey = HotkeyText.Format(_state.Settings.ScreenHotkey); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { hotkey = Strings.Page_Info_TheHotkey; }
        var ctrl = OperatingSystem.IsMacOS() ? "⌘" : Strings.Page_Info_CtrlPrefix;
        KeysText.Text = string.Format(Strings.Page_Info_Keys, hotkey, ctrl);
    }

    public void OnHidden() { }

    public Task PrepareScreenshotAsync()
    {
        OnShown();
        return Ui.IdleAsync();
    }

    private static string DataFolder()
    {
        try { return Path.GetDirectoryName(SettingsStore.DefaultPath) ?? ""; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return ""; }
    }

    /// <summary>Unter Linux liegen Einstellungen (~/.config/lupe) und Verlauf (~/.local/share/lupe) getrennt (XDG).</summary>
    private static string DataFolderText()
    {
        var settings = Shorten(DataFolder());
        string history;
        try { history = Shorten(Path.GetDirectoryName(HistoryStore.DefaultPath) ?? ""); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { history = settings; }
        return history == settings || history.Length == 0
            ? string.Format(Strings.Page_Info_DataText, settings)
            : string.Format(Strings.Page_Info_DataTextSplit, settings, history);
    }

    private static string Shorten(string folder)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 0 && folder.StartsWith(home, StringComparison.Ordinal) ? "~" + folder[home.Length..] : folder;
    }

    private void OpenRepository_Click(object? sender, RoutedEventArgs e) => Open(AppInfo.RepositoryUrl);

    private void OpenNotices_Click(object? sender, RoutedEventArgs e)
    {
        if (ThirdPartyNotices.Open() is { } error) _state.Report(NoticeKind.Warning, error);
    }

    private void Open(string url)
    {
        if (Shell.OpenLink(new Uri(url)) is { } error) _state.Report(NoticeKind.Warning, error);
    }

    private void OpenData_Click(object? sender, RoutedEventArgs e)
    {
        var folder = DataFolder();
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            _state.Report(NoticeKind.Info, Strings.Page_Shared_FolderMissing);
            return;
        }
        if (Shell.ShowInExplorer(folder) is { } error) _state.Report(NoticeKind.Warning, error);
    }
}
