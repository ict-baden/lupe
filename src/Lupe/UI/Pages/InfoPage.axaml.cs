using Avalonia.Controls;
using Avalonia.Interactivity;
using Lupe.Core.Input;
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
        VersionText.Text = $"Version {AppInfo.Version} · .NET {Environment.Version.ToString(3)}";
        if (OperatingSystem.IsWindows())
            CommandExample.Text = "Lupe.exe lesen C:\\Scans\\etikett.png --format QR_CODE";
        else
        {
            CommandIntro.Text = "Dasselbe Programm liest Bilder auch ohne Fenster – je Code eine Zeile „FORMAT⇥Inhalt“, für Skripte und geplante Aufgaben:";
            CommandExample.Text = "lupe lesen ~/Scans/etikett.png --format QR_CODE";
            CommandHelp.Text = "Alle Befehle und Optionen: lupe --hilfe";
            DataCard.Description = $"Einstellungen und Verlauf liegen unter {DataFolderText()} – nur für Sie. Lupe schickt nichts ins Internet; Links öffnet nur der Browser, wenn Sie es einstellen.";
        }
        if (state.IsDemo && !state.Strict) DataCard.Description = "Demo: Einstellungen und Verlauf liegen nur im Speicher und verschwinden beim Beenden.";
        DataButton.IsEnabled = !state.IsDemo || state.Strict;
    }

    public void OnShown()
    {
        string hotkey;
        try { hotkey = HotkeyText.Format(_state.Settings.ScreenHotkey); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { hotkey = "Das Tastenkürzel"; }
        var ctrl = OperatingSystem.IsMacOS() ? "⌘" : "Strg+";
        KeysText.Text = $"{hotkey} scannt einen Bildschirmausschnitt – aus jedem Programm · {ctrl}1 bis {ctrl}6 wechselt die Seiten · "
            + $"{ctrl}V liest ein Bild aus der Zwischenablage · im Verlauf: {ctrl}C kopiert, Eingabe öffnet den Link, Entf löscht";
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

    private static string DataFolderText()
    {
        var folder = DataFolder();
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
            _state.Report(NoticeKind.Info, "Der Ordner existiert noch nicht – Lupe legt ihn beim ersten Speichern an.");
            return;
        }
        if (Shell.ShowInExplorer(folder) is { } error) _state.Report(NoticeKind.Warning, error);
    }
}
