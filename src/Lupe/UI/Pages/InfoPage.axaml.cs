// SPDX-License-Identifier: MIT
using Avalonia.Controls;
using Avalonia.Interactivity;
using Lupe.Core.History;
using Lupe.Core.Input;
using Lupe.Core.Resources;
using Lupe.Core.Settings;
using Lupe.Services;

namespace Lupe.UI.Pages;

/// <summary>
/// Info (Richtlinie 07): Version und Build, Herausgeber, Lizenz, Webseite, Impressum, Repo, Erkennung und Oberfläche mit
/// Lizenztexten, Datenschutz, Hilfe und Support, Daten, Befehlszeile, Tastatur.
/// </summary>
public partial class InfoPage : UserControl, IPage
{
    private readonly AppState _state;

    internal InfoPage(AppState state)
    {
        InitializeComponent();
        _state = state;
        VersionText.Text = AppInfo.Build.Length > 0
            ? string.Format(Strings.Page_Info_VersionBuild, AppInfo.Version, AppInfo.Build, Environment.Version.ToString(3))
            : string.Format(Strings.Page_Info_Version, AppInfo.Version, Environment.Version.ToString(3));
        // Befehl und Beispiel für dieses System (unter macOS gibt es keinen Befehl „lupe“, nur das Programm im Bündel)
        var command = Lupe.Cli.CommandLine.CommandName;
        if (command.Contains(' ', StringComparison.Ordinal)) command = $"\"{command}\"";
        var folder = OperatingSystem.IsWindows() ? @"C:\Scans\" : "~/Scans/";
        CommandExample.Text = $"{command} {Strings.App_Cli_ReadKeyword} {folder}{Strings.App_Cli_ExampleFile} --format QR_CODE";
        CommandHelp.Text = string.Format(Strings.Page_Info_CommandHelp, $"{command} {Strings.App_Cli_HelpKeyword}");
        if (OperatingSystem.IsWindows())
        {
            DataCard.Description = string.Format(Strings.Page_Info_DataText, "%LocalAppData%\\Lupe");
        }
        else
        {
            CommandIntro.Text = Strings.Page_Info_CommandIntro;
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

    /// <summary>Lupes Lizenz aus dem Paket (LICENSE.txt neben dem Programm, sonst eingebettet) – ohne Browser und Netz.</summary>
    private void OpenLicense_Click(object? sender, RoutedEventArgs e)
    {
        if (ThirdPartyNotices.Open(ThirdPartyNotices.LicenseFileName) is { } error) _state.Report(NoticeKind.Warning, error);
    }

    private void OpenWebsite_Click(object? sender, RoutedEventArgs e) => Open(AppInfo.WebsiteUrl);

    private void OpenImprint_Click(object? sender, RoutedEventArgs e) => Open(AppInfo.ImprintUrl);

    private void OpenPrivacy_Click(object? sender, RoutedEventArgs e) => Open(AppInfo.PrivacyUrl);

    private void OpenSupport_Click(object? sender, RoutedEventArgs e) => Open(AppInfo.SupportUrl);

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
