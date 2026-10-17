using System.Collections.ObjectModel;
using Avalonia.Threading;
using Lupe.Core.Decoding;
using Lupe.Core.History;
using Lupe.Core.Model;
using Lupe.Core.Settings;
using Lupe.Services;

namespace Lupe;

public enum NoticeKind { Info, Success, Warning, Error }

/// <summary>
/// Gemeinsamer Zustand aller Seiten und Fenster: Einstellungen, Verlauf, Treffer-Pipeline, Tastenkürzel-Status.
/// Alle Methoden werden vom Oberflächen-Thread aufgerufen.
/// </summary>
internal sealed class AppState : IDisposable
{
    /// <summary>So oft wird eine beim Start gesperrte Einstellungsdatei erneut gelesen (alle 5 s, also gut zwei Minuten lang).</summary>
    private const int SettingsRetries = 25;

    /// <summary>Die Einstellungsdatei; null in Demo und Screenshots (dort wird nichts gespeichert).</summary>
    private readonly SettingsFile? _settingsFile;
    private HistoryStore _history;
    private string? _reportedHistoryProblem;
    private DispatcherTimer? _settingsRetry;
    /// <summary>
    /// Die Einstellungsdatei war beim Start nicht lesbar: Der Verlauf steht nur im Speicher, bis feststeht, ob er gespeichert
    /// werden darf (<see cref="SettleHistory"/>).
    /// </summary>
    private bool _historyWaitsForSettings;

    private AppState(LupeSettings settings, SettingsFile? settingsFile, HistoryStore history, bool isDemo, bool strict)
    {
        Settings = settings;
        _settingsFile = settingsFile;
        _history = history;
        _history.Changed += OnHistoryChanged;
        _reportedHistoryProblem = history.Problem;
        IsDemo = isDemo;
        Strict = strict;
        Hits = new Hits(this);
        // Die Knöpfe der Ergebniszeilen (Kopieren, Link öffnen) arbeiten mit dem Zustand dieser Oberfläche
        UI.CodeCommands.State = this;
    }

    /// <summary>Demo oder Screenshots: nichts wird gespeichert, weder Einstellungen noch Verlauf noch Autostart.</summary>
    public bool IsDemo { get; }

    /// <summary>
    /// Screenshot-Modus: Fehler nicht als Meldung abfangen, sondern durchreichen, damit ein unvollständiges Bild nie
    /// unbemerkt entsteht. Keine Töne, keine Zwischenablage.
    /// </summary>
    public bool Strict { get; }

    public LupeSettings Settings { get; private set; }
    public HistoryStore History => _history;
    public Hits Hits { get; }

    /// <summary>Problem beim Start (z. B. Einstellungen nicht lesbar), wird nach dem Öffnen des Fensters gemeldet.</summary>
    public string? StartupProblem { get; private set; }

    /// <summary>
    /// Was mit der Einstellungsdatei gerade nicht stimmt (kaputt, gesperrt, Änderung nicht gespeichert) – für den Hinweis auf
    /// der Seite Einstellungen; null, sobald wieder gespeichert werden konnte.
    /// </summary>
    public string? SettingsProblem
    {
        get => _settingsProblem;
        private set
        {
            if (value == _settingsProblem) return;
            _settingsProblem = value;
            SettingsProblemChanged?.Invoke();
        }
    }

    private string? _settingsProblem;

    /// <summary><see cref="SettingsProblem"/> hat sich geändert.</summary>
    public event Action? SettingsProblemChanged;

    /// <summary>Die letzten Treffer aus Bildschirm-Scans (neueste zuerst), für die Seite „Bildschirm“.</summary>
    public ObservableCollection<CodeItem> ScreenResults { get; } = [];

    // ---------- Tastenkürzel (meldet das App-Objekt) ----------
    public bool HotkeyActive { get; private set; }
    public string? HotkeyError { get; private set; }
    public event Action? HotkeyChanged;

