using System.Text.RegularExpressions;
using Lupe.Core.Model;
using Lupe.Core.Resources;

namespace Lupe.Core.Settings;

/// <summary>Link-Regeln anwenden – sicher: nur http(s), Regex mit Zeitlimit, ungültige Regeln werden übersprungen.</summary>
/// <remarks>
/// <para>
/// Der Code wird vor allem anderen an beiden Enden von Leerraum befreit (Scanner liefern oft ein Zeilenende mit).
/// Die Regeln werden der Reihe nach geprüft (<see cref="Regex.IsMatch(string, string, RegexOptions, TimeSpan)"/>, also
/// „kommt irgendwo vor“ – wer den ganzen Code meint, verankert mit ^…$). Passt eine Regel, ergibt aber keinen sicheren
/// Link, geht es mit der nächsten weiter.
/// </para>
/// <para>Wie der Link entsteht – drei Fälle je nach Präfix:</para>
/// <list type="bullet">
/// <item><b>Präfix leer:</b> Der Code selbst ist der Link (z. B. ein QR-Code mit „https://…“).</item>
/// <item><b>Präfix nur „http://“ oder „https://“:</b> Der Code ist der Rest der Adresse (z. B. „www.example.com/x“) und wird
/// nicht maskiert – er bestimmt hier ausdrücklich den Host, genau wie bei leerem Präfix.</item>
/// <item><b>Präfix mit Host</b> (z. B. „https://example.com/suche?q=“): Der Code wird mit <see cref="Uri.EscapeDataString"/>
/// maskiert (alles außer A–Z, a–z, 0–9 und - . _ ~). Er landet damit nur als Daten im Link und kann weder Host, Pfad noch
/// weitere Parameter verändern („A&amp;B 1/2“ → „A%26B%201%2F2“). Dafür muss das Präfix den Host mit „/“, „?“ oder „#“
/// abschließen; zusätzlich wird geprüft, dass der fertige Link denselben Host hat wie das Präfix.</item>
/// </list>
/// <para>
/// Ergebnis ist nur ein absoluter http(s)-Link mit Host, ohne Steuerzeichen, Backslash oder Benutzername/Kennwort
/// („https://bank.de@evil.example“) – also nie file:, javascript:, ms-settings:, data:, UNC-Pfade o. Ä.
/// </para>
/// </remarks>
public static class LinkRules
{
    /// <summary>Zeitlimit je Regel und Code – schützt vor katastrophalem Backtracking in selbst geschriebenen Mustern.</summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    private const RegexOptions PatternOptions = RegexOptions.CultureInvariant;

    /// <summary>Der zu öffnende Link für <paramref name="text"/>, null wenn keine Regel passt oder das Ergebnis kein sicherer Link ist.</summary>
    public static Uri? Match(IEnumerable<LinkRule> rules, string text)
    {
        if (rules is null || text is null) return null;
        var code = text.Trim();
        if (code.Length == 0) return null;

        foreach (var rule in rules)
        {
            if (rule is null || !rule.Enabled || string.IsNullOrEmpty(rule.Pattern)) continue;
            if (!IsMatch(rule.Pattern, code)) continue;
            var link = BuildLink(rule.Prefix, code);
            if (link is not null) return link;
        }
        return null;
    }

    /// <summary>Prüft eine Regel für die Einstellungen; Fehlermeldung auf Deutsch oder null.</summary>
    public static string? Validate(LinkRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (string.IsNullOrWhiteSpace(rule.Pattern))
            return Strings.Core_LinkRule_PatternMissing;
        try
        {
            _ = new Regex(rule.Pattern, PatternOptions, MatchTimeout);
        }
        catch (RegexParseException ex)
        {
            return string.Format(Strings.Core_LinkRule_InvalidRegexAt, Describe(ex.Error), ex.Offset);
        }
        catch (ArgumentException ex)
        {
            return string.Format(Strings.Core_LinkRule_InvalidRegex, ex.Message);
        }

        var prefix = rule.Prefix?.Trim() ?? "";
        if (prefix.Length == 0) return null;
        return CheckPrefix(prefix, out _) switch
        {
            PrefixKind.SchemeOnly or PrefixKind.WithHost => null,
            PrefixKind.NotHttp => Strings.Core_LinkRule_PrefixNotHttp,
            PrefixKind.BadCharacters => Strings.Core_LinkRule_PrefixBadCharacters,
            PrefixKind.HostNotClosed => Strings.Core_LinkRule_PrefixHostNotClosed,
            PrefixKind.UserInfo => Strings.Core_LinkRule_PrefixUserInfo,
            _ => Strings.Core_LinkRule_PrefixInvalid,
        };
    }

