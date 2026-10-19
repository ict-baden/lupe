// SPDX-License-Identifier: MIT
using Lupe.Core.Model;

namespace Lupe.Core.Camera;

/// <summary>
/// Wiederholungssperre („Freeze“) je Text + Format: Ein Code wird erst wieder gemeldet, wenn er die Sperrzeit lang
/// <b>nicht</b> gesehen wurde. Jede Sichtung – auch eine gesperrte – verlängert die Sperre (ab letzter Sichtung, nicht ab
/// erster). Nicht threadsicher; der Aufrufer sperrt.
/// </summary>
internal sealed class RepeatFilter
{
    private readonly TimeSpan _window;
    private readonly TimeProvider _time;
    private readonly Dictionary<(string Text, string Format), long> _lastSeen = [];

    public RepeatFilter(TimeSpan window, TimeProvider time)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        _window = window;
        _time = time;
    }

    /// <summary>Anzahl der gemerkten Codes (für Tests: alte Einträge werden aufgeräumt).</summary>
    public int Count => _lastSeen.Count;

    public void Clear() => _lastSeen.Clear();

    /// <summary>
    /// Die Codes eines Bildes, die gemeldet werden dürfen (Reihenfolge bleibt, Doppelte im selben Bild nur einmal).
    /// <paramref name="timestamp"/> ist der Zeitpunkt der Sichtung (<see cref="TimeProvider.GetTimestamp"/>).
    /// </summary>
    public IReadOnlyList<DecodedCode> Filter(IReadOnlyList<DecodedCode> codes, long timestamp)
    {
        Prune(timestamp);
        var fresh = new List<DecodedCode>(codes.Count);
        var inThisFrame = new HashSet<(string, string)>();
        foreach (var code in codes)
        {
            if (code is null) continue;
            var key = (code.Text ?? "", code.Format ?? "");
            if (!inThisFrame.Add(key)) continue;
            if (_lastSeen.TryGetValue(key, out long last) && IsWithinWindow(last, timestamp))
            {
                _lastSeen[key] = Math.Max(last, timestamp);
                continue;
            }
            _lastSeen[key] = timestamp;
            fresh.Add(code);
        }
        return fresh;
    }

    private bool IsWithinWindow(long last, long now) => _time.GetElapsedTime(last, now) < _window;

    /// <summary>Codes vergessen, deren Sperre abgelaufen ist – hält das Wörterbuch klein.</summary>
    private void Prune(long now)
    {
        List<(string, string)>? expired = null;
        foreach (var (key, last) in _lastSeen)
            if (!IsWithinWindow(last, now)) (expired ??= []).Add(key);
        if (expired is null) return;
        foreach (var key in expired) _lastSeen.Remove(key);
    }
}
