// SPDX-License-Identifier: MIT
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Lupe.Core.Camera;
using Lupe.Core.Decoding;
using Lupe.Core.Model;
using Lupe.Core.Platform;
using Lupe.Core.Resources;
using Lupe.Demo;
using Lupe.Services;

namespace Lupe.UI.Pages;

/// <summary>
/// Kamera: Live-Bild mit Markierung erkannter Codes, Zielhilfe, Kamera-Auswahl, Start/Stopp, Ergebnisleiste.
/// Die Kamera läuft nur, solange die Seite zu sehen ist (Seite verlassen oder Fenster ausgeblendet: aus; zurück:
/// wieder an, wenn sie lief).
/// </summary>
public partial class CameraPage : UserControl, IPage
{
    private enum Hint { None, Stopped, Starting, NoCamera, Error }

    private sealed record MarkerUpdate(IReadOnlyList<CodeMarker> Markers, int Generation);

    private readonly AppState _state;
    private readonly ObservableCollection<CodeItem> _results = [];
    private readonly DispatcherTimer _markerTimer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<CameraInfo> _cameras = [];
    private bool _listed;
    private bool _resumeOnShow;
    private bool _suppressSelection;
    private bool _starting;

    // Laufende Kamera; _generation trennt Ereignisse einer gestoppten Schleife von denen der neuen
    private IFrameSource? _source;
    private ScanLoop? _loop;
    private volatile LiveBarcodeDecoder? _decoder;
    private int _generation;

    // Vorschau: immer nur das neueste Bild zeichnen, ältere verwerfen (keine Warteschlange auf dem UI-Thread)
    private WriteableBitmap? _bitmap;
    private PixelImage? _pendingFrame;
    private int _renderQueued;
    private MarkerUpdate? _pendingMarkers;
    private int _markersQueued;
    private DateTime _markersUntil;

    internal CameraPage(AppState state)
    {
        InitializeComponent();
        _state = state;
        ResultList.ItemsSource = _results;
        AimToggle.IsChecked = state.Settings.AimHelp;
        View.ShowAim = state.Settings.AimHelp;

        // Markierungen verschwinden kurz nachdem der Code nicht mehr gesehen wird
        _markerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _markerTimer.Tick += (_, _) =>
        {
            if (View.Markers.Count > 0 && DateTime.UtcNow > _markersUntil) View.Markers = [];
        };
        state.SettingsChanged += OnSettingsChanged;
        ShowHint(Hint.Stopped);
        UpdateTypeHint();
    }

    /// <summary>„Eintippen“ für die Kamera an: Lupe arbeitet wie ein Handscanner und tippt in das Programm vorn.</summary>
    private bool ScannerMode => _state.Settings.Actions.HasFlag(ResultActions.Eintippen);

    private void UpdateTypeHint() => TypeHint.Text = ScannerMode
        ? Strings.Page_Camera_TypeOnHint
        : Strings.Page_Camera_TypeOffHint;

    // ================= Sichtbarkeit =================

    public void OnShown()
    {
        _keptWhileMinimized = false;
        // Screenshots stellen ihren Zustand selbst her – eine langsame Kamerasuche käme ihnen sonst dazwischen
        if (_state.Strict) return;
        if (!_listed) Ui.Run(_state, ListCamerasAsync, Strings.Page_Camera_SearchCameras);
        else if (_resumeOnShow)
        {
            _resumeOnShow = false;
            Ui.Run(_state, StartAsync, Strings.Page_Camera_StartCamera);
        }
    }

    public void OnHidden()
    {
        if (_loop is null && !_starting) return;
        // Als Handscanner läuft die Kamera auch bei minimiertem Fenster weiter und tippt in das Programm vorn. Beim
        // Seitenwechsel und beim Schließen des Fensters (Infobereich) hält sie an – unsichtbar soll sie nicht laufen.
        if (ScannerMode && TopLevel.GetTopLevel(this) is Window { IsVisible: true, WindowState: WindowState.Minimized } window)
        {
            WatchWindow(window);
            _keptWhileMinimized = true;
            return;
        }
        _keptWhileMinimized = false;
        _resumeOnShow = true;
        Ui.Run(_state, StopCameraAsync, Strings.Page_Camera_StopCamera);
    }

