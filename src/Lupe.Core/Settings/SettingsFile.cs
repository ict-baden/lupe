// SPDX-License-Identifier: MIT
using System.Globalization;
using Lupe.Core.Model;
using Lupe.Core.Resources;

namespace Lupe.Core.Settings;

/// <summary>Wie die Einstellungsdatei beim Laden vorgefunden wurde.</summary>
public enum SettingsFileState
{
    /// <summary>Keine Datei (oder eine leere) – es gelten die Standardwerte.</summary>
    Missing,
    /// <summary>Vollständig gelesen.</summary>
    Loaded,
    /// <summary>Gelesen, aber einzelne Werte waren unbrauchbar und gelten mit ihrem Standardwert.</summary>
    Repaired,
    /// <summary>Inhalt unbrauchbar (kein JSON, falscher Aufbau) – es gelten die Standardwerte.</summary>
    Broken,
    /// <summary>Nicht lesbar (gesperrt, keine Berechtigung) – der Inhalt ist unbekannt.</summary>
    Unreadable,
}

/// <summary>Was <see cref="SettingsFile.Save"/> getan hat.</summary>
public enum SettingsSaveStatus
{
    /// <summary>Dieselben Werte wie bisher – nichts geschrieben.</summary>
    Unchanged,
    /// <summary>Geschrieben (bei einer kaputten Datei nach dem Sichern, siehe <see cref="SettingsSaveResult.Message"/>).</summary>
    Saved,
    /// <summary>
    /// Die beim Start nicht lesbare Datei ließ sich jetzt lesen: Die Änderungen dieser Sitzung wurden auf ihren Inhalt
    /// gelegt und geschrieben. <see cref="SettingsSaveResult.Settings"/> kann deshalb von den übergebenen Werten abweichen.
    /// </summary>
    Merged,
    /// <summary>Die Datei ist noch immer nicht lesbar – nichts geschrieben, die Werte gelten bis zum Beenden.</summary>
    Deferred,
    /// <summary>Schreiben (oder Sichern der kaputten Datei) ist gescheitert – die Werte gelten bis zum Beenden.</summary>
    Failed,
}

/// <param name="Status">Was passiert ist.</param>
/// <param name="Settings">Die Einstellungen, die jetzt gelten (bei <see cref="SettingsSaveStatus.Merged"/> zusammengeführt).</param>
/// <param name="Message">Deutscher Hinweis für die Oberfläche (Sicherung angelegt, nicht gespeichert …), sonst null.</param>
public sealed record SettingsSaveResult(SettingsSaveStatus Status, LupeSettings Settings, string? Message)
{
    /// <summary>Die Änderung steht nicht in der Datei (<see cref="SettingsSaveStatus.Deferred"/> oder <see cref="SettingsSaveStatus.Failed"/>).</summary>
    public bool IsProblem => Status is SettingsSaveStatus.Deferred or SettingsSaveStatus.Failed;
}

/// <summary>
/// Die Einstellungsdatei einer laufenden App: einmal laden, jede echte Änderung speichern – ohne je eine Datei, die beim
/// Start nicht gelesen werden konnte, stillschweigend mit Standardwerten zu überschreiben.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Kaputt</b> (<see cref="SettingsFileState.Broken"/>) oder <b>teilweise unbrauchbar</b>
/// (<see cref="SettingsFileState.Repaired"/>): Vor dem ersten Speichern wird die Datei unverändert als
/// <c>einstellungen.json.defekt-JJJJMMTT-hhmmss</c> daneben gesichert. Klappt das Sichern nicht, wird nicht gespeichert.</item>
/// <item><b>Nicht lesbar</b> (<see cref="SettingsFileState.Unreadable"/>, z. B. von einem anderen Programm gesperrt):
/// Gespeichert wird erst, wenn sie wieder lesbar ist. Dann kommen die Änderungen dieser Sitzung – gegenüber dem Stand
/// beim Start – feldweise auf den Inhalt der Datei; was dort stand und nicht geändert wurde, bleibt.
/// <see cref="Reload"/> versucht das auch ohne Änderung (z. B. per Zeitgeber).</item>
/// <item>Gespeichert wird nur bei einer echten Änderung; dieselben Werte noch einmal schreiben nichts.</item>
/// </list>
/// <para>Nicht threadsicher – gedacht für den Oberflächen-Thread. Wirft nie wegen der Datei.</para>
/// </remarks>
public sealed class SettingsFile
{
    /// <summary>Höchstens so viele Sicherungen mit derselben Sekunde im Namen (-2, -3 …).</summary>
    private const int MaxBackupNames = 50;

