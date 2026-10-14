using System.Diagnostics;
using Lupe.Core.Model;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;

namespace Lupe.Core.Camera;

/// <summary>Womit eine Kamerasitzung läuft (Diagnose, Tests).</summary>
/// <param name="Width">Breite des Kameraformats.</param>
/// <param name="Height">Höhe des Kameraformats.</param>
/// <param name="Subtype">Untertyp des Kameraformats (NV12, MJPG …).</param>
/// <param name="OutputWidth">Breite der gelieferten Bilder (ggf. herunterskaliert).</param>
/// <param name="OutputHeight">Höhe der gelieferten Bilder.</param>
/// <param name="Shared">Geteilter Lesemodus (Format nicht wählbar), weil die Kamera nicht exklusiv zu haben war.</param>
internal sealed record CaptureFormat(int Width, int Height, string Subtype, double FramesPerSecond, int OutputWidth, int OutputHeight, bool Shared);

/// <summary>
/// Eine geöffnete Kamera: MediaCapture (exklusiv, sonst geteilt nur lesend) und ein MediaFrameReader im Echtzeitmodus,
/// der Bilder als BGRA8 liefert. Bilder, die kommen, während das vorige noch verarbeitet wird, verfallen.
/// Laufzeitfehler (abgezogen, übernommen, Datenschutz) werden genau einmal gemeldet.
/// </summary>
internal sealed class CaptureSession : IAsyncDisposable
{
    /// <summary>So viele Bilder hintereinander dürfen bei der Umwandlung scheitern, bevor das als Ausfall gilt.</summary>
    private const int MaxConsecutiveConversionErrors = 30;
    /// <summary>So lange wartet die Freigabe höchstens auf einen hängenden Ereignisempfänger.</summary>
    private static readonly TimeSpan FrameDrainTimeout = TimeSpan.FromSeconds(5);
    /// <summary>Erst exklusiv (Format wählbar), dann geteilt nur lesend (wenn ein anderes Programm die Kamera hat).</summary>
    private static readonly MediaCaptureSharingMode[] SharingModes =
        [MediaCaptureSharingMode.ExclusiveControl, MediaCaptureSharingMode.SharedReadOnly];

    private readonly CameraInfo _camera;
    private readonly Action<CaptureSession, PixelImage> _onFrame;
    private readonly Action<CaptureSession, string> _onFailure;
    private readonly SemaphoreSlim _frameGate = new(1, 1);
    private readonly object _disposeLock = new();
    private string _name;
    private string _deviceId;
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private volatile bool _active;
    private int _failureClaimed;
    private int _conversionErrors;
    private Task? _disposeTask;
    // feste Delegaten, damit das Abmelden bei WinRT sicher dieselben Einträge trifft
    private readonly MediaCaptureFailedEventHandler _captureFailedHandler;
    private readonly Windows.Foundation.TypedEventHandler<MediaCapture, object> _streamStateHandler;
    private readonly Windows.Foundation.TypedEventHandler<MediaFrameReader, MediaFrameArrivedEventArgs> _frameArrivedHandler;

    public CaptureSession(CameraInfo camera, Action<CaptureSession, PixelImage> onFrame, Action<CaptureSession, string> onFailure)
    {
        _camera = camera;
        _name = CameraList.CleanName(camera.Name);
        _deviceId = camera.Id?.Trim() ?? "";
        _onFrame = onFrame;
        _onFailure = onFailure;
        _captureFailedHandler = OnCaptureFailed;
        _streamStateHandler = OnStreamStateChanged;
        _frameArrivedHandler = OnFrameArrived;
    }

    /// <summary>Liefert Bilder und ist nicht ausgefallen/angehalten.</summary>
    public bool IsActive => _active;

    public CaptureFormat? Format { get; private set; }

    /// <summary>Ab sofort keine Bilder und keine Meldungen mehr (Freigabe folgt mit <see cref="DisposeAsync"/>).</summary>
    public void Deactivate()
    {
        _active = false;
        Interlocked.Exchange(ref _failureClaimed, 1);
    }

