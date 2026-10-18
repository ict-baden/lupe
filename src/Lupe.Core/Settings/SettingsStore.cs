using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Lupe.Core.Decoding;
using Lupe.Core.Model;
using Lupe.Core.Resources;

namespace Lupe.Core.Settings;

/// <summary>Einstellungen als JSON unter %LocalAppData%\Lupe\einstellungen.json.</summary>
/// <remarks>
/// Laden ist tolerant: fehlende Felder bekommen den Standardwert, unbekannte werden ignoriert, ein einzelnes
/// unbrauchbares Feld (falscher Typ) fällt auf seinen Standard zurück, ohne die übrigen zu verlieren; in Listen
/// (Link-Regeln, Formate) werden nur die unbrauchbaren Elemente verworfen. Danach werden alle Werte in gültige
/// Bereiche gebracht (<see cref="Normalize"/>). Gespeichert wird atomar (Temp-Datei + Ersetzen).
/// </remarks>
public static class SettingsStore
{
    /// <summary>Kleinster erlaubter Wert für <see cref="LupeSettings.HistoryLimit"/>.</summary>
    public const int MinHistoryLimit = 1;
    /// <summary>
    /// Größter erlaubter Wert für <see cref="LupeSettings.HistoryLimit"/>. Der Verlauf wird nach jedem Treffer ganz neu
    /// geschrieben (atomar, siehe <see cref="History.HistoryStore"/>) – bei 10 000 Einträgen sind das rund 2 MB und
    /// gemessen rund 40 ms, bei 100 000 stünde die Oberfläche bei jedem Treffer spürbar.
    /// </summary>
    public const int MaxHistoryLimit = 10_000;
    /// <summary>Größter erlaubter Wert für <see cref="LupeSettings.TypeDelayMs"/> (eine halbe Sekunde je Zeichen).</summary>
    public const int MaxTypeDelayMs = 500;

    /// <summary>Größter erlaubter Wert für <see cref="LupeSettings.RepeatSuppressMs"/> (eine Stunde).</summary>
    public const int MaxRepeatSuppressMs = 3_600_000;