    /// <summary>
    /// In der Demo wird das globale Tastenkürzel nicht angemeldet – sonst wäre es neben einer echten Lupe „belegt“.
    /// (Die Screenshots zeigen es trotzdem als bereit, wie im echten Betrieb.)
    /// </summary>
    public bool HotkeyOffInDemo => IsDemo && !Strict;

    /// <summary>Das globale Tastenkürzel ist gerade abgemeldet, weil in den Einstellungen ein neues aufgenommen wird.</summary>
    public bool HotkeySuspended { get; private set; }

    /// <summary>Tastenkürzel vorübergehend ab- (true) oder wieder anmelden (false) – erledigt das App-Objekt.</summary>
    public event Action<bool>? HotkeySuspendChanged;

    public void SetHotkeyStatus(bool active, string? error)
    {
        HotkeyActive = active;
        HotkeyError = error;
        HotkeyChanged?.Invoke();
    }

    /// <summary>
    /// Während ein neues Tastenkürzel aufgenommen wird, darf das alte nicht angemeldet sein: Windows gäbe die Tasten sonst
    /// gar nicht an das Eingabefeld weiter, sondern startete einen Bildschirm-Scan.
    /// </summary>
    public void SuspendHotkey(bool suspend)
    {
        if (HotkeySuspended == suspend) return;
        HotkeySuspended = suspend;
        HotkeySuspendChanged?.Invoke(suspend);
    }

    /// <summary>Einstellungen geändert (alt, neu).</summary>
    public event Action<LupeSettings, LupeSettings>? SettingsChanged;
    /// <summary>Verlauf geändert (Eintrag dazu, gelöscht, neuer Speicher) – auf dem Oberflächen-Thread.</summary>
    public event Action? HistoryChanged;
    public event Action<NoticeKind, string>? Notice;
    public event Action<string>? PageRequested;
    /// <summary>Fenster zeigen (auch aus dem Infobereich) und zur Seite wechseln.</summary>
    public event Action<string>? WindowRequested;
    public event Action? ScreenScanRequested;

    // ================= Öffnen =================

    /// <summary>Normaler Start mit den Dateien im Lupe-Ordner des Benutzers (Windows: %LocalAppData%\Lupe).</summary>
    public static AppState Open()
    {
        var problems = new List<string>();
        SettingsFile? file = null;
        LupeSettings settings;
        try
        {
            file = new SettingsFile();
            settings = file.Current;
            if (file.LoadProblem is { } loadProblem) problems.Add(loadProblem);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            settings = new LupeSettings();
            problems.Add($"Die Einstellungen ließen sich nicht lesen ({ex.Message}). Es gelten die Standardwerte; gespeichert wird in dieser Sitzung nicht.");
        }

        // Ob der Verlauf auf die Platte darf, steht in den Einstellungen. Sind sie nicht lesbar, ist das unbekannt – dann
        // bleibt er im Speicher, bis sie gelesen werden konnten (SettleHistory). Sonst legte Lupe womöglich eine verlauf.json
        // mit gescannten Inhalten an, obwohl das Speichern ausgeschaltet ist. Liegt schon eine da, war es eingeschaltet
        // (beim Ausschalten wird sie gelöscht) – dann geht es mit ihr weiter.
        bool settingsUnknown = file is null or { State: SettingsFileState.Unreadable };
        bool historyWaits = settingsUnknown && settings.KeepHistory && !File.Exists(HistoryStore.DefaultPath);
        if (historyWaits) problems.Add(file is null ? "Der Verlauf bleibt in dieser Sitzung nur im Speicher." : "Der Verlauf bleibt bis dahin nur im Speicher.");

        // „Verlauf speichern“ ist aus, aber die Datei liegt noch da (beim Ausschalten gesperrt): Löschen jetzt nachholen
        if (file is { State: SettingsFileState.Loaded or SettingsFileState.Repaired } && !settings.KeepHistory
            && !HistoryStore.TryDeleteFile(null, out _, out var deleteError))
            problems.Add($"{deleteError} „Verlauf speichern“ ist aus – bitte die Datei von Hand löschen, sie kann vertrauliche Inhalte enthalten.");

        HistoryStore history;
        try
        {
            history = new HistoryStore(null, settings.HistoryLimit, settings.KeepHistory && !historyWaits);
            if (history.Problem is { } historyProblem) problems.Add(historyProblem);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            history = new HistoryStore(null, settings.HistoryLimit, persist: false);
            problems.Add($"Der Verlauf ließ sich nicht öffnen ({ex.Message}). Er wird in dieser Sitzung nicht gespeichert.");
        }

        var state = new AppState(settings, file, history, isDemo: false, strict: false)
        {
            StartupProblem = problems.Count == 0 ? null : string.Join(" ", problems),
            SettingsProblem = file is null ? problems.FirstOrDefault() : file.Problem,
            _historyWaitsForSettings = historyWaits && file is not null,
        };
        state.MoveLegacyPreferences();
        if (file?.State == SettingsFileState.Unreadable) state.StartSettingsRetry();
        return state;
    }