    private bool _keptWhileMinimized;
    private Window? _watchedWindow;

    /// <summary>Minimiert weitergelaufen und dann das Fenster ganz geschlossen: jetzt anhalten.</summary>
    private void WatchWindow(Window window)
    {
        if (_watchedWindow == window) return;
        _watchedWindow = window;
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty && !window.IsVisible && _keptWhileMinimized) OnHidden();
        };
    }

    // ================= Kameras =================

    private async Task ListCamerasAsync()
    {
        _listed = true;
        CameraBox.IsEnabled = false;
        string? problem = null;
        var list = new List<CameraInfo>();
        try { list.AddRange(await Cameras.ListAsync()); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { problem = ex.Message; }
        if (_state.IsDemo) list.Insert(0, DemoData.Camera);

        _cameras = list;
        _suppressSelection = true;
        CameraBox.ItemsSource = list;
        CameraBox.SelectedItem = Cameras.Find(list, _state.Settings.Camera);
        _suppressSelection = false;
        CameraBox.IsEnabled = list.Count > 0;

        if (list.Count == 0) ShowHint(Hint.NoCamera, problem);
        else if (_loop is null) ShowHint(Hint.Stopped);
        UpdateButtons();
    }

    private void Refresh_Click(object? sender, RoutedEventArgs e) => Ui.Run(_state, ListCamerasAsync, Strings.Page_Camera_SearchCameras);

    private void CameraBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || CameraBox.SelectedItem is not CameraInfo info) return;
        if (info.Id != _state.Settings.Camera) _state.UpdateSettings(s => s with { Camera = info.Id });
        // Läuft eine andere Kamera: auf die gewählte umschalten
        if (_loop is not null) Ui.Run(_state, StartAsync, Strings.Page_Camera_StartCamera);
    }

    // ================= Start und Stopp =================

    private void StartStop_Click(object? sender, RoutedEventArgs e)
    {
        if (_loop is not null) Ui.Run(_state, StopCameraAsync, Strings.Page_Camera_StopCamera);
        else Ui.Run(_state, StartAsync, Strings.Page_Camera_StartCamera);
    }

    private async Task StartAsync()
    {
        if (CameraBox.SelectedItem is not CameraInfo info) return;
        _starting = true;
        await _gate.WaitAsync();
        try
        {
            await StopCoreAsync();
            int generation = ++_generation;
            ShowHint(Hint.Starting);
            UpdateButtons(starting: true);

            // Erkennung vor der Kamera anlegen: Scheitert sie, ist noch keine Kamera geöffnet, die freizugeben wäre.
            // Der Live-Decoder nimmt nur die schnelle Stufe und wenige Kerne – das nächste Bild ist ohnehin eine neue Chance.
            var decoder = _state.CreateLiveDecoder();
            IFrameSource source;
            try { source = info.Id == DemoData.CameraId ? DemoData.CreateCamera() : Cameras.Open(info); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                ShowHint(Hint.Error, ex.Message);
                return;
            }

            ScanLoop loop;
            try { loop = new ScanLoop(source, image => Decode(generation, image), Math.Max(0, _state.Settings.RepeatSuppressMs)); }
            catch
            {
                // Die Kamera ist schon offen – nicht bis zum Programmende blockiert lassen
                await source.DisposeAsync();
                throw;
            }
            _decoder = decoder;
            loop.Frame += (_, image) => OnFrame(generation, image);
            loop.Detected += (_, hit) => Dispatcher.UIThread.Post(() => Ui.Run(_state, () => OnDetectedAsync(generation, hit.Codes), Strings.Page_Camera_ProcessHit));
            loop.Failed += (_, message) => Dispatcher.UIThread.Post(() => OnFailed(generation, message));
            _source = source;
            _loop = loop;
            try
            {
                await loop.StartAsync();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                await StopCoreAsync();
                ShowHint(Hint.Error, ex.Message);
                return;
            }
            LiveText.Text = source.Name;
            LiveTag.IsVisible = true;
            _markerTimer.Start();
        }
        finally
        {
            _starting = false;
            _gate.Release();
            UpdateButtons();
        }
    }

    /// <summary>Kamera aus (Seite verlassen, Fenster zu, Programmende).</summary>
    public async Task StopCameraAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await StopCoreAsync();
            ShowHint(Hint.Stopped);
        }
        finally
        {
            _gate.Release();
            UpdateButtons();
        }
    }

    private async Task StopCoreAsync()
    {
        Interlocked.Increment(ref _generation);
        var loop = _loop;
        var source = _source;
        _loop = null;
        _source = null;
        _decoder = null;
        _markerTimer.Stop();
        LiveTag.IsVisible = false;
        View.Markers = [];
        if (loop is not null)
        {
            try { await loop.StopAsync(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { _state.Report(NoticeKind.Warning, string.Format(Strings.Page_Camera_StopCameraFailed, ex.Message)); }
            try { await loop.DisposeAsync(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { _state.Report(NoticeKind.Warning, string.Format(Strings.Page_Camera_ReleaseCameraFailed, ex.Message)); }
        }
        if (source is not null)
        {
            // Die Schleife gibt ihre Quelle nicht zwingend frei – die Kamera gehört der Seite
            try { await source.DisposeAsync(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { _state.Report(NoticeKind.Warning, string.Format(Strings.Page_Camera_ReleaseCameraFailed, ex.Message)); }
        }
        // Letztes Bild nicht stehen lassen: ohne laufende Kamera zeigt der Sucher den Hinweis
        View.Source = null;
        _bitmap = null;
    }

    // ================= Bilder und Treffer (Ereignisse von Hintergrund-Threads) =================

    private IReadOnlyList<DecodedCode> Decode(int generation, PixelImage image)
    {
        var decoder = _decoder;
        if (decoder is null || generation != Volatile.Read(ref _generation)) return [];
        var codes = decoder.Decode(image);
        // Jede Erkennung (auch Wiederholungen, die die Sperre nicht meldet) zeigt die Markierung – so folgt sie dem Code.
        // Den Umriss gleich hier berechnen: Hier ist das Bild noch da (1D-Codes: Band so hoch wie die Striche).
        Interlocked.Exchange(ref _pendingMarkers, new MarkerUpdate(Markers(codes, numbered: false, image), generation));
        if (Interlocked.Exchange(ref _markersQueued, 1) == 0) Dispatcher.UIThread.Post(ApplyMarkers, DispatcherPriority.Render);
        return codes;
    }

    private void OnFrame(int generation, PixelImage image)
    {
        if (generation != Volatile.Read(ref _generation)) return;
        Interlocked.Exchange(ref _pendingFrame, image);
        if (Interlocked.Exchange(ref _renderQueued, 1) == 0) Dispatcher.UIThread.Post(() => RenderFrame(generation), DispatcherPriority.Render);
    }

    private void RenderFrame(int generation)
    {
        Interlocked.Exchange(ref _renderQueued, 0);
        var image = Interlocked.Exchange(ref _pendingFrame, null);
        if (image is null || generation != _generation || _loop is null) return;
        ShowImage(image);
        if (Placeholder.IsVisible) ShowHint(Hint.None);
    }

    private void ShowImage(PixelImage image)
    {
        if (_bitmap is null || _bitmap.PixelSize.Width != image.Width || _bitmap.PixelSize.Height != image.Height)
        {
            // Undurchsichtig: Alpha egal (siehe Images.ToBitmap) – ein Treiber mit Alpha 0 ergäbe sonst einen leeren Sucher
            _bitmap = Images.ToBitmap(image);
            View.Source = _bitmap;
            return;
        }
        Images.Write(_bitmap, image);
        View.Refresh();
    }

    private void ApplyMarkers()
    {
        Interlocked.Exchange(ref _markersQueued, 0);
        if (Interlocked.Exchange(ref _pendingMarkers, null) is not { } pending || pending.Generation != _generation) return;
        if (pending.Markers.Count > 0)
        {
            View.Markers = pending.Markers;
            _markersUntil = DateTime.UtcNow.AddMilliseconds(700);
        }
    }

    /// <param name="image">Das Bild, in dem die Codes gefunden wurden – für Umrisse, die sich an die Striche halten.</param>
    internal static IReadOnlyList<CodeMarker> Markers(IReadOnlyList<DecodedCode> codes, bool numbered, PixelImage? image = null) =>
        codes.Select((c, i) => CodeMarker.From(c, numbered && codes.Count > 1 ? $"{i + 1}  {Labels.Format(c.Format)}" : Labels.Format(c.Format), image))
             .OfType<CodeMarker>().ToList();

    private async Task OnDetectedAsync(int generation, IReadOnlyList<DecodedCode> codes)
    {
        if (generation != _generation || codes.Count == 0) return;
        // Wie ein Handscanner: „Eintippen“ schreibt in das Programm, das gerade vorn ist – Lupe selbst zählt nicht
        var active = LupePlatform.Current.ActiveWindow;
        var foreground = active.Foreground();
        nint typeTarget = TypeTargets.From(active, foreground, lupeActive: TopLevel.GetTopLevel(this) is Window { IsActive: true });
        var report = await _state.Hits.ReportAsync(codes, ScanSource.Kamera, typeTarget);
        ShowResults(report.Codes, report.Entries, report.Time, report.NeedsChoice ? null : report.Outcome);
    }

    /// <param name="entries">Die Verlaufseinträge der Codes (gleiche Reihenfolge) – Kopieren und Link öffnen aus der Zeile werden darin vermerkt.</param>
    private void ShowResults(IReadOnlyList<DecodedCode> codes, IReadOnlyList<HistoryEntry?> entries, DateTimeOffset time, ActionOutcome? outcome)
    {
        _results.Clear();
        for (int i = 0; i < codes.Count; i++)
            _results.Add(new CodeItem(codes[i], ScanSource.Kamera, time, _state.LinkFor(codes[i].Text), codes.Count > 1 ? i + 1 : 0,
                i < entries.Count ? entries[i] : null));
        ResultEmpty.IsVisible = _results.Count == 0;
        CopyAllButton.IsVisible = _results.Count > 1;
        ResultTitle.Text = codes.Count > 1
            ? string.Format(Strings.Page_Camera_CodesDetectedAt, codes.Count, Labels.ShortTime(time, DateTimeOffset.Now))
            : string.Format(Strings.Page_Camera_LastDetectedAt, Labels.ShortTime(time, DateTimeOffset.Now));

        string done = codes.Count > 1
            ? Strings.Page_Camera_MultipleCodes
            : outcome is null ? "" : outcome.Summary;
        ResultDone.Text = done;
        ResultDone.IsVisible = done.Length > 0;
    }

    private void OnFailed(int generation, string message)
    {
        if (generation != _generation) return;
        _resumeOnShow = false;
        Ui.Run(_state, async () =>
        {
            await _gate.WaitAsync();
            try { await StopCoreAsync(); }
            finally { _gate.Release(); }
            ShowHint(Hint.Error, message);
            UpdateButtons();
        }, Strings.Page_Camera_StopCamera);
    }

    // ================= Anzeige =================

    private void ShowHint(Hint hint, string? detail = null)
    {
        Placeholder.IsVisible = hint != Hint.None;
        PlaceholderStart.IsVisible = hint is Hint.Stopped or Hint.Error;
        GoScreen.IsVisible = GoFile.IsVisible = hint is Hint.NoCamera or Hint.Error;
        (PlaceholderGlyph.Text, PlaceholderTitle.Text, PlaceholderText.Text, PlaceholderStartText.Text) = hint switch
        {
            Hint.Starting => ("\uE722", Strings.Page_Camera_Starting, "", ""),
            Hint.NoCamera => ("\uE722", Strings.Page_Camera_NoCameraTitle,
                detail is null ? Strings.Page_Camera_NoCameraText : string.Format(Strings.Page_Camera_NoCameraTextWithDetail, detail), ""),
            Hint.Error => ("\uE7BA", Strings.Page_Camera_UnavailableTitle, detail ?? Strings.Page_Camera_StartFailed, Strings.Page_Camera_TryAgain),
            _ => ("\uE722", Strings.Page_Camera_Off, Strings.Page_Camera_OffText, Strings.Page_Camera_StartCamera),
        };
        PlaceholderText.IsVisible = PlaceholderText.Text?.Length > 0;
    }

    private void UpdateButtons(bool starting = false)
    {
        bool running = _loop is not null;
        StartButton.IsEnabled = !starting && CameraBox.SelectedItem is CameraInfo;
        StartText.Text = running ? Strings.Page_Camera_Stop : Strings.Page_Camera_Start;
        StartGlyph.Text = running ? "\uE71A" : "\uE768";
    }

    private void Aim_Click(object? sender, RoutedEventArgs e)
    {
        bool on = AimToggle.IsChecked == true;
        _state.UpdateSettings(s => s with { AimHelp = on });
    }

    private void OnSettingsChanged(LupeSettings old, LupeSettings now)
    {
        AimToggle.IsChecked = now.AimHelp;
        View.ShowAim = now.AimHelp;
        if (old.Actions != now.Actions) UpdateTypeHint();
        if (!old.Formats.SequenceEqual(now.Formats) && _loop is not null) _decoder = _state.CreateLiveDecoder();
        // Die Wiederholungssperre gibt der Schleife ihr Konstruktor mit – also neu starten
        if (old.RepeatSuppressMs != now.RepeatSuppressMs && _loop is not null) Ui.Run(_state, StartAsync, Strings.Page_Camera_StartCamera);
        // Link-Regeln geändert: Knöpfe der Ergebnisleiste neu bewerten
        if (!old.LinkRules.SequenceEqual(now.LinkRules) && _results.Count > 0)
            ShowResults([.. _results.Select(r => r.Code)], [.. _results.Select(r => r.Entry)], _results[0].Time, null);
    }

    private void GoScreen_Click(object? sender, RoutedEventArgs e) => _state.RequestPage("bildschirm");
    private void GoFile_Click(object? sender, RoutedEventArgs e) => _state.RequestPage("datei");

    private async void CopyAll_Click(object? sender, RoutedEventArgs e)
    {
        if (await _state.Hits.CopyAllAsync([.. _results]) is { } error) _state.Report(NoticeKind.Error, error);
        else _state.Report(NoticeKind.Success, string.Format(Strings.Page_Shared_CopiedAll, _results.Count));
    }

    // ================= Screenshots =================

    public async Task PrepareScreenshotAsync()
    {
        await StopCameraAsync();
        _listed = true;
        _suppressSelection = true;
        CameraBox.ItemsSource = new[] { DemoData.Camera };
        CameraBox.SelectedIndex = 0;
        _suppressSelection = false;

        var frame = DemoData.CameraFrame();
        // Derselbe Decoder wie bei laufender Kamera – erkennt er den Demo-Code nicht, scheitert der Screenshot sichtbar
        // (Die schnelle Stufe ist zeitbegrenzt – auf einem ausgelasteten Rechner bekommt sie ein paar Anläufe)
        var live = _state.CreateLiveDecoder();
        IReadOnlyList<DecodedCode> codes = [];
        for (int attempt = 0; attempt < 3 && codes.Count == 0; attempt++) codes = live.Decode(frame);
        if (codes.Count == 0) throw new InvalidOperationException("Im Demo-Kamerabild wurde kein Code erkannt.");
        ShowImage(frame);
        View.Markers = Markers(codes, numbered: false, frame);
        ShowHint(Hint.None);
        LiveText.Text = DemoData.Camera.Name;
        LiveTag.IsVisible = true;
        // Wie bei laufender Kamera: Knopf bedienbar (StopCameraAsync hat ihn ohne gewählte Kamera ausgegraut)
        UpdateButtons();
        StartText.Text = Strings.Page_Camera_Stop;
        StartGlyph.Text = "\uE71A";
        ShowResults(codes, [], DemoData.ScreenshotTime, new ActionOutcome(ResultActions.Kopieren, []));
        ResultTitle.Text = string.Format(Strings.Page_Camera_LastDetectedAt, "10:42:07");
        await Ui.IdleAsync();
    }
}