    /// <summary>
    /// Öffnet und startet die Kamera. Ergebnis: null = läuft (oder ist dabei schon ausgefallen und hat das selbst
    /// gemeldet, dann ist <see cref="IsActive"/> false), sonst die deutsche Fehlermeldung. Wirft nur bei Abbruch.
    /// </summary>
    public async Task<string?> OpenAsync(CancellationToken ct)
    {
        try
        {
            var (group, problem) = await FindGroupAsync(ct).ConfigureAwait(false);
            if (problem is not null) return Describe(problem);

            CameraProblem? first = null, last = null;
            foreach (var mode in SharingModes)
            {
                ct.ThrowIfCancellationRequested();
                last = await TryStartAsync(group, mode, ct).ConfigureAwait(false);
                if (last is null) return null;
                // Schon gemeldet (Laufzeitfehler während des Starts) oder aus einem Ereignis heraus angehalten
                if (Volatile.Read(ref _failureClaimed) != 0) return null;
                first ??= last;
                if (!last.RetryShared) break;
                Trace.TraceInformation($"Lupe: Kamera „{_name}“ exklusiv nicht verfügbar ({last.Kind}), versuche geteilten Modus.");
            }
            return Describe(CameraProblems.MoreSpecific(first!, last));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Describe(CameraProblems.FromException(ex));
        }
    }

    private string? Describe(CameraProblem problem)
    {
        // Ist während des Starts schon ein Laufzeitfehler gemeldet worden, nicht ein zweites Mal melden
        if (Interlocked.Exchange(ref _failureClaimed, 1) != 0) return null;
        _active = false;
        return CameraProblems.Describe(problem, _name);
    }