    /// <summary>
    /// Prüft, ob <paramref name="candidate"/> ein sicherer, absoluter http(s)-Link ist, und gibt ihn zurück (sonst null).
    /// Der Text wird nicht verändert oder ergänzt.
    /// </summary>
    internal static Uri? SafeLink(string candidate)
    {
        if (!StartsWithHttpScheme(candidate) || HasBadCharacters(candidate)) return null;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return null;
        try
        {
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
            if (uri.IsFile || uri.IsUnc || string.IsNullOrEmpty(uri.Host)) return null;

            var absolute = uri.AbsoluteUri;
            if (HasUserInfo(absolute)) return null;
            // Die normalisierte Form ist das, was der Browser bekommt – sie muss für sich wieder genauso gelesen werden.
            // IdnHost wirft bei Zeichen, die nach IDN-Regeln im Hostnamen verboten sind (z. B. „／“ oder „﹫“) –
            // solche Links sind ohnehin nicht sicher und werden abgelehnt.
            if (!Uri.TryCreate(absolute, UriKind.Absolute, out var again)
                || !string.Equals(again.IdnHost, uri.IdnHost, StringComparison.OrdinalIgnoreCase)
                || again.Port != uri.Port || again.Scheme != uri.Scheme)
                return null;
            return uri;
        }
        catch (Exception ex) when (ex is UriFormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static bool IsMatch(string pattern, string code)
    {
        try
        {
            // Statische Variante: .NET hält die übersetzten Muster in einem Cache, wichtig bei vielen Kamera-Treffern
            return Regex.IsMatch(code, pattern, PatternOptions, MatchTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            // Ungültiges Muster – die Regel wird übersprungen (Validate meldet es in den Einstellungen)
            return false;
        }
    }

    private static Uri? BuildLink(string? prefix, string code)
    {
        var p = prefix?.Trim() ?? "";
        if (p.Length == 0) return SafeLink(code);

        switch (CheckPrefix(p, out var prefixUri))
        {
            case PrefixKind.SchemeOnly:
                return SafeLink(p + code);
            case PrefixKind.WithHost:
                string escaped;
                try
                {
                    escaped = Uri.EscapeDataString(code);
                }
                catch (UriFormatException)
                {
                    return null;
                }
                var link = SafeLink(p + escaped);
                return link is not null && SameOrigin(link, prefixUri!) ? link : null;
            default:
                return null;
        }
    }

    private enum PrefixKind { NotHttp, BadCharacters, SchemeOnly, HostNotClosed, UserInfo, Invalid, WithHost }

    private static PrefixKind CheckPrefix(string prefix, out Uri? prefixUri)
    {
        prefixUri = null;
        if (!StartsWithHttpScheme(prefix)) return PrefixKind.NotHttp;
        if (HasBadCharacters(prefix)) return PrefixKind.BadCharacters;

        var rest = prefix.AsSpan(prefix.IndexOf("://", StringComparison.Ordinal) + 3);
        if (rest.Length == 0) return PrefixKind.SchemeOnly;

        // Ohne Abschluss würde der Code an den Hostnamen angehängt („https://shop.example“ + „x.evil.example“)
        var hostEnd = rest.IndexOfAny('/', '?', '#');
        if (hostEnd < 0) return PrefixKind.HostNotClosed;
        if (rest[..hostEnd].Contains('@')) return PrefixKind.UserInfo;

        prefixUri = SafeLink(prefix);
        return prefixUri is null ? PrefixKind.Invalid : PrefixKind.WithHost;
    }

    private static bool SameOrigin(Uri link, Uri prefix) =>
        link.Scheme == prefix.Scheme
        && link.Port == prefix.Port
        && string.Equals(link.IdnHost, prefix.IdnHost, StringComparison.OrdinalIgnoreCase);

    private static bool StartsWithHttpScheme(string text) =>
        text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Steuerzeichen (auch Zeilenumbruch, Tab, DEL, C1) und Backslash – Browser und .NET lesen beides unterschiedlich.</summary>
    private static bool HasBadCharacters(string text)
    {
        foreach (var c in text)
        {
            if (char.IsControl(c) || c == '\\') return true;
        }
        return false;
    }

    /// <summary>Steht im Autoritätsteil (zwischen „://“ und dem ersten / ? #) ein „@“, enthält der Link Anmeldedaten.</summary>
    private static bool HasUserInfo(string absoluteUri)
    {
        var start = absoluteUri.IndexOf("://", StringComparison.Ordinal);
        if (start < 0) return true;
        var authority = absoluteUri.AsSpan(start + 3);
        var end = authority.IndexOfAny('/', '?', '#');
        if (end >= 0) authority = authority[..end];
        return authority.Contains('@');
    }

    private static string Describe(RegexParseError error) => error switch
    {
        RegexParseError.InsufficientClosingParentheses => Strings.Core_Regex_MissingClosingParenthesis,
        RegexParseError.InsufficientOpeningParentheses => Strings.Core_Regex_TooManyClosingParentheses,
        RegexParseError.UnterminatedBracket => Strings.Core_Regex_UnterminatedBracket,
        RegexParseError.ReversedCharacterRange => Strings.Core_Regex_ReversedCharacterRange,
        RegexParseError.ReversedQuantifierRange => Strings.Core_Regex_ReversedQuantifierRange,
        RegexParseError.QuantifierAfterNothing => Strings.Core_Regex_QuantifierAfterNothing,
        RegexParseError.NestedQuantifiersNotParenthesized => Strings.Core_Regex_NestedQuantifiers,
        RegexParseError.UnescapedEndingBackslash => Strings.Core_Regex_EndingBackslash,
        RegexParseError.UnrecognizedEscape => Strings.Core_Regex_UnrecognizedEscape,
        RegexParseError.UnrecognizedControlCharacter or RegexParseError.MissingControlCharacter => Strings.Core_Regex_InvalidControlCharacter,
        RegexParseError.InsufficientOrInvalidHexDigits => Strings.Core_Regex_InvalidHexDigits,
        RegexParseError.InvalidGroupingConstruct => Strings.Core_Regex_InvalidGroup,
        RegexParseError.UndefinedNamedReference or RegexParseError.UndefinedNumberedReference
            or RegexParseError.MalformedNamedReference => Strings.Core_Regex_UndefinedReference,
        RegexParseError.CaptureGroupNameInvalid or RegexParseError.CaptureGroupOfZero => Strings.Core_Regex_InvalidGroupName,
        RegexParseError.UnrecognizedUnicodeProperty or RegexParseError.InvalidUnicodePropertyEscape
            or RegexParseError.MalformedUnicodePropertyEscape => Strings.Core_Regex_UnknownUnicodeCategory,
        RegexParseError.UnterminatedComment => Strings.Core_Regex_UnterminatedComment,
        RegexParseError.QuantifierOrCaptureGroupOutOfRange => Strings.Core_Regex_NumberTooLarge,
        RegexParseError.ShorthandClassInCharacterRange => Strings.Core_Regex_ShorthandInRange,
        RegexParseError.ExclusionGroupNotLast => Strings.Core_Regex_ExclusionGroupNotLast,
        _ => Strings.Core_Regex_SyntaxError,
    };
}