    private const ResultActions AllActions = ResultActions.Kopieren | ResultActions.Eintippen | ResultActions.LinkOeffnen;

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        // Regex-Muster lesbar halten (sonst würde z. B. „+“ als + geschrieben); die Datei landet nie in HTML
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Alle Einstellungen, die aus der Datei übernommen werden (öffentlich, mit init/set).</summary>
    private static readonly PropertyInfo[] SettingsProperties = typeof(LupeSettings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
        .ToArray();

    private static readonly Lock SaveGate = new();

    /// <summary>Windows: %LocalAppData%\Lupe\einstellungen.json; Linux: ~/.config/lupe; macOS: ~/Library/Application Support/Lupe (siehe <see cref="AppPaths"/>).</summary>
    public static string DefaultPath => Path.Combine(AppPaths.ConfigDirectory, "einstellungen.json");

    /// <summary>Fehlt die Datei oder ist sie kaputt: Standardwerte (eine kaputte Datei wird nicht überschrieben, bis gespeichert wird).</summary>
    public static LupeSettings Load(string? path = null) => Load(path, out _, out _);

    /// <summary>
    /// Wie <see cref="Load(string?)"/>; <paramref name="problem"/> beschreibt auf Deutsch, was nicht stimmte (Datei kaputt,
    /// nicht lesbar, einzelne Werte ersetzt), sonst null. Wirft nie wegen der Datei.
    /// </summary>
    public static LupeSettings Load(string? path, out string? problem) => Load(path, out problem, out _);

    /// <summary>
    /// Wie <see cref="Load(string?, out string?)"/>; <paramref name="state"/> sagt zusätzlich, wie die Datei vorgefunden
    /// wurde – wichtig, bevor gespeichert wird: Eine kaputte (<see cref="SettingsFileState.Broken"/>) oder nicht lesbare
    /// (<see cref="SettingsFileState.Unreadable"/>) Datei darf nicht einfach mit Standardwerten überschrieben werden. Das
    /// übernimmt <see cref="SettingsFile"/>.
    /// </summary>
    public static LupeSettings Load(string? path, out string? problem, out SettingsFileState state)
    {
        problem = null;
        state = SettingsFileState.Missing;
        var file = ResolvePath(path);

        byte[] bytes;
        try
        {
            bytes = AtomicFile.ReadAllBytes(file);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new LupeSettings();
        }
        catch (Exception ex) when (FileProblems.IsExpected(ex))
        {
            state = SettingsFileState.Unreadable;
            problem = string.Format(Strings.Core_Settings_ReadError, file, FileProblems.Describe(ex));
            return new LupeSettings();
        }

        var json = SkipUtf8Bom(bytes);
        if (IsBlank(json)) return new LupeSettings();

        try
        {
            var replaced = new List<string>();
            var filtered = new List<string>();
            var settings = Parse(json, replaced, filtered);
            state = SettingsFileState.Loaded;
            if (replaced.Count > 0 || filtered.Count > 0)
            {
                state = SettingsFileState.Repaired;
                problem = string.Format(Strings.Core_Settings_SomeInvalid, file);
                if (replaced.Count > 0) problem += " " + string.Format(Strings.Core_Settings_DefaultUsedFor, DisplayNames(replaced));
                if (filtered.Count > 0) problem += " " + string.Format(Strings.Core_Settings_EntriesRemovedFrom, DisplayNames(filtered));
            }
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or ArgumentException or FormatException)
        {
            state = SettingsFileState.Broken;
            problem = ex is JsonException { LineNumber: { } line }
                ? string.Format(Strings.Core_Settings_BrokenAtLine, file, line + 1)
                : string.Format(Strings.Core_Settings_Broken, file);
            return new LupeSettings();
        }
    }

    /// <summary>
    /// Speichert atomar (Ordner wird angelegt). Die Werte werden vorher mit <see cref="Normalize"/> bereinigt.
    /// Kann die Datei nicht geschrieben werden, kommt eine <see cref="IOException"/> mit deutscher Meldung –
    /// wer das nicht abfangen will, nimmt <see cref="TrySave"/>.
    /// </summary>
    public static void Save(LupeSettings settings, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var file = ResolvePath(path);
        try
        {
            SaveCore(settings, file);
        }
        catch (Exception ex) when (FileProblems.IsExpected(ex))
        {
            throw new IOException(SaveError(file, ex), ex);
        }
    }

    /// <summary>Wie <see cref="Save"/>, aber ohne Ausnahme: false und deutsche Meldung in <paramref name="error"/>, wenn es nicht klappt.</summary>
    public static bool TrySave(LupeSettings settings, out string? error, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var file = ResolvePath(path);
        try
        {
            SaveCore(settings, file);
            error = null;
            return true;
        }
        catch (Exception ex) when (FileProblems.IsExpected(ex))
        {
            error = SaveError(file, ex);
            return false;
        }
    }

    /// <summary>
    /// Bringt alle Werte in gültige Bereiche: unbekannte Aktions-Bits weg, Suffix nur "", "Enter" oder "Tab", Link-Regeln
    /// ohne Muster weg (Präfix getrimmt; Regeln mit ungültigem Regex bleiben, damit man sie in der Tabelle sieht und
    /// korrigiert – angewendet werden sie nicht), Formate in ZXing-Schreibweise (unbekannte und doppelte weg),
    /// Tastenkürzel ohne gültige Taste oder Schreibtaste ohne Strg/Alt/Win → Standard, Zahlen begrenzt, null-Texte → "".
    /// </summary>
    public static LupeSettings Normalize(LupeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var defaults = new LupeSettings();
        return settings with
        {
            Actions = settings.Actions & AllActions,
            ScreenActions = settings.ScreenActions & AllActions,
            TypeSuffix = NormalizeSuffix(settings.TypeSuffix),
            TypeDelayMs = Math.Clamp(settings.TypeDelayMs, 0, MaxTypeDelayMs),
            LinkRules = settings.LinkRules is null ? defaults.LinkRules : NormalizeRules(settings.LinkRules),
            Formats = settings.Formats is null ? defaults.Formats : NormalizeFormats(settings.Formats),
            ScreenHotkey = IsUsableHotkey(settings.ScreenHotkey) ? settings.ScreenHotkey : Hotkey.PlatformDefault,
            RepeatSuppressMs = Math.Clamp(settings.RepeatSuppressMs, 0, MaxRepeatSuppressMs),
            Camera = settings.Camera?.Trim() ?? "",
            HistoryLimit = Math.Clamp(settings.HistoryLimit, MinHistoryLimit, MaxHistoryLimit),
            Language = Localization.UiLanguage.Normalize(settings.Language),
        };
    }

    internal static string ResolvePath(string? path) => Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? DefaultPath : path);

