using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lupe.Core.Camera;
using Lupe.Core.Decoding;
using Lupe.Core.Model;
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

    private sealed record MarkerUpdate(IReadOnlyList<DecodedCode> Codes, int Generation);

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
    private volatile BarcodeDecoder? _decoder;
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
        CodeActions.Bind(this, state);
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
    }

    // ================= Sichtbarkeit =================

    public void OnShown()
    {
        // Screenshots stellen ihren Zustand selbst her – eine langsame Kamerasuche käme ihnen sonst dazwischen
        if (_state.Strict) return;
        if (!_listed) Ui.Run(_state, ListCamerasAsync, "Kameras suchen");
        else if (_resumeOnShow)
        {
            _resumeOnShow = false;
            Ui.Run(_state, StartAsync, "Kamera starten");
        }
    }

    public void OnHidden()
    {
        if (_loop is null && !_starting) return;
        _resumeOnShow = true;
        Ui.Run(_state, StopCameraAsync, "Kamera stoppen");
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
        CameraBox.SelectedItem = list.FirstOrDefault(c => c.Id == _state.Settings.Camera || c.Name == _state.Settings.Camera) ?? list.FirstOrDefault();
        _suppressSelection = false;
        CameraBox.IsEnabled = list.Count > 0;

        if (list.Count == 0) ShowHint(Hint.NoCamera, problem);
        else if (_loop is null) ShowHint(Hint.Stopped);
        UpdateButtons();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Ui.Run(_state, ListCamerasAsync, "Kameras suchen");

    private void CameraBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || CameraBox.SelectedItem is not CameraInfo info) return;
        if (info.Id != _state.Settings.Camera) _state.UpdateSettings(s => s with { Camera = info.Id });
        // Läuft eine andere Kamera: auf die gewählte umschalten
        if (_loop is not null) Ui.Run(_state, StartAsync, "Kamera starten");
    }

    // ================= Start und Stopp =================

    private void StartStop_Click(object sender, RoutedEventArgs e)
    {
        if (_loop is not null) Ui.Run(_state, StopCameraAsync, "Kamera stoppen");
        else Ui.Run(_state, StartAsync, "Kamera starten");
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

            // Erkennung vor der Kamera anlegen: Scheitert sie, ist noch keine Kamera geöffnet, die freizugeben wäre
            var decoder = _state.CreateDecoder();
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
            loop.Detected += (_, hit) => Dispatcher.BeginInvoke(() => Ui.Run(_state, () => OnDetectedAsync(generation, hit.Codes), "Treffer verarbeiten"));
            loop.Failed += (_, message) => Dispatcher.BeginInvoke(() => OnFailed(generation, message));
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
            LiveTag.Visibility = Visibility.Visible;
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
        LiveTag.Visibility = Visibility.Collapsed;
        View.Markers = [];
        if (loop is not null)
        {
            try { await loop.StopAsync(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { _state.Report(NoticeKind.Warning, $"Kamera stoppen: {ex.Message}"); }
            try { await loop.DisposeAsync(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { _state.Report(NoticeKind.Warning, $"Kamera freigeben: {ex.Message}"); }
        }
        if (source is not null)
        {
            // Die Schleife gibt ihre Quelle nicht zwingend frei – die Kamera gehört der Seite
            try { await source.DisposeAsync(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { _state.Report(NoticeKind.Warning, $"Kamera freigeben: {ex.Message}"); }
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
        // Jede Erkennung (auch Wiederholungen, die die Sperre nicht meldet) zeigt die Markierung – so folgt sie dem Code
        Interlocked.Exchange(ref _pendingMarkers, new MarkerUpdate(codes, generation));
        if (Interlocked.Exchange(ref _markersQueued, 1) == 0) Dispatcher.BeginInvoke(ApplyMarkers, DispatcherPriority.Render);
        return codes;
    }

    private void OnFrame(int generation, PixelImage image)
    {
        if (generation != Volatile.Read(ref _generation)) return;
        Interlocked.Exchange(ref _pendingFrame, image);
        if (Interlocked.Exchange(ref _renderQueued, 1) == 0) Dispatcher.BeginInvoke(() => RenderFrame(generation), DispatcherPriority.Render);
    }

    private void RenderFrame(int generation)
    {
        Interlocked.Exchange(ref _renderQueued, 0);
        var image = Interlocked.Exchange(ref _pendingFrame, null);
        if (image is null || generation != _generation || _loop is null) return;
        ShowImage(image);
        if (Placeholder.Visibility == Visibility.Visible) ShowHint(Hint.None);
    }

    private void ShowImage(PixelImage image)
    {
        if (_bitmap is null || _bitmap.PixelWidth != image.Width || _bitmap.PixelHeight != image.Height)
        {
            // Bgr32: Alpha egal (siehe Images.ToBitmap) – ein Treiber mit Alpha 0 ergäbe sonst einen leeren Sucher
            _bitmap = new WriteableBitmap(image.Width, image.Height, 96, 96, PixelFormats.Bgr32, null);
            View.Source = _bitmap;
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, image.Width, image.Height), image.Bgra, image.Stride, 0);
    }

    private void ApplyMarkers()
    {
        Interlocked.Exchange(ref _markersQueued, 0);
        if (Interlocked.Exchange(ref _pendingMarkers, null) is not { } pending || pending.Generation != _generation) return;
        if (pending.Codes.Count > 0)
        {
            View.Markers = Markers(pending.Codes, numbered: false);
            _markersUntil = DateTime.UtcNow.AddMilliseconds(700);
        }
    }

    internal static IReadOnlyList<CodeMarker> Markers(IReadOnlyList<DecodedCode> codes, bool numbered) =>
        codes.Select((c, i) => CodeMarker.From(c, numbered && codes.Count > 1 ? $"{i + 1}  {Labels.Format(c.Format)}" : Labels.Format(c.Format)))
             .OfType<CodeMarker>().ToList();

    private async Task OnDetectedAsync(int generation, IReadOnlyList<DecodedCode> codes)
    {
        if (generation != _generation || codes.Count == 0) return;
        var report = await _state.Hits.ReportAsync(codes, ScanSource.Kamera);
        ShowResults(report.Codes, report.Time, report.NeedsChoice ? null : report.Outcome);
    }

    private void ShowResults(IReadOnlyList<DecodedCode> codes, DateTimeOffset time, ActionOutcome? outcome)
    {
        _results.Clear();
        for (int i = 0; i < codes.Count; i++)
            _results.Add(new CodeItem(codes[i], ScanSource.Kamera, time, _state.LinkFor(codes[i].Text), codes.Count > 1 ? i + 1 : 0));
        ResultEmpty.Visibility = _results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CopyAllButton.Visibility = _results.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        ResultTitle.Text = codes.Count > 1 ? $"{codes.Count} Codes erkannt · {Labels.ShortTime(time, DateTimeOffset.Now)}" : $"Zuletzt erkannt · {Labels.ShortTime(time, DateTimeOffset.Now)}";

        string done = codes.Count > 1
            ? "Mehrere Codes im Bild – alle stehen im Verlauf. Den gewünschten kopieren oder öffnen."
            : outcome is null ? "" : outcome.Summary;
        ResultDone.Text = done;
        ResultDone.Visibility = done.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
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
        }, "Kamera stoppen");
    }

    // ================= Anzeige =================

    private void ShowHint(Hint hint, string? detail = null)
    {
        Placeholder.Visibility = hint == Hint.None ? Visibility.Collapsed : Visibility.Visible;
        PlaceholderStart.Visibility = hint is Hint.Stopped or Hint.Error ? Visibility.Visible : Visibility.Collapsed;
        GoScreen.Visibility = GoFile.Visibility = hint is Hint.NoCamera or Hint.Error ? Visibility.Visible : Visibility.Collapsed;
        (PlaceholderGlyph.Text, PlaceholderTitle.Text, PlaceholderText.Text, PlaceholderStartText.Text) = hint switch
        {
            Hint.Starting => ("\uE722", "Kamera wird gestartet …", "", ""),
            Hint.NoCamera => ("\uE722", "Keine Kamera gefunden – Bildschirm oder Datei nutzen",
                "Codes auf dem Bildschirm liest „Ausschnitt scannen“, Fotos und Scans die Seite „Datei“. Eine Webcam anschließen und oben „Kameras neu suchen“ klicken."
                + (detail is null ? "" : $"\n({detail})"), ""),
            Hint.Error => ("\uE7BA", "Kamera nicht verfügbar", detail ?? "Die Kamera ließ sich nicht starten.", "Erneut versuchen"),
            _ => ("\uE722", "Kamera ist aus",
                "Code in die Mitte des Bildes halten – Lupe erkennt ihn von selbst, meldet ihn mit einem Ton und legt ihn in den Verlauf.", "Kamera starten"),
        };
        PlaceholderText.Visibility = PlaceholderText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateButtons(bool starting = false)
    {
        bool running = _loop is not null;
        StartButton.IsEnabled = !starting && CameraBox.SelectedItem is CameraInfo;
        StartText.Text = running ? "Stoppen" : "Starten";
        StartGlyph.Text = running ? "\uE71A" : "\uE768";
    }

    private void Aim_Click(object sender, RoutedEventArgs e)
    {
        bool on = AimToggle.IsChecked == true;
        _state.UpdateSettings(s => s with { AimHelp = on });
    }

    private void OnSettingsChanged(LupeSettings old, LupeSettings now)
    {
        AimToggle.IsChecked = now.AimHelp;
        View.ShowAim = now.AimHelp;
        if (!old.Formats.SequenceEqual(now.Formats) && _loop is not null) _decoder = _state.CreateDecoder();
        // Die Wiederholungssperre gibt der Schleife ihr Konstruktor mit – also neu starten
        if (old.RepeatSuppressMs != now.RepeatSuppressMs && _loop is not null) Ui.Run(_state, StartAsync, "Kamera starten");
        // Link-Regeln geändert: Knöpfe der Ergebnisleiste neu bewerten
        if (!old.LinkRules.SequenceEqual(now.LinkRules) && _results.Count > 0)
            ShowResults([.. _results.Select(r => r.Code)], _results[0].Time, null);
    }

    private void GoScreen_Click(object sender, RoutedEventArgs e) => _state.RequestPage("bildschirm");
    private void GoFile_Click(object sender, RoutedEventArgs e) => _state.RequestPage("datei");

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        if (_state.Hits.CopyAll(_results.Select(r => r.Code)) is { } error) _state.Report(NoticeKind.Error, error);
        else _state.Report(NoticeKind.Success, $"{_results.Count} Codes zeilenweise kopiert.");
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
        var codes = _state.CreateDecoder().Decode(frame);
        if (codes.Count == 0) throw new InvalidOperationException("Im Demo-Kamerabild wurde kein Code erkannt.");
        ShowImage(frame);
        View.Markers = Markers(codes, numbered: false);
        ShowHint(Hint.None);
        LiveText.Text = DemoData.Camera.Name;
        LiveTag.Visibility = Visibility.Visible;
        // Wie bei laufender Kamera: Knopf bedienbar (StopCameraAsync hat ihn ohne gewählte Kamera ausgegraut)
        UpdateButtons();
        StartText.Text = "Stoppen";
        StartGlyph.Text = "\uE71A";
        ShowResults(codes, DemoData.ScreenshotTime, new ActionOutcome(ResultActions.Kopieren, []));
        ResultTitle.Text = "Zuletzt erkannt · 10:42:07";
        await Ui.IdleAsync();
    }
}
