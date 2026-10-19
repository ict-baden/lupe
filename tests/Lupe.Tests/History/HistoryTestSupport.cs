// SPDX-License-Identifier: MIT
using System.Text.Json;
using Lupe.Core.Model;

namespace Lupe.Tests.History;

/// <summary>Eigener, leerer Ordner unter %TEMP% je Test – wird danach gelöscht. Nie der echte Verlauf des Benutzers.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Lupe.Tests", "verlauf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /// <summary>Alle Dateien im Ordner (nur Namen), um liegen gebliebene Temp-Dateien zu finden.</summary>
    public string[] FileNames() => Directory.GetFiles(Path).Select(System.IO.Path.GetFileName).Order().ToArray()!;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Aufräumen ist Kür – ein gesperrter Rest in %TEMP% stört keinen Test
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal static class TestEntries
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 14, 9, 30, 0, TimeSpan.FromHours(2));

    public static HistoryEntry Make(string text, double minutes = 0, ScanSource source = ScanSource.Kamera, string format = "QR_CODE",
        ResultActions actions = ResultActions.Kopieren) => new()
        {
            Time = T0.AddMinutes(minutes),
            Text = text,
            Format = format,
            Source = source,
            ActionsDone = actions,
        };

    /// <summary>Gleich inklusive Zeitzonen-Versatz (Record-Gleichheit vergleicht bei DateTimeOffset nur den Zeitpunkt).</summary>
    public static void AssertSame(HistoryEntry expected, HistoryEntry actual)
    {
        Assert.Equal(expected, actual);
        Assert.True(expected.Time.EqualsExact(actual.Time), $"Zeit {expected.Time:o} erwartet, {actual.Time:o} gelesen");
    }

    public static void AssertSame(IReadOnlyList<HistoryEntry> expected, IReadOnlyList<HistoryEntry> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++) AssertSame(expected[i], actual[i]);
    }

    public static void AssertNewestFirst(IReadOnlyList<HistoryEntry> entries)
    {
        for (var i = 1; i < entries.Count; i++)
            Assert.True(entries[i - 1].Time >= entries[i].Time, $"Eintrag {i} ist neuer als sein Vorgänger");
    }

    /// <summary>Liest die Verlaufsdatei so, wie sie auf der Platte steht.</summary>
    public static JsonElement ReadFile(string path)
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllBytes(path));
        return doc.RootElement.Clone();
    }
}