    /// <summary>Gleiche Werte nach dem Bereinigen (Listen nach Inhalt – die Record-Gleichheit vergleicht sie nur als Verweis).</summary>
    public static bool SameValues(LupeSettings a, LupeSettings b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return ReferenceEquals(a, b) || JsonNode.DeepEquals(ToNode(a), ToNode(b));
    }

    /// <summary>
    /// Legt die Änderungen von <paramref name="from"/> nach <paramref name="to"/> feldweise auf <paramref name="target"/>:
    /// Jedes Feld, das sich geändert hat, kommt aus <paramref name="to"/>, alle übrigen bleiben wie in <paramref name="target"/>.
    /// </summary>
    internal static LupeSettings ApplyChanges(LupeSettings target, LupeSettings from, LupeSettings to)
    {
        JsonObject result = ToNode(target), before = ToNode(from), after = ToNode(to);
        foreach (var property in SettingsProperties)
        {
            var old = before[property.Name];
            var now = after[property.Name];
            if (!JsonNode.DeepEquals(old, now)) result[property.Name] = now?.DeepClone();
        }
        return Normalize(result.Deserialize<LupeSettings>(Json) ?? new LupeSettings());
    }

    private static JsonObject ToNode(LupeSettings settings) => JsonSerializer.SerializeToNode(Normalize(settings), Json)!.AsObject();