    private readonly Func<DateTime> _clock;
    /// <summary>Stand, auf den sich die Änderungen dieser Sitzung beziehen (bei nicht lesbarer Datei: die Standardwerte).</summary>
    private LupeSettings _baseline;

    /// <summary>Lädt die Datei (null = <see cref="SettingsStore.DefaultPath"/>).</summary>
    public SettingsFile(string? path = null) : this(path, () => DateTime.Now) { }

    internal SettingsFile(string? path, Func<DateTime> clock)
    {
        _clock = clock;
        FilePath = SettingsStore.ResolvePath(path);
        Current = SettingsStore.Load(FilePath, out var problem, out var state);
        _baseline = Current;
        State = state;
        LoadProblem = Describe(problem, state);
        Problem = LoadProblem;
    }

    /// <summary>Hinweis vom Laden samt dem, was mit der Datei passieren wird – sie wird nie stillschweigend überschrieben.</summary>
    private string? Describe(string? problem, SettingsFileState state) => problem is null ? null : state switch
    {
        SettingsFileState.Broken or SettingsFileState.Repaired =>
            string.Format(Strings.Core_Settings_BackupBeforeSave, problem, Path.GetFileName(FilePath)),
        SettingsFileState.Unreadable =>
            string.Format(Strings.Core_Settings_SaveWhenReadable, problem),
        _ => problem,
    };

    /// <summary>Vollständiger Pfad der Datei.</summary>
    public string FilePath { get; }

    /// <summary>Die geltenden Einstellungen.</summary>
    public LupeSettings Current { get; private set; }

    /// <summary>Zustand der Datei; nach erfolgreichem Speichern <see cref="SettingsFileState.Loaded"/>.</summary>
    public SettingsFileState State { get; private set; }

    /// <summary>Hinweis vom Laden (deutsch, für die Oberfläche) oder null.</summary>
    public string? LoadProblem { get; }

    /// <summary>
    /// Was mit der Datei gerade nicht stimmt (kaputt, gesperrt, letzte Änderung nicht gespeichert) – deutsch, für einen
    /// bleibenden Hinweis in der Oberfläche; null, sobald sie gelesen bzw. gespeichert werden konnte.
    /// </summary>
    public string? Problem { get; private set; }

    /// <summary>Die zuletzt angelegte Sicherung einer kaputten Datei, sonst null.</summary>
    public string? BackupPath { get; private set; }

    /// <summary>Speichert <paramref name="settings"/>, wenn sie sich von <see cref="Current"/> unterscheiden (siehe Klassenbeschreibung).</summary>
    public SettingsSaveResult Save(LupeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var next = SettingsStore.Normalize(settings);
        if (SettingsStore.SameValues(next, Current)) return new(SettingsSaveStatus.Unchanged, Current, null);

        if (State == SettingsFileState.Unreadable)
        {
            var file = SettingsStore.Load(FilePath, out var problem, out var state);
            if (state == SettingsFileState.Unreadable)
            {
                Current = next;
                Problem = problem is null
                    ? string.Format(Strings.Core_Settings_StillUnreadable, FilePath)
                    : string.Format(Strings.Core_Settings_StillUnreadableReason, FilePath, problem);
                return new(SettingsSaveStatus.Deferred, Current, Problem);
            }
            State = state;
            if (state is SettingsFileState.Loaded or SettingsFileState.Repaired)
            {
                // Was in der Datei steht, bleibt – nur was in dieser Sitzung geändert wurde, kommt darüber
                var merged = SettingsStore.ApplyChanges(file, _baseline, next);
                return Write(merged, SettingsSaveStatus.Merged, Strings.Core_Settings_ReadableAgainMerged);
            }
        }
        return Write(next, SettingsSaveStatus.Saved, null);
    }