    /// <summary>Kameragruppe zur Kennung; leere Kennung = erste Kamera. Unbekannte Kennung → null (dann als Geräte-Kennung versuchen).</summary>
    private async Task<(MediaFrameSourceGroup? Group, CameraProblem? Problem)> FindGroupAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_deviceId))
        {
            var list = await Cameras.ListAsync(ct).ConfigureAwait(false);
            if (list.Count == 0) return (null, new CameraProblem(CameraProblemKind.NoCamera));
            _deviceId = list[0].Id;
            if (string.IsNullOrWhiteSpace(_camera.Name)) _name = list[0].Name;
        }
        try
        {
            return (await MediaFrameSourceGroup.FromIdAsync(_deviceId).AsTask(ct).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Keine Gruppen-Kennung (oder Gerät weg) – MediaCapture bekommt sie als Geräte-Kennung und meldet Genaueres
            Trace.TraceInformation($"Lupe: Kameragruppe „{_deviceId}“ nicht gefunden (0x{ex.HResult:X8}).");
            return (null, null);
        }
    }

    /// <summary>Ein Startversuch in einem Modus; null = läuft, sonst das Problem (alles Geöffnete ist dann wieder zu).</summary>
    private async Task<CameraProblem?> TryStartAsync(MediaFrameSourceGroup? group, MediaCaptureSharingMode mode, CancellationToken ct)
    {
        var settings = new MediaCaptureInitializationSettings
        {
            StreamingCaptureMode = StreamingCaptureMode.Video,
            MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            SharingMode = mode,
        };
        if (group is not null) settings.SourceGroup = group;
        else settings.VideoDeviceId = _deviceId;

        var capture = new MediaCapture();
        MediaFrameReader? reader = null;
        try
        {
            await capture.InitializeAsync(settings).AsTask(ct).ConfigureAwait(false);

            var source = PickColorSource(capture.FrameSources.Values);
            if (source is null)
            {
                await ReleaseAsync(capture, null).ConfigureAwait(false);
                return new CameraProblem(CameraProblemKind.NoColorStream);
            }

            bool shared = mode == MediaCaptureSharingMode.SharedReadOnly;
            if (!shared) await TrySetPreferredFormatAsync(source, ct).ConfigureAwait(false);

            var current = source.CurrentFormat;
            int width = (int)(current?.VideoFormat?.Width ?? 0), height = (int)(current?.VideoFormat?.Height ?? 0);
            var output = FormatChooser.OutputSize(width, height);
            reader = await CreateReaderAsync(capture, source, (width, height), output, ct).ConfigureAwait(false);
            reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;

            Format = new CaptureFormat(width, height, current?.Subtype ?? "", Fps(current), output.Width, output.Height, shared);
            _capture = capture;
            _reader = reader;
            capture.Failed += _captureFailedHandler;
            capture.CameraStreamStateChanged += _streamStateHandler;
            reader.FrameArrived += _frameArrivedHandler;
            _active = true;

            var status = await reader.StartAsync().AsTask(ct).ConfigureAwait(false);
            if (status == MediaFrameReaderStartStatus.Success) return null;

            _active = false;
            await DetachAndReleaseAsync().ConfigureAwait(false);
            return CameraProblems.FromStartStatus(status);
        }
        catch (Exception ex)
        {
            _active = false;
            if (_capture == capture) await DetachAndReleaseAsync().ConfigureAwait(false);
            else await ReleaseAsync(capture, reader).ConfigureAwait(false);
            if (ex is OperationCanceledException && ct.IsCancellationRequested) throw;
            return CameraProblems.FromException(ex);
        }
    }

    /// <summary>Farbquelle wählen: Vorschau vor Aufnahme; Foto-, Infrarot- und Tiefenströme nie.</summary>
    private static MediaFrameSource? PickColorSource(IEnumerable<MediaFrameSource> sources)
    {
        MediaFrameSource? record = null;
        foreach (var source in sources)
        {
            var info = source.Info;
            if (info.SourceKind != MediaFrameSourceKind.Color) continue;
            if (info.MediaStreamType == MediaStreamType.VideoPreview) return source;
            if (info.MediaStreamType == MediaStreamType.VideoRecord) record ??= source;
        }
        return record;
    }

    private async Task TrySetPreferredFormatAsync(MediaFrameSource source, CancellationToken ct)
    {
        try
        {
            var formats = source.SupportedFormats
                .Where(f => f.VideoFormat is not null && f.VideoFormat.Width > 0 && f.VideoFormat.Height > 0)
                .ToList();
            int best = FormatChooser.Choose(formats.Select(f => new VideoFormatCandidate(
                (int)f.VideoFormat.Width, (int)f.VideoFormat.Height, Fps(f), f.Subtype ?? "")).ToList());
            if (best < 0) return;
            var chosen = formats[best];
            if (SameFormat(chosen, source.CurrentFormat)) return;
            await source.SetFormatAsync(chosen).AsTask(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Dann eben im voreingestellten Format (der Reader skaliert notfalls herunter)
            Trace.TraceWarning($"Lupe: Kameraformat ließ sich nicht setzen: 0x{ex.HResult:X8} {ex.Message}");
        }
    }

    private static async Task<MediaFrameReader> CreateReaderAsync(MediaCapture capture, MediaFrameSource source,
        (int Width, int Height) current, (int Width, int Height) output, CancellationToken ct)
    {
        try
        {
            if (output.Width > 0 && output != current)
            {
                var size = new BitmapSize { Width = (uint)output.Width, Height = (uint)output.Height };
                return await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8, size).AsTask(ct).ConfigureAwait(false);
            }
            return await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8).AsTask(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Ohne Umwandlung durch Media Foundation: Rohformat, FrameConverter wandelt je Bild um
            Trace.TraceWarning($"Lupe: BGRA-Frame-Reader nicht verfügbar (0x{ex.HResult:X8}), nehme das Rohformat.");
            return await capture.CreateFrameReaderAsync(source).AsTask(ct).ConfigureAwait(false);
        }
    }

    private static double Fps(MediaFrameFormat? format)
    {
        var rate = format?.FrameRate;
        return rate is null || rate.Denominator == 0 ? 0 : (double)rate.Numerator / rate.Denominator;
    }

    private static bool SameFormat(MediaFrameFormat a, MediaFrameFormat? b) =>
        b?.VideoFormat is not null
        && a.VideoFormat.Width == b.VideoFormat.Width && a.VideoFormat.Height == b.VideoFormat.Height
        && string.Equals(a.Subtype, b.Subtype, StringComparison.OrdinalIgnoreCase)
        && Math.Abs(Fps(a) - Fps(b)) < 0.01;

    // ---------- laufender Betrieb ----------

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (!_active || !_frameGate.Wait(0)) return; // vorheriges Bild noch in Arbeit → dieses verfällt
        MediaFrameReference? frame = null;
        bool handedOff = false;
        try
        {
            if (!_active) return;
            frame = sender.TryAcquireLatestFrame();
            var video = frame?.VideoMediaFrame;
            if (video is null) return;

            var bitmap = video.SoftwareBitmap;
            if (bitmap is not null)
            {
                PixelImage image;
                using (bitmap) image = FrameConverter.ToPixelImage(bitmap);
                Deliver(image);
            }
            else if (video.Direct3DSurface is { } surface)
            {
                // Bild liegt nur im Grafikspeicher: asynchron kopieren, Bild und Sperre gibt die Aufgabe frei
                handedOff = true;
                var owned = frame!;
                frame = null;
                _ = DeliverFromSurfaceAsync(owned, surface);
            }
        }
        catch (Exception ex)
        {
            ConversionFailed(ex);
        }
        finally
        {
            frame?.Dispose();
            if (!handedOff) _frameGate.Release();
        }
    }

    private async Task DeliverFromSurfaceAsync(MediaFrameReference frame, Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface surface)
    {
        try
        {
            using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(surface, BitmapAlphaMode.Ignore).AsTask().ConfigureAwait(false);
            var image = FrameConverter.ToPixelImage(bitmap);
            if (_active) Deliver(image);
        }
        catch (Exception ex)
        {
            ConversionFailed(ex);
        }
        finally
        {
            frame.Dispose();
            _frameGate.Release();
        }
    }

    private void Deliver(PixelImage image)
    {
        Interlocked.Exchange(ref _conversionErrors, 0);
        if (_active) _onFrame(this, image);
    }

    private void ConversionFailed(Exception ex)
    {
        int count = Interlocked.Increment(ref _conversionErrors);
        Trace.TraceWarning($"Lupe: Kamerabild nicht lesbar ({count}.): {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}");
        if (count >= MaxConsecutiveConversionErrors)
            ReportFailure(new CameraProblem(CameraProblemKind.FormatNotSupported, ex.HResult, ex.Message));
    }

    private void OnCaptureFailed(MediaCapture sender, MediaCaptureFailedEventArgs args)
    {
        int code = unchecked((int)args.Code);
        ReportFailure(new CameraProblem(CameraProblems.Classify(code), code, args.Message));
    }

    private void OnStreamStateChanged(MediaCapture sender, object args)
    {
        CameraStreamState state;
        try { state = sender.CameraStreamState; }
        catch (Exception) { return; }
        if (state == CameraStreamState.BlockedForPrivacy) ReportFailure(new CameraProblem(CameraProblemKind.AccessDenied));
        else if (state == CameraStreamState.Shutdown) ReportFailure(new CameraProblem(CameraProblemKind.StreamShutdown));
    }

    /// <summary>Laufzeitfehler genau einmal melden; danach liefert die Sitzung nichts mehr.</summary>
    private void ReportFailure(CameraProblem problem)
    {
        if (!_active) return;
        if (Interlocked.Exchange(ref _failureClaimed, 1) != 0) return;
        _active = false;
        Trace.TraceWarning($"Lupe: Kamera „{_name}“ ausgefallen: {problem}");
        string message;
        try { message = CameraProblems.Describe(problem, _name); }
        catch (Exception) { message = $"Die Kamera „{_name}“ ist ausgefallen. Starten Sie sie erneut."; }
        _onFailure(this, message);
    }

    // ---------- Freigabe ----------

    /// <summary>Gibt Reader und MediaCapture frei; mehrfach und nebenläufig aufrufbar, nie aus einem eigenen Ereignis.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_disposeLock) _disposeTask ??= DisposeCoreAsync();
        return new ValueTask(_disposeTask);
    }

    private async Task DisposeCoreAsync()
    {
        Deactivate();
        try
        {
            await DetachAndReleaseAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Lupe: Freigeben der Kamera „{_name}“: {ex}");
        }
    }

    /// <summary>Ereignisse abmelden, laufendes Bild abwarten, dann Reader und MediaCapture freigeben.</summary>
    private async Task DetachAndReleaseAsync()
    {
        var capture = Interlocked.Exchange(ref _capture, null);
        var reader = Interlocked.Exchange(ref _reader, null);
        if (capture is null && reader is null) return;
        if (capture is not null)
        {
            try
            {
                capture.Failed -= _captureFailedHandler;
                capture.CameraStreamStateChanged -= _streamStateHandler;
            }
            catch (Exception ex) { Trace.TraceWarning($"Lupe: Kamera-Ereignisse abmelden: 0x{ex.HResult:X8} {ex.Message}"); }
        }
        if (reader is not null)
        {
            try { reader.FrameArrived -= _frameArrivedHandler; }
            catch (Exception ex) { Trace.TraceWarning($"Lupe: Bild-Ereignis abmelden: 0x{ex.HResult:X8} {ex.Message}"); }
        }

        bool drained = await _frameGate.WaitAsync(FrameDrainTimeout).ConfigureAwait(false);
        if (!drained) Trace.TraceWarning("Lupe: Ein Empfänger von Kamerabildern hängt – gebe die Kamera trotzdem frei.");
        try
        {
            await ReleaseAsync(capture, reader).ConfigureAwait(false);
        }
        finally
        {
            if (drained) _frameGate.Release();
        }
    }

    private static async Task ReleaseAsync(MediaCapture? capture, MediaFrameReader? reader)
    {
        if (reader is not null)
        {
            try { await reader.StopAsync().AsTask().ConfigureAwait(false); }
            catch (Exception ex) { Trace.TraceWarning($"Lupe: Frame-Reader anhalten: 0x{ex.HResult:X8} {ex.Message}"); }
            try { reader.Dispose(); }
            catch (Exception ex) { Trace.TraceWarning($"Lupe: Frame-Reader freigeben: 0x{ex.HResult:X8} {ex.Message}"); }
        }
        if (capture is not null)
        {
            try { capture.Dispose(); }
            catch (Exception ex) { Trace.TraceWarning($"Lupe: MediaCapture freigeben: 0x{ex.HResult:X8} {ex.Message}"); }
        }
    }
}
