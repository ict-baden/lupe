using System.Windows;
using System.Windows.Controls;
using Lupe.Core.Input;
using Lupe.Services;

namespace Lupe.UI.Pages;

/// <summary>Info: Version, Herausgeber, Lizenz (noch offen), Repo, Herkunft vom Barcode-Reader, Erkennung mit Lizenztexten, Daten, Befehlszeile, Tastatur.</summary>
public partial class InfoPage : UserControl, IPage
{
    private readonly AppState _state;

    internal InfoPage(AppState state)
    {
        InitializeComponent();
        _state = state;
        VersionText.Text = $"Version {AppInfo.Version} · .NET {Environment.Version.ToString(3)}";
        CommandExample.Text = "Lupe.exe lesen C:\\Scans\\etikett.png --format QR_CODE";
        if (state.IsDemo && !state.Strict) DataCard.Description = "Demo: Einstellungen und Verlauf liegen nur im Speicher und verschwinden beim Beenden.";
        DataButton.IsEnabled = !state.IsDemo || state.Strict;
    }

    public void OnShown()
    {
        string hotkey;
        try { hotkey = HotkeyText.Format(_state.Settings.ScreenHotkey); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { hotkey = "Das Tastenkürzel"; }
        KeysText.Text = $"{hotkey} scannt einen Bildschirmausschnitt – aus jedem Programm · Strg+1 bis Strg+6 wechselt die Seiten · "
            + "Strg+V liest ein Bild aus der Zwischenablage · im Verlauf: Strg+C kopiert, Eingabe öffnet den Link, Entf löscht";
    }

    public void OnHidden() { }

    public Task PrepareScreenshotAsync()
    {
        OnShown();
        return Ui.IdleAsync();
    }

    private void OpenRepository_Click(object sender, RoutedEventArgs e) => Open(AppInfo.RepositoryUrl);

    private void OpenNotices_Click(object sender, RoutedEventArgs e)
    {
        if (ThirdPartyNotices.Open() is { } error) _state.Report(NoticeKind.Warning, error);
    }
    private void OpenOrigin_Click(object sender, RoutedEventArgs e) => Open(AppInfo.OriginUrl);

    private void Open(string url)
    {
        if (Shell.OpenLink(new Uri(url)) is { } error) _state.Report(NoticeKind.Warning, error);
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lupe");
        if (!Directory.Exists(folder))
        {
            _state.Report(NoticeKind.Info, "Der Ordner existiert noch nicht – Lupe legt ihn beim ersten Speichern an.");
            return;
        }
        if (Shell.ShowInExplorer(folder) is { } error) _state.Report(NoticeKind.Warning, error);
    }
}
