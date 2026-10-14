using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Lupe.Core.Model;
using Lupe.Core.Settings;

namespace Lupe.Core.History;

/// <summary>Wie die Verlaufsdatei gelesen wurde.</summary>
internal enum HistoryReadStatus
{
    /// <summary>Keine Datei (oder leer) – frischer Verlauf.</summary>
    Missing,
    /// <summary>Gelesen; einzelne unbrauchbare Einträge können übersprungen worden sein.</summary>
    Loaded,
    /// <summary>Inhalt kaputt (kein JSON, falscher Aufbau) – beim nächsten Hinzufügen wird sie ersetzt.</summary>
    Broken,
    /// <summary>Nicht lesbar (gesperrt, keine Berechtigung) – Inhalt unbekannt, darf nicht überschrieben werden.</summary>
    Unreadable,
}

internal sealed record HistoryReadResult(HistoryReadStatus Status, IReadOnlyList<HistoryEntry> Entries, int Skipped, string? Problem);

/// <summary>
/// Dateiformat des Verlaufs: <c>{ "Version": 1, "Entries": [ { "Time": "…+02:00", "Text": "…", "Format": "QR_CODE",
/// "Source": "Kamera", "ActionsDone": "Kopieren, LinkOeffnen" } ] }</c>, neueste zuerst, UTF-8 ohne BOM.
/// Lesen ist tolerant: unbrauchbare Einträge werden übersprungen, die übrigen bleiben.
/// </summary>
internal static class HistoryFile
{
    public const int CurrentVersion = 1;

    private const ResultActions AllActions = ResultActions.Kopieren | ResultActions.Eintippen | ResultActions.LinkOeffnen;

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static HistoryReadResult Read(string path)
    {
        byte[] bytes;
        try
        {
            bytes = AtomicFile.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new HistoryReadResult(HistoryReadStatus.Missing, [], 0, null);
        }
        catch (Exception ex) when (FileProblems.IsExpected(ex))
        {
            return new HistoryReadResult(HistoryReadStatus.Unreadable, [], 0,
                $"Der gespeicherte Verlauf „{path}“ konnte nicht gelesen werden ({FileProblems.Describe(ex)}). "
                + "Neue Einträge werden erst gespeichert, wenn die Datei wieder lesbar ist.");
        }

        var json = SettingsStore.SkipUtf8Bom(bytes);
        if (SettingsStore.IsBlank(json)) return new HistoryReadResult(HistoryReadStatus.Missing, [], 0, null);

        try
        {
            var (entries, skipped) = Parse(json);
            var problem = skipped switch
            {
                0 => null,
                1 => "Ein Eintrag im gespeicherten Verlauf war unlesbar und wurde übersprungen.",
                _ => $"{skipped} Einträge im gespeicherten Verlauf waren unlesbar und wurden übersprungen.",
            };
            return new HistoryReadResult(HistoryReadStatus.Loaded, entries, skipped, problem);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            var where = ex is JsonException { LineNumber: { } line } ? $" (Fehler in Zeile {line + 1})" : "";
            return new HistoryReadResult(HistoryReadStatus.Broken, [], 0,
                $"Der gespeicherte Verlauf „{path}“ ist beschädigt{where} und wurde nicht geladen. "
                + "Die Datei bleibt unverändert, bis ein neuer Eintrag dazukommt – dann beginnt der Verlauf neu.");
        }
    }

    /// <summary>Liest die Einträge in Dateireihenfolge; wirft <see cref="JsonException"/>, wenn die Datei als Ganzes unbrauchbar ist.</summary>
    internal static (List<HistoryEntry> Entries, int Skipped) Parse(ReadOnlySpan<byte> json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, NodeOptions, DocumentOptions);
        }
        catch (ArgumentException ex)
        {
            // z. B. doppelte Schlüssel
            throw new JsonException(ex.Message, ex);
        }

        JsonArray items = root switch
        {
            JsonObject obj when !obj.TryGetPropertyValue("Entries", out var value) || value is null => [],
            JsonObject obj => obj["Entries"] as JsonArray ?? throw new JsonException("„Entries“ ist keine Liste."),
            // Nackte Liste ohne Hülle wird auch akzeptiert
            JsonArray array => array,
            _ => throw new JsonException("Die Datei enthält weder ein Objekt noch eine Liste."),
        };

        var entries = new List<HistoryEntry>(items.Count);
        var skipped = 0;
        foreach (var item in items)
        {
            var entry = TryReadEntry(item);
            if (entry is null) skipped++;
            else entries.Add(entry);
        }
        return (entries, skipped);
    }

    private static HistoryEntry? TryReadEntry(JsonNode? item)
    {
        if (item is not JsonObject) return null;
        HistoryEntry? entry;
        try
        {
            entry = item.Deserialize<HistoryEntry>(Json);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or FormatException
                                       or ArgumentException or OverflowException)
        {
            return null;
        }
        if (entry is null || entry.Text is null || entry.Format is null || !Enum.IsDefined(entry.Source)) return null;
        return (entry.ActionsDone & ~AllActions) == 0 ? entry : entry with { ActionsDone = entry.ActionsDone & AllActions };
    }

    public static byte[] Serialize(IReadOnlyList<HistoryEntry> entries)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, Encoder = Json.Encoder }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("Version", CurrentVersion);
            writer.WritePropertyName("Entries");
            writer.WriteStartArray();
            foreach (var entry in entries)
            {
                // Lücken (null-Texte) nicht in die Datei – sie würden beim Lesen als unbrauchbar übersprungen
                var clean = entry.Text is null || entry.Format is null
                    ? entry with { Text = entry.Text ?? "", Format = entry.Format ?? "" }
                    : entry;
                JsonSerializer.Serialize(writer, clean, Json);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    public static void Write(string path, IReadOnlyList<HistoryEntry> entries) => AtomicFile.Write(path, Serialize(entries));
}