    private static void SaveCore(LupeSettings settings, string file)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Normalize(settings), Json);
        // Gleichzeitiges Speichern aus mehreren Threads nacheinander – die Datei ist danach immer eine vollständige Fassung
        lock (SaveGate)
        {
            AtomicFile.Write(file, bytes);
        }
    }

    private static string SaveError(string file, Exception ex) =>
        string.Format(Strings.Core_Settings_SaveError, file, FileProblems.Describe(ex));

    /// <summary>
    /// Legt die Datei feldweise über die Standardwerte: Jedes bekannte Feld wird nur übernommen, wenn es sich in
    /// den Zieltyp lesen lässt; bei Listen wird elementweise gefiltert.
    /// </summary>
    private static LupeSettings Parse(ReadOnlySpan<byte> json, List<string> replaced, List<string> filtered)
    {
        var root = JsonNode.Parse(json, NodeOptions, DocumentOptions);
        if (root is not JsonObject file)
            throw new JsonException("Die Datei enthält kein JSON-Objekt.");

        var merged = JsonSerializer.SerializeToNode(new LupeSettings(), Json)!.AsObject();
        foreach (var property in SettingsProperties)
        {
            if (!file.TryGetPropertyValue(property.Name, out var value)) continue;

            if (value is JsonArray array && TryGetListElementType(property.PropertyType, out var elementType))
            {
                var kept = new JsonArray();
                foreach (var item in array)
                {
                    if (CanRead(item, elementType)) kept.Add(item?.DeepClone());
                }
                if (kept.Count != array.Count) filtered.Add(property.Name);
                merged[property.Name] = kept;
            }
            else if (CanRead(value, property.PropertyType))
            {
                merged[property.Name] = value?.DeepClone();
            }
            else
            {
                replaced.Add(property.Name);
            }
        }

        var settings = merged.Deserialize<LupeSettings>(Json) ?? new LupeSettings();
        return Normalize(settings);
    }

    /// <summary>Bezeichnungen der Felder für Hinweise (wie in den Einstellungen der App), in der Sprache der Oberfläche.</summary>
    private static string FieldName(string property) => property switch
    {
        nameof(LupeSettings.Actions) => Strings.Core_Settings_Field_Actions,
        nameof(LupeSettings.ScreenActions) => Strings.Core_Settings_Field_ScreenActions,
        nameof(LupeSettings.TypeSuffix) => Strings.Core_Settings_Field_TypeSuffix,
        nameof(LupeSettings.TypeDelayMs) => Strings.Core_Settings_Field_TypeDelay,
        nameof(LupeSettings.LinkRules) => Strings.Core_Settings_Field_LinkRules,
        nameof(LupeSettings.Formats) => Strings.Core_Settings_Field_Formats,
        nameof(LupeSettings.ScreenHotkey) => Strings.Core_Settings_Field_ScreenHotkey,
        nameof(LupeSettings.Sound) => Strings.Core_Settings_Field_Sound,
        nameof(LupeSettings.AimHelp) => Strings.Core_Settings_Field_AimHelp,
        nameof(LupeSettings.RepeatSuppressMs) => Strings.Core_Settings_Field_RepeatSuppress,
        nameof(LupeSettings.Camera) => Strings.Core_Settings_Field_Camera,
        nameof(LupeSettings.KeepHistory) => Strings.Core_Settings_Field_KeepHistory,
        nameof(LupeSettings.HistoryLimit) => Strings.Core_Settings_Field_HistoryLimit,
        nameof(LupeSettings.KeepRunningInTray) => Strings.Core_Settings_Field_KeepRunningInTray,
        nameof(LupeSettings.Language) => Strings.Core_Settings_Field_Language,
        nameof(LupeSettings.TrayHintShown) => Strings.Core_Settings_Field_TrayHintShown,
        _ => property,
    };

    private static string DisplayNames(IEnumerable<string> properties) =>
        string.Join(", ", properties.Select(FieldName));

    private static bool CanRead(JsonNode? value, Type type)
    {
        try
        {
            _ = value.Deserialize(type, Json);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or FormatException
                                       or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    private static bool TryGetListElementType(Type type, out Type elementType)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() is var definition
            && (definition == typeof(IReadOnlyList<>) || definition == typeof(IList<>) || definition == typeof(List<>)
                || definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyCollection<>)))
        {
            elementType = type.GetGenericArguments()[0];
            return true;
        }
        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }
        elementType = typeof(object);
        return false;
    }

    private static string NormalizeSuffix(string? suffix) => suffix?.Trim() switch
    {
        { } s when s.Equals("Enter", StringComparison.OrdinalIgnoreCase) => "Enter",
        { } s when s.Equals("Tab", StringComparison.OrdinalIgnoreCase) => "Tab",
        _ => "",
    };

    private static LinkRule[] NormalizeRules(IReadOnlyList<LinkRule> rules) => rules
        .Where(r => r is not null && !string.IsNullOrEmpty(r.Pattern))
        .Select(r => r with { Prefix = r.Prefix?.Trim() ?? "" })
        .ToArray();

    /// <summary>
    /// Formatnamen in ZXing-Schreibweise (QR_CODE …); nur bekannte Formate der Erkennung, ohne Doppelte. Formate, die
    /// Lupe nicht mehr liest (z. B. IMB seit dem Wechsel auf zxing-cpp), fallen dabei weg.
    /// </summary>
    private static string[] NormalizeFormats(IReadOnlyList<string> formats)
    {
        var result = new List<string>();
        foreach (var raw in formats)
        {
            var name = raw?.Trim();
            if (string.IsNullOrEmpty(name) || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') || char.IsAsciiDigit(name[0])) continue;
            if (FormatCatalog.Find(name) is not { } format) continue;
            if (!result.Contains(format.Name, StringComparer.Ordinal)) result.Add(format.Name);
        }
        return result.ToArray();
    }

    /// <summary>
    /// Eine echte Taste (keine Maustaste, keine reine Umschalt-/Strg-/Alt-/Windows-Taste) im Bereich der Virtual-Key-Codes.
    /// Ohne Strg, Alt oder Win (Umschalt allein zählt nicht) taugen nur F1–F24, Druck und Pause: Ein systemweites Kürzel
    /// auf „A“, „Umschalt+A“, Enter oder einer Pfeiltaste finge diese Taste in allen Programmen ab – Tippen ginge nicht mehr.
    /// </summary>
    private static bool IsUsableHotkey(Hotkey? hotkey)
    {
        if (hotkey is null || hotkey.Key is < 0x08 or > 0xFE) return false;
        if (hotkey.Key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5)) return false;
        return hotkey.Ctrl || hotkey.Alt || hotkey.Win || IsStandaloneKey(hotkey.Key);
    }

    /// <summary>F1–F24 (0x70–0x87), Druck (0x2C) und Pause (0x13) – Tasten, die beim Schreiben nicht gebraucht werden.</summary>
    private static bool IsStandaloneKey(int key) => key is (>= 0x70 and <= 0x87) or 0x2C or 0x13;

    internal static ReadOnlySpan<byte> SkipUtf8Bom(byte[] bytes) =>
        bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? bytes.AsSpan(3) : bytes;

    internal static bool IsBlank(ReadOnlySpan<byte> json)
    {
        foreach (var b in json)
        {
            if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')) return false;
        }
        return true;
    }
}