    /// <summary>Demo und Screenshots: Beispielverlauf im Speicher, Standard-Einstellungen mit zwei Link-Regeln.</summary>
    public static AppState OpenDemo(bool strict, DateTimeOffset now)
    {
        var settings = Demo.DemoData.Settings();
        var history = new HistoryStore(null, settings.HistoryLimit, persist: false);
        foreach (var entry in Demo.DemoData.History(now).Reverse()) history.Add(entry);
        var state = new AppState(settings, null, history, isDemo: true, strict);
        foreach (var item in Demo.DemoData.ScreenResults(now, state.LinkFor, history.Entries)) state.ScreenResults.Add(item);
        return state;
    }

    /// <summary>
    /// „Im Infobereich weiterlaufen“ aus der alten oberflaeche.json in die Einstellungen holen, danach die alte Datei löschen.
    /// Nur bei einer heilen (oder noch fehlenden) Einstellungsdatei – eine kaputte oder gesperrte wird beim Start nicht
    /// angefasst; die alte Datei wartet dann bis zum nächsten Start.
    /// </summary>
    private void MoveLegacyPreferences()
    {
        if (_settingsFile is not { State: SettingsFileState.Loaded or SettingsFileState.Missing } || !LegacyPreferences.Exists()) return;
        var migrated = LegacyPreferences.Migrate(Settings);
        if (SettingsStore.SameValues(migrated, Settings))
        {
            LegacyPreferences.Delete();
            return;
        }
        var result = _settingsFile.Save(migrated);
        Settings = result.Settings;
        SettingsProblem = _settingsFile.Problem;
        if (!result.IsProblem) LegacyPreferences.Delete();
    }

    // ================= Einstellungen =================

    /// <summary>
    /// Einstellungen ändern und sofort speichern (außer in der Demo). Konnte die Datei beim Start nicht gelesen werden,
    /// sorgt <see cref="SettingsFile"/> dafür, dass sie nicht mit Standardwerten überschrieben wird (Sicherung bzw.
    /// Zusammenführen) – der Hinweis dazu erscheint als Meldung.
    /// </summary>
    public void UpdateSettings(Func<LupeSettings, LupeSettings> change)
    {
        var old = Settings;
        var next = change(old);
        if (ReferenceEquals(next, old) || SettingsStore.SameValues(next, old)) return;
        if (_settingsFile is not null)
        {
            SettingsSaveResult result;
            try { result = _settingsFile.Save(next); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                result = new SettingsSaveResult(SettingsSaveStatus.Failed, next, $"Die Einstellungen ließen sich nicht speichern: {ex.Message}");
            }
            next = result.Settings;
            SettingsProblem = _settingsFile.Problem;
            if (result.Message is { } message)
                Report(result.Status switch
                {
                    SettingsSaveStatus.Failed => NoticeKind.Error,
                    SettingsSaveStatus.Deferred => NoticeKind.Warning,
                    _ => NoticeKind.Info,
                }, message);
            // Noch immer gesperrt: weiter im Hintergrund versuchen, damit die Änderung nicht erst mit der nächsten gespeichert wird
            if (result.Status != SettingsSaveStatus.Deferred) StopSettingsRetry();
            else if (_settingsRetry is null) StartSettingsRetry();
        }
        Apply(old, next);
    }

