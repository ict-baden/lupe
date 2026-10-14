using System.Collections.ObjectModel;
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
    private HistoryStore _history;

    private AppState(LupeSettings settings, HistoryStore history, AppPreferences preferences, bool isDemo, bool strict)
    {
        Settings = settings;
        _history = history;
        _history.Changed += OnHistoryChanged;
        Preferences = preferences;
        IsDemo = isDemo;
        Strict = strict;
        Hits = new Hits(this);
    }

    /// <summary>Demo oder Screenshots: nichts wird gespeichert, weder Einstellungen noch Verlauf noch Autostart.</summary>
    public bool IsDemo { get; }

    /// <summary>
    /// Screenshot-Modus: Fehler nicht als Meldung abfangen, sondern durchreichen, damit ein unvollständiges Bild nie
    /// unbemerkt entsteht. Keine Töne, keine Zwischenablage.
    /// </summary>
    public bool Strict { get; }

    public LupeSettings Settings { get; private set; }
    public AppPreferences Preferences { get; private set; }
    public HistoryStore History => _history;
    public Hits Hits { get; }

    /// <summary>Problem beim Start (z. B. Einstellungen nicht lesbar), wird nach dem Öffnen des Fensters gemeldet.</summary>
    public string? StartupProblem { get; private set; }

    /// <summary>Die letzten Treffer aus Bildschirm-Scans (neueste zuerst), für die Seite „Bildschirm“.</summary>
    public ObservableCollection<CodeItem> ScreenResults { get; } = [];

    // ---------- Tastenkürzel (meldet das App-Objekt) ----------
    public bool HotkeyActive { get; private set; }
    public string? HotkeyError { get; private set; }
    public event Action? HotkeyChanged;

    public void SetHotkeyStatus(bool active, string? error)
    {
        HotkeyActive = active;
        HotkeyError = error;
        HotkeyChanged?.Invoke();
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

    /// <summary>Normaler Start mit den Dateien unter %LocalAppData%\Lupe.</summary>
    public static AppState Open()
    {
        string? problem = null;
        LupeSettings settings;
        try { settings = SettingsStore.Load(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            settings = new LupeSettings();
            problem = $"Die Einstellungen ließen sich nicht lesen ({ex.Message}). Es gelten die Standardwerte.";
        }

        HistoryStore history;
        try { history = new HistoryStore(null, settings.HistoryLimit, settings.KeepHistory); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            history = new HistoryStore(null, settings.HistoryLimit, persist: false);
            problem = $"Der Verlauf ließ sich nicht öffnen ({ex.Message}). Er wird in dieser Sitzung nicht gespeichert.";
        }
        return new AppState(settings, history, AppPreferences.Load(), isDemo: false, strict: false) { StartupProblem = problem };
    }

    /// <summary>Demo und Screenshots: Beispielverlauf im Speicher, Standard-Einstellungen mit zwei Link-Regeln.</summary>
    public static AppState OpenDemo(bool strict, DateTimeOffset now)
    {
        var settings = Demo.DemoData.Settings();
        var history = new HistoryStore(null, settings.HistoryLimit, persist: false);
        foreach (var entry in Demo.DemoData.History(now).Reverse()) history.Add(entry);
        var state = new AppState(settings, history, new AppPreferences(), isDemo: true, strict);
        foreach (var item in Demo.DemoData.ScreenResults(now, state.LinkFor)) state.ScreenResults.Add(item);
        return state;
    }

    // ================= Einstellungen =================

    /// <summary>Einstellungen ändern und sofort speichern (außer in der Demo).</summary>
    public void UpdateSettings(Func<LupeSettings, LupeSettings> change)
    {
        var old = Settings;
        var next = change(old);
        if (next == old) return;
        Settings = next;
        if (!IsDemo)
        {
            try { SettingsStore.Save(next); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Report(NoticeKind.Error, $"Die Einstellungen ließen sich nicht speichern: {ex.Message}");
            }
        }
        if (old.KeepHistory != next.KeepHistory || old.HistoryLimit != next.HistoryLimit) ReplaceHistory(old, next);
        SettingsChanged?.Invoke(old, next);
    }

    public void UpdatePreferences(Func<AppPreferences, AppPreferences> change)
    {
        var next = change(Preferences);
        if (next == Preferences) return;
        Preferences = next;
        if (!IsDemo && !next.Save()) Report(NoticeKind.Warning, "Die Einstellung ließ sich nicht speichern (oberflaeche.json).");
    }

    /// <summary>
    /// Verlauf mit neuem Limit oder neuer Speicherart. Ausgeschaltet: Die Datei wird gelöscht (nichts mehr auf der
    /// Platte), die Einträge bleiben bis zum Beenden im Speicher. Eingeschaltet: Die Einträge dieser Sitzung kommen
    /// in die Datei.
    /// </summary>
    private void ReplaceHistory(LupeSettings old, LupeSettings next)
    {
        bool oldPersist = old.KeepHistory && !IsDemo, newPersist = next.KeepHistory && !IsDemo;
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
            try
            {
                if (File.Exists(HistoryStore.DefaultPath)) File.Delete(HistoryStore.DefaultPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Report(NoticeKind.Warning, $"Die Verlaufsdatei ließ sich nicht löschen: {ex.Message}");
            }
        }
        // Was nur im Speicher stand, übernehmen (älteste zuerst, damit die Reihenfolge bleibt)
        if (!newPersist || !oldPersist)
            foreach (var entry in Enumerable.Reverse(entries)) created.Add(entry);

        _history.Changed -= OnHistoryChanged;
        _history = created;
        _history.Changed += OnHistoryChanged;
        HistoryChanged?.Invoke();
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        // Der Verlauf meldet sich auf dem Thread, der ihn geändert hat
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) HistoryChanged?.Invoke();
        else dispatcher.BeginInvoke(() => HistoryChanged?.Invoke());
    }

    // ================= Erkennung und Links =================

    /// <summary>Neuer Decoder mit den erlaubten Formaten (je Aufgabe einer – Kamera, Datei und Bildschirm laufen nebeneinander).</summary>
    public BarcodeDecoder CreateDecoder() => new(FormatFilter.Usable(Settings.Formats, FormatFilter.Known()));

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

    public void Dispose() => _history.Changed -= OnHistoryChanged;
}
