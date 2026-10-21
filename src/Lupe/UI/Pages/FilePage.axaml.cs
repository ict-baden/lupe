// SPDX-License-Identifier: MIT
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Lupe.Core.Decoding;
using Lupe.Core.Model;
using Lupe.Core.Resources;
using Lupe.Demo;
using Lupe.Services;

namespace Lupe.UI.Pages;

/// <summary>Datei: Ablagefläche, „Bild öffnen …“, „Aus Zwischenablage“ (Strg+V), Vorschau mit Markierungen, Liste der Codes.</summary>
public partial class FilePage : UserControl, IPage
{
    private static readonly string[] FallbackExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"];

    private readonly AppState _state;
    private readonly ObservableCollection<CodeItem> _results = [];
    private CancellationTokenSource? _work;
    private IStorageFolder? _lastFolder;

    internal FilePage(AppState state)
    {
        InitializeComponent();
        _state = state;
        ResultList.ItemsSource = _results;
        if (OperatingSystem.IsWindows())
            DropHintText.Text = Strings.Page_File_DropHintTextWindows;
        if (OperatingSystem.IsMacOS()) ToolTip.SetTip(PasteButton, "⌘V");
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
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

    private async void Open_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage) return;
        var extensions = Extensions();
        var images = new FilePickerFileType(string.Format(Strings.Page_File_ImagesFilter, string.Join(", ", extensions.Take(6).Select(x => x.TrimStart('.').ToUpperInvariant()))))
        {
            Patterns = [.. extensions.Select(x => "*" + x)],
            AppleUniformTypeIdentifiers = ["public.image"],
            MimeTypes = ["image/*"],
        };
        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Strings.Page_File_OpenDialogTitle,
                AllowMultiple = false,
                FileTypeFilter = [images, FilePickerFileTypes.All],
                SuggestedStartLocation = _lastFolder,
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _state.Report(NoticeKind.Error, string.Format(Strings.Page_File_OpenDialogFailed, ex.Message));
            return;
        }
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        _lastFolder = await files[0].GetParentAsync();
        Ui.Run(_state, () => LoadFileAsync(path, ScanSource.Datei), Strings.Page_File_ReadImage);
    }

    private void Paste_Click(object? sender, RoutedEventArgs e) => PasteFromClipboard();

    private static string ClipboardName => Strings.Page_File_ClipboardImageName;

    /// <summary>Strg+V: Bild, PNG-Daten oder kopierte Bilddatei aus der Zwischenablage lesen.</summary>
    public void PasteFromClipboard() => Ui.Run(_state, PasteAsync, Strings.Page_File_ReadClipboard);

    private async Task PasteAsync()
    {
        ClipboardContent content;
        try { content = await ClipboardImage.ReadAsync(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _state.Report(NoticeKind.Warning, string.Format(Strings.Page_File_ClipboardReadFailed, ex.Message));
            return;
        }
        if (content.File is { } file) await LoadFileAsync(file, ScanSource.Zwischenablage);
        else if (content.Png is { } png) await LoadPngAsync(png);
        else if (content.Bitmap is { } image) await DecodeAsync(image, ScanSource.Zwischenablage, ClipboardName);
        else _state.Report(NoticeKind.Info, OperatingSystem.IsWindows()
            ? Strings.Page_File_NoImageInClipboardWindows
            : Strings.Page_File_NoImageInClipboard);
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
            try { bitmap = await ClipboardImage.ReadBitmapAsync(); }
            catch (Exception clipboard) when (clipboard is not OutOfMemoryException) { }
            if (bitmap is null)
            {
                _state.Report(NoticeKind.Error, string.Format(Strings.Page_File_ClipboardImageFailed, ex.Message));
                return;
            }
            image = bitmap;
        }
        await DecodeAsync(image, ScanSource.Zwischenablage, ClipboardName, ct);
    }

    // ================= Ziehen und Ablegen =================

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool ok = DroppedFile(e.DataTransfer) is not null || e.DataTransfer.Contains(DataFormat.Bitmap);
        e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DropFrame.SetResource(Avalonia.Controls.Shapes.Shape.StrokeProperty, ok ? "AccentFillColorDefaultBrush" : "DropZoneStroke");
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e) =>
        DropFrame.SetResource(Avalonia.Controls.Shapes.Shape.StrokeProperty, "DropZoneStroke");

    private void OnDrop(object? sender, DragEventArgs e)
    {
        OnDragLeave(sender, e);
        e.Handled = true;
        if (DroppedFile(e.DataTransfer) is { } file)
        {
            if (e.DataTransfer.TryGetFiles() is { Length: > 1 })
                _state.Report(NoticeKind.Info, string.Format(Strings.Page_File_MultipleFilesDropped, Path.GetFileName(file)));
            Ui.Run(_state, () => LoadFileAsync(file, ScanSource.Datei), Strings.Page_File_ReadImage);
        }
        else if (e.DataTransfer.TryGetBitmap() is { } bitmap)
        {
            var image = Images.FromBitmap(bitmap);
            bitmap.Dispose();
            Ui.Run(_state, () => DecodeAsync(image, ScanSource.Datei, Strings.Page_File_DroppedImageName), Strings.Page_File_ReadImage);
        }
        else
            _state.Report(NoticeKind.Info, Strings.Page_File_NotAnImage);
    }

    private static string? DroppedFile(IDataTransfer data) =>
        data.TryGetFiles() is { } files
            ? files.Select(f => f.TryGetLocalPath()).OfType<string>().FirstOrDefault(ClipboardImage.IsImageFile)
            : null;

    // ================= Lesen =================

    private async Task LoadFileAsync(string path, ScanSource source)
    {
        var ct = Restart();
        SetBusy(Strings.Page_File_LoadingImage);
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
            _state.Report(NoticeKind.Error, string.Format(Strings.Page_File_FileReadFailed, Path.GetFileName(path), ex.Message));
            return;
        }
        await DecodeAsync(image, source, Path.GetFileName(path), ct);
    }

    private async Task DecodeAsync(PixelImage image, ScanSource source, string name, CancellationToken ct = default)
    {
        if (ct == default) ct = Restart();
        SourceText.Text = string.Format(Strings.Page_File_SourceInfo, name, image.Width, image.Height);
        ToolTip.SetTip(SourceText, name);
        View.Source = Images.ToBitmap(image);
        View.Markers = [];
        DropHint.IsVisible = false;
        _results.Clear();
        SetBusy(Strings.Page_File_SearchingCodes);

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
        catch (DecoderUnavailableException ex)
        {
            // Die Erkennung lädt auf diesem System nicht (Meldung mit Fehlercode) – kein „Kein Code gefunden“
            _state.Report(NoticeKind.Error, ex.Message);
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
            0 => Strings.Page_File_NoCodeTip,
            1 => report.Outcome.Summary,
            _ => Strings.Page_File_AllCodesInHistory,
        };
        ResultDone.IsVisible = ResultDone.Text.Length > 0;
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
            0 => Strings.Page_File_CodesFound_Zero,
            1 => Strings.Page_File_CodesFound_One,
            _ => string.Format(Strings.Page_File_CodesFound_Other, codes.Count),
        };
        ResultEmpty.IsVisible = codes.Count == 0;
        ResultEmpty.Text = codes.Count == 0 ? Strings.Page_File_NoCodeInImage : "";
        CopyAllButton.IsVisible = codes.Count > 1;
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
        BusyTag.IsVisible = text is not null;
        BusyText.Text = text ?? "";
    }

    private async void CopyAll_Click(object? sender, RoutedEventArgs e)
    {
        if (await _state.Hits.CopyAllAsync([.. _results]) is { } error) _state.Report(NoticeKind.Error, error);
        else _state.Report(NoticeKind.Success, string.Format(Strings.Page_Shared_CopiedAll, _results.Count));
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
        SourceText.Text = string.Format(Strings.Page_File_SourceInfo, "lieferschein-4711.png", image.Width, image.Height);
        View.Source = Images.ToBitmap(image);
        View.Markers = CameraPage.Markers(codes, numbered: true, image);
        DropHint.IsVisible = false;
        ShowCodes(codes, [], ScanSource.Datei, DemoData.ScreenshotTime);
        ResultDone.Text = Strings.Page_File_AllCodesInHistory;
        ResultDone.IsVisible = true;
        await Ui.IdleAsync();
    }
}