    private void Apply(LupeSettings old, LupeSettings next)
    {
        Settings = next;
        if (old.KeepHistory != next.KeepHistory || old.HistoryLimit != next.HistoryLimit) ReplaceHistory(old, next);
        SettleHistory();
        SettingsChanged?.Invoke(old, next);
    }

    /// <summary>
    /// Die beim Start nicht lesbare Einstellungsdatei ist jetzt gelesen bzw. geschrieben: Ist „Verlauf speichern“ an, kommt
    /// der Verlauf, der bis dahin nur im Speicher stand, jetzt in die Datei.
    /// </summary>
    private void SettleHistory()
    {
        if (!_historyWaitsForSettings || _settingsFile is null or { State: SettingsFileState.Unreadable }) return;
        _historyWaitsForSettings = false;
        if (Settings.KeepHistory && !_history.Persist) ReplaceHistory(Settings, Settings);
    }

    /// <summary>Beim Start gesperrte Einstellungsdatei: eine Weile im Hintergrund erneut lesen, statt die ganze Sitzung mit Standardwerten zu laufen.</summary>
    private void StartSettingsRetry()
    {
        int attempts = 0;
        _settingsRetry = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) =>
        {
            if (++attempts >= SettingsRetries) StopSettingsRetry();
            if (_settingsFile is null) return;
            SettingsSaveResult? result;
            try { result = _settingsFile.Reload(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { return; }
            if (_settingsFile.State == SettingsFileState.Unreadable) return; // noch gesperrt
            StopSettingsRetry();
            SettingsProblem = _settingsFile.Problem;
            // Ohne Ergebnis: jetzt lesbar, aber kaputt und nichts zu speichern – dann den Hinweis dazu zeigen
            if ((result is null ? _settingsFile.Problem : result.Message) is { } message)
                Report(result is null || result.IsProblem ? NoticeKind.Warning : NoticeKind.Info, message);
            if (result is not null && !SettingsStore.SameValues(result.Settings, Settings)) Apply(Settings, result.Settings);
            else SettleHistory();
        });
        _settingsRetry.Start();
    }

    private void StopSettingsRetry()
    {
        _settingsRetry?.Stop();
        _settingsRetry = null;
    }

