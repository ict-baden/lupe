using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Lupe.Core.Decoding;
using Lupe.Core.Model;
using Lupe.Demo;
using Lupe.Services;
using Microsoft.Win32;

namespace Lupe.UI.Pages;

/// <summary>Datei: Ablagefläche, „Bild öffnen …“, „Aus Zwischenablage“ (Strg+V), Vorschau mit Markierungen, Liste der Codes.</summary>
public partial class FilePage : UserControl, IPage
{
    private static readonly string[] FallbackExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"];

    private readonly AppState _state;
    private readonly ObservableCollection<CodeItem> _results = [];
    private CancellationTokenSource? _work;
    private string? _lastFolder;

    internal FilePage(AppState state)
    {
        InitializeComponent();
        _state = state;
        ResultList.ItemsSource = _results;
        CodeActions.Bind(this, state);
        state.SettingsChanged += (old, now) =>
        {
            // Link-Regeln geändert: Knöpfe „Link öffnen“ neu bewerten
            if (!old.LinkRules.SequenceEqual(now.LinkRules) && _results.Count > 0)
                ShowCodes([.. _results.Select(r => r.Code)], [.. _results.Select(r => r.Entry)], _results[0].Source, _results[0].Time);
        };
    }

    public void OnShown() { }
    public void OnHidden() { }

    // ================= Bild holen =================

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var extensions = Extensions();
        var pattern = string.Join(";", extensions.Select(x => "*" + x));
        var dialog = new OpenFileDialog
        {
            Title = "Bild mit Code öffnen",
            Filter = $"Bilder ({string.Join(", ", extensions.Take(6).Select(x => x.TrimStart('.').ToUpperInvariant()))} …)|{pattern}|Alle Dateien (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (_lastFolder is not null) dialog.InitialDirectory = _lastFolder;
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        _lastFolder = Path.GetDirectoryName(dialog.FileName);
        Ui.Run(_state, () => LoadFileAsync(dialog.FileName, ScanSource.Datei), "Bild lesen");
    }

    private void Paste_Click(object sender, RoutedEventArgs e) => PasteFromClipboard();

    private const string ClipboardName = "Bild aus der Zwischenablage";

    /// <summary>Strg+V: Bild, PNG-Daten oder kopierte Bilddatei aus der Zwischenablage lesen.</summary>
    public void PasteFromClipboard()
    {
        ClipboardContent content;
        try { content = ClipboardImage.Read(); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            _state.Report(NoticeKind.Warning, $"Die Zwischenablage ließ sich nicht lesen: {ex.Message}");
            return;
        }
        if (content.File is { } file) Ui.Run(_state, () => LoadFileAsync(file, ScanSource.Zwischenablage), "Bild lesen");
        else if (content.Png is { } png) Ui.Run(_state, () => LoadPngAsync(png), "Bild lesen");
        else if (content.Bitmap is { } image) Ui.Run(_state, () => DecodeAsync(image, ScanSource.Zwischenablage, ClipboardName), "Bild lesen");
        else _state.Report(NoticeKind.Info, "In der Zwischenablage ist kein Bild. Tipp: Mit Win+Umschalt+S ein Bildschirmfoto machen – oder gleich „Ausschnitt scannen“.");
    }

