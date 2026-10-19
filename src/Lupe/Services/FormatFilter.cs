// SPDX-License-Identifier: MIT
using Lupe.Core.Decoding;

namespace Lupe.Services;

/// <summary>Welche Formate aus den Einstellungen die Erkennung bekommt.</summary>
internal static class FormatFilter
{
    /// <summary>
    /// Die gewünschten Formate, die die Erkennung kennt – in ihrer Schreibweise und Reihenfolge; null = alle. Eine von
    /// Hand bearbeitete oder ältere einstellungen.json mit unbekannten Namen soll das Lesen nicht lahmlegen. Bleibt
    /// nichts Bekanntes übrig, gilt dasselbe wie bei einer leeren Liste: alle Formate (sonst fände Lupe nie etwas).
    /// </summary>
    /// <param name="known">Alle Formate der Erkennung; null = unbekannt, dann unverändert weitergeben.</param>
    public static IReadOnlyList<string>? Usable(IReadOnlyList<string> wanted, IReadOnlyList<string>? known)
    {
        if (wanted.Count == 0) return null;
        if (known is null) return wanted;
        var usable = known.Where(k => wanted.Any(w => w.Trim().Equals(k, StringComparison.OrdinalIgnoreCase))).ToList();
        return usable.Count == 0 ? null : usable;
    }

    /// <summary>Alle Formate der Erkennung, null wenn sie sich nicht abfragen lassen.</summary>
    public static IReadOnlyList<string>? Known()
    {
        try { return BarcodeDecoder.AllFormats; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }
}