    /// <summary>
    /// Verlauf mit neuem Limit oder neuer Speicherart. Ausgeschaltet: Die Datei wird gelöscht (die Rückfrage stellt die
    /// Einstellungsseite), die Einträge bleiben bis zum Beenden im Speicher. Eingeschaltet: Die Einträge dieser Sitzung
    /// kommen in die Datei.
    /// </summary>
    private void ReplaceHistory(LupeSettings old, LupeSettings next)
    {
        // Ob bisher wirklich gespeichert wurde, sagt der Verlauf selbst (eingeschaltet, aber nur im Speicher: Datei war nicht
        // zu öffnen oder die Einstellungen beim Start nicht lesbar) – dann kommen seine Einträge mit in die Datei
        bool wasPersisting = _history.Persist;
        bool oldPersist = (old.KeepHistory && !IsDemo) || wasPersisting, newPersist = next.KeepHistory && !IsDemo;
        var entries = _history.Entries.ToList();
        HistoryStore created;
        try { created = new HistoryStore(null, next.HistoryLimit, newPersist); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Report(NoticeKind.Error, $"Der Verlauf ließ sich nicht öffnen ({ex.Message}). Er wird nicht gespeichert.");
            created = new HistoryStore(null, next.HistoryLimit, persist: false);
            newPersist = false;
        }

        if (oldPersist && !next.KeepHistory)
        {
            if (!HistoryStore.TryDeleteFile(null, out bool deleted, out var error))
                Report(NoticeKind.Warning, $"{error} Bitte die Datei von Hand löschen – sie kann vertrauliche Inhalte enthalten.");
            else if (deleted)
                Report(NoticeKind.Success, "Der Verlauf wird nicht mehr gespeichert – die Datei verlauf.json ist gelöscht. Die Einträge hier bleiben bis zum Beenden.");
        }
        // Was nur im Speicher stand, übernehmen (älteste zuerst, damit die Reihenfolge bleibt)
        if (!newPersist || !wasPersisting)
            foreach (var entry in Enumerable.Reverse(entries)) created.Add(entry);

        _history.Changed -= OnHistoryChanged;
        _history = created;
        _history.Changed += OnHistoryChanged;
        _reportedHistoryProblem = created.Problem;
        if (created.Problem is { } problem) Report(NoticeKind.Warning, problem);
        HistoryChanged?.Invoke();
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        // Der Verlauf meldet sich auf dem Thread, der ihn geändert hat
        if (Dispatcher.UIThread.CheckAccess()) OnHistoryChangedUi();
        else Dispatcher.UIThread.Post(OnHistoryChangedUi);
    }

    private void OnHistoryChangedUi()
    {
        // Neues Problem beim Speichern (z. B. Platte voll, Datei gesperrt) einmal melden; erledigt es sich, wieder bereit
        var problem = _history.Problem;
        if (problem != _reportedHistoryProblem)
        {
            _reportedHistoryProblem = problem;
            if (problem is not null) Report(NoticeKind.Warning, problem);
        }
        HistoryChanged?.Invoke();
    }

    // ================= Erkennung und Links =================

    /// <summary>Neuer Decoder mit den erlaubten Formaten (je Aufgabe einer – Kamera, Datei und Bildschirm laufen nebeneinander).</summary>
    public BarcodeDecoder CreateDecoder() => new(FormatFilter.Usable(Settings.Formats, FormatFilter.Known()));

    /// <summary>
    /// Decoder für den Kamerastrom: nur die schnelle Stufe und wenige Kerne – im Dauerbetrieb bliebe der Rechner sonst
    /// unter Last, obwohl das nächste Bild ohnehin eine neue Chance ist.
    /// </summary>
    public LiveBarcodeDecoder CreateLiveDecoder() => new(FormatFilter.Usable(Settings.Formats, FormatFilter.Known()));

    /// <summary>Link zum Code laut Link-Regeln (nur http/https), sonst null.</summary>
    public Uri? LinkFor(string text)
    {
        try { return LinkRules.Match(Settings.LinkRules, text); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    // ================= Zwischen Seiten und Fenstern =================

    public void Report(NoticeKind kind, string text) => Notice?.Invoke(kind, text);
    public void RequestPage(string slug) => PageRequested?.Invoke(slug);
    public void RequestWindow(string slug) => WindowRequested?.Invoke(slug);
    public void RequestScreenScan() => ScreenScanRequested?.Invoke();

    public void Dispose()
    {
        StopSettingsRetry();
        // Einstellungsdatei war die ganze Zeit gesperrt: Änderungen dieser Sitzung ein letztes Mal zu speichern versuchen
        // (zusammengeführt mit dem Inhalt der Datei, siehe SettingsFile.Reload)
        if (_settingsFile is { State: SettingsFileState.Unreadable })
        {
            try { _settingsFile.Reload(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { /* beim Beenden nichts mehr melden */ }
        }
        _history.Changed -= OnHistoryChanged;
    }
}