    /// <summary>
    /// Nur bei <see cref="SettingsFileState.Unreadable"/>: liest die Datei erneut. Ist sie jetzt lesbar, gelten ihre Werte –
    /// samt den Änderungen dieser Sitzung, die dann auch gespeichert werden. null, wenn sich nichts geändert hat (Datei noch
    /// gesperrt oder gar nicht nötig).
    /// </summary>
    public SettingsSaveResult? Reload()
    {
        if (State != SettingsFileState.Unreadable) return null;
        var file = SettingsStore.Load(FilePath, out var problem, out var state);
        if (state == SettingsFileState.Unreadable) return null;

        State = state;
        Problem = Describe(problem, state);
        var pending = !SettingsStore.SameValues(Current, _baseline);
        if (state is SettingsFileState.Broken)
        {
            // Nichts Brauchbares in der Datei: Es bleibt bei den geltenden Werten, gesichert wird beim nächsten Speichern
            _baseline = new LupeSettings();
            if (!pending) return null;
            return Write(Current, SettingsSaveStatus.Saved, null);
        }

        var merged = SettingsStore.ApplyChanges(file, _baseline, Current);
        if (!pending)
        {
            Current = file;
            _baseline = file;
            return new(SettingsSaveStatus.Unchanged, Current,
                state == SettingsFileState.Missing ? null : Strings.Core_Settings_ReadableAgain);
        }
        if (state == SettingsFileState.Missing) return Write(Current, SettingsSaveStatus.Saved, null);
        return Write(merged, SettingsSaveStatus.Merged, Strings.Core_Settings_ReadableAgainMerged);
    }

    private SettingsSaveResult Write(LupeSettings settings, SettingsSaveStatus success, string? message)
    {
        Current = settings;
        string? backup = null;
        if (State is SettingsFileState.Broken or SettingsFileState.Repaired)
        {
            if (!TryBackup(out backup, out var backupError))
            {
                Problem = string.Format(Strings.Core_Settings_BackupFailed, backupError);
                return new(SettingsSaveStatus.Failed, Current, Problem);
            }
        }

        if (!SettingsStore.TrySave(settings, out var error, FilePath))
        {
            Problem = string.Format(Strings.Core_Settings_SaveFailedUntilExit, error);
            return new(SettingsSaveStatus.Failed, Current, Problem);
        }

        State = SettingsFileState.Loaded;
        Problem = null;
        _baseline = Current;
        if (backup is not null)
        {
            BackupPath = backup;
            var note = string.Format(Strings.Core_Settings_BackupCreated, Path.GetFileName(backup));
            message = message is null ? note : string.Format(Strings.Core_Settings_MessageAndNote, message, note);
        }
        return new(success, Current, message);
    }

    /// <summary>Kopiert die Datei unverändert nach „…defekt-JJJJMMTT-hhmmss“. Fehlt sie inzwischen, ist nichts zu sichern.</summary>
    private bool TryBackup(out string? backup, out string? error)
    {
        backup = null;
        error = null;
        if (!File.Exists(FilePath)) return true;
        var stamp = _clock().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        for (int i = 1; i <= MaxBackupNames; i++)
        {
            var candidate = $"{FilePath}.defekt-{stamp}" + (i == 1 ? "" : $"-{i}");
            if (Path.Exists(candidate)) continue;
            try
            {
                File.Copy(FilePath, candidate, overwrite: false);
                backup = candidate;
                return true;
            }
            catch (IOException ex) when (Path.Exists(candidate))
            {
                // Gleichzeitig angelegt (zweite Lupe) – nächster Name
                error = FileProblems.Describe(ex);
            }
            catch (Exception ex) when (FileProblems.IsExpected(ex))
            {
                error = FileProblems.Describe(ex);
                return false;
            }
        }
        error ??= Strings.Core_Settings_NoFreeFileName;
        return false;
    }
}
