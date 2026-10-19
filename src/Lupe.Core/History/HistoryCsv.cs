// SPDX-License-Identifier: MIT
using System.Buffers;
using System.Globalization;
using System.Text;
using Lupe.Core.Model;

namespace Lupe.Core.History;

/// <summary>
/// CSV wie Excel in Deutschland es erwartet: UTF-8 mit BOM, Semikolon, CRLF, Zeit „dd.MM.yyyy HH:mm:ss“ (Ortszeit).
/// Felder mit Semikolon, Anführungszeichen, Zeilenumbruch oder Leerraum am Rand werden in "…" gesetzt ("" für ").
/// Gegen CSV-Injektion wird jedem Feld, das (auch nach Leerzeichen) mit = + - @ Tab CR LF oder deren
/// Vollbreiten-Formen beginnt, ein ' vorangestellt – Excel und LibreOffice zeigen es dann als Text statt als Formel.
/// </summary>
internal static class HistoryCsv
{
    public const string Header = "Zeit;Format;Quelle;Inhalt";
    public const string TimeFormat = "dd.MM.yyyy HH:mm:ss";

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly SearchValues<char> QuoteTriggers = SearchValues.Create(";\"\r\n");

    public static byte[] Build(IEnumerable<HistoryEntry> entries)
    {
        var text = new StringBuilder();
        text.Append(Header).Append("\r\n");
        foreach (var entry in entries)
        {
            text.Append(Field(FormatTime(entry.Time))).Append(';')
                .Append(Field(entry.Format)).Append(';')
                .Append(Field(entry.Source.ToString())).Append(';')
                .Append(Field(entry.Text)).Append("\r\n");
        }

        var preamble = Utf8WithBom.GetPreamble();
        var body = Utf8WithBom.GetBytes(text.ToString());
        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    /// <summary>Ein Feld, entschärft und bei Bedarf in Anführungszeichen.</summary>
    internal static string Field(string? value)
    {
        value ??= "";
        if (StartsLikeFormula(value)) value = "'" + value;

        var needsQuotes = value.AsSpan().IndexOfAny(QuoteTriggers) >= 0
            || (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])));
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    internal static string FormatTime(DateTimeOffset time)
    {
        DateTimeOffset local;
        try
        {
            local = time.ToLocalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            // Randwerte (MinValue/MaxValue) lassen sich nicht immer umrechnen
            local = time;
        }
        return local.ToString(TimeFormat, CultureInfo.InvariantCulture);
    }

    private static bool StartsLikeFormula(string value)
    {
        if (value.Length == 0) return false;
        if (IsFormulaStart(value[0])) return true;
        // Auch „  =1+1“: manche Programme ignorieren führende Leerzeichen
        var trimmed = value.AsSpan().TrimStart();
        return trimmed.Length > 0 && IsFormulaStart(trimmed[0]);
    }

    private static bool IsFormulaStart(char c) => c is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n'
        or '\uFF1D' or '\uFF0B' or '\uFF0D' or '\uFF20'; // ＝ ＋ － ＠
}