    /// <summary>PNG-Daten aus der Zwischenablage – geladen wie eine Datei (Transparenz auf Weiß, Größenschutz); unbrauchbar: die Bitmap.</summary>
    private async Task LoadPngAsync(byte[] png)
    {
        var ct = Restart();
        PixelImage image;
        try
        {
            using var stream = new MemoryStream(png, writable: false);
            image = await ImageFile.LoadAsync(stream, ClipboardName, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && !_state.Strict)
        {
            PixelImage? bitmap = null;
            try { bitmap = ClipboardImage.ReadBitmap(); }
            catch (Exception clipboard) when (clipboard is System.Runtime.InteropServices.ExternalException or InvalidOperationException or NotSupportedException or ArgumentException) { }
            if (bitmap is null)
            {
                _state.Report(NoticeKind.Error, $"Das Bild aus der Zwischenablage ließ sich nicht lesen: {ex.Message}");
                return;
            }
            image = bitmap;
        }
        await DecodeAsync(image, ScanSource.Zwischenablage, ClipboardName, ct);
    }

    // ================= Ziehen und Ablegen =================

    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool ok = DroppedFile(e.Data) is not null || e.Data.GetDataPresent(DataFormats.Bitmap);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DropFrame.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, ok ? "AccentFillColorDefaultBrush" : "DropZoneStroke");
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) =>
        DropFrame.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "DropZoneStroke");

    private void OnDrop(object sender, DragEventArgs e)
    {
        OnDragLeave(sender, e);
        e.Handled = true;
        if (DroppedFile(e.Data) is { } file)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 1 })
                _state.Report(NoticeKind.Info, $"Mehrere Dateien abgelegt – gelesen wird „{Path.GetFileName(file)}“.");
            Ui.Run(_state, () => LoadFileAsync(file, ScanSource.Datei), "Bild lesen");
        }
        else if (e.Data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
            Ui.Run(_state, () => DecodeAsync(Images.FromBitmap(bitmap), ScanSource.Datei, "abgelegtes Bild"), "Bild lesen");
        else
            _state.Report(NoticeKind.Info, "Das ist kein Bild, das Lupe lesen kann.");
    }

    private static string? DroppedFile(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files
            ? files.FirstOrDefault(ClipboardImage.IsImageFile)
            : null;

    // ================= Lesen =================

    private async Task LoadFileAsync(string path, ScanSource source)
    {
        var ct = Restart();
        SetBusy("Bild wird geladen …");
        PixelImage image;
        try
        {
            image = await ImageFile.LoadAsync(path, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && !_state.Strict)
        {
            SetBusy(null);
            _state.Report(NoticeKind.Error, $"„{Path.GetFileName(path)}“ ließ sich nicht lesen: {ex.Message}");
            return;
        }
        await DecodeAsync(image, source, Path.GetFileName(path), ct);
    }

    private async Task DecodeAsync(PixelImage image, ScanSource source, string name, CancellationToken ct = default)
    {
        if (ct == default) ct = Restart();
        SourceText.Text = $"{name} · {image.Width} × {image.Height} Pixel";
        SourceText.ToolTip = name;
        View.Source = Images.ToBitmap(image);
        View.Markers = [];
        DropHint.Visibility = Visibility.Collapsed;
        _results.Clear();
        SetBusy("Suche Codes …");

        IReadOnlyList<DecodedCode> codes;
        var decoder = _state.CreateDecoder();
        try
        {
            codes = await Task.Run(() => decoder.Decode(image, ct), ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (!ct.IsCancellationRequested) SetBusy(null);
        }
        if (ct.IsCancellationRequested) return;

        var report = await _state.Hits.ReportAsync(codes, source);
        ShowCodes(codes, report.Entries, source, report.Time);
        View.Markers = CameraPage.Markers(codes, numbered: true, image);
        ResultDone.Text = codes.Count switch
        {
            0 => "Tipp: Den Code möglichst groß und scharf aufnehmen; bei Bildschirmfotos besser „Ausschnitt scannen“.",
            1 => report.Outcome.Summary,
            _ => "Alle Codes stehen im Verlauf. Den gewünschten kopieren oder öffnen – oder alle zeilenweise kopieren.",
        };
        ResultDone.Visibility = ResultDone.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <param name="entries">Die Verlaufseinträge der Codes (gleiche Reihenfolge) – Kopieren und Link öffnen aus der Zeile werden darin vermerkt.</param>
    private void ShowCodes(IReadOnlyList<DecodedCode> codes, IReadOnlyList<HistoryEntry?> entries, ScanSource source, DateTimeOffset time)
    {
        _results.Clear();
        for (int i = 0; i < codes.Count; i++)
            _results.Add(new CodeItem(codes[i], source, time, _state.LinkFor(codes[i].Text), codes.Count > 1 ? i + 1 : 0,
                i < entries.Count ? entries[i] : null));
        ResultTitle.Text = codes.Count switch
        {
            0 => "Kein Code gefunden",
            1 => "1 Code gefunden",
            _ => $"{codes.Count} Codes gefunden",
        };
        ResultEmpty.Visibility = codes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultEmpty.Text = codes.Count == 0 ? "In diesem Bild hat Lupe keinen QR- oder Barcode gefunden." : "";
        CopyAllButton.Visibility = codes.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private CancellationToken Restart()
    {
        _work?.Cancel();
        _work?.Dispose();
        _work = new CancellationTokenSource();
        return _work.Token;
    }

    private void SetBusy(string? text)
    {
        BusyTag.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        BusyText.Text = text ?? "";
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        if (_state.Hits.CopyAll([.. _results]) is { } error) _state.Report(NoticeKind.Error, error);
        else _state.Report(NoticeKind.Success, $"{_results.Count} Codes zeilenweise kopiert.");
    }

    private static IReadOnlyList<string> Extensions()
    {
        try
        {
            var list = ImageFile.Extensions;
            return list.Count > 0 ? list : FallbackExtensions;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return FallbackExtensions;
        }
    }

    // ================= Screenshots =================

    public async Task PrepareScreenshotAsync()
    {
        var image = DemoData.DeliveryNoteImage();
        var codes = _state.CreateDecoder().Decode(image);
        if (codes.Count < 2) throw new InvalidOperationException($"Im Demo-Lieferschein wurden nur {codes.Count} Codes erkannt (erwartet: 4).");
        SourceText.Text = $"lieferschein-4711.png · {image.Width} × {image.Height} Pixel";
        View.Source = Images.ToBitmap(image);
        View.Markers = CameraPage.Markers(codes, numbered: true, image);
        DropHint.Visibility = Visibility.Collapsed;
        ShowCodes(codes, [], ScanSource.Datei, DemoData.ScreenshotTime);
        ResultDone.Text = "Alle Codes stehen im Verlauf. Den gewünschten kopieren oder öffnen – oder alle zeilenweise kopieren.";
        ResultDone.Visibility = Visibility.Visible;
        await Ui.IdleAsync();
    }
}
