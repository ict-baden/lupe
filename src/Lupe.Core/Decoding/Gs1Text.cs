using System.Text;

namespace Lupe.Core.Decoding;

/// <summary>
/// GS1-Daten in Klammerschreibweise: „0198898765432106320201234515991231“ → „(01)98898765432106(3202)012345(15)991231“.
/// <para>
/// Nur für GS1 DataBar Expanded: Diesen Inhalt lieferte Lupe schon mit ZXing.Net in Klammern, und dabei bleibt es
/// (Link-Regeln, Verlauf, Eintippen). zxing-cpp liefert die Rohform mit Gruppentrennzeichen (0x1D) nach Feldern
/// variabler Länge – so wie Lupe GS1-128, GS1 Data Matrix und GS1-QR-Codes weiterhin ausgibt.
/// </para>
/// <para>
/// Tabelle der Datenbezeichner (AI) aus zxing-cpp 3.1.1 (core/src/HRI.cpp, Apache-2.0, nach dem GS1 Syntax Dictionary):
/// Präfix und Feldlänge, negativ = variabel bis höchstens so viele Zeichen. AIs, die mit 31–36 und 39 beginnen, sowie
/// 703 und 723 haben eine vierstellige Kennung (die letzte Ziffer gehört dazu, z. B. 3202).
/// </para>
/// </summary>
internal static class Gs1Text
{
    private const char GroupSeparator = (char)0x1D;

    private const string Table =
        "00:18 01:14 02:14 03:14 10:-20 11:6 12:6 13:6 15:6 16:6 17:6 20:2 21:-20 22:-20 30:-8 37:-8 " +
        "90:-30 91:-90 92:-90 93:-90 94:-90 95:-90 96:-90 97:-90 98:-90 99:-90 " +
        "235:-28 240:-30 241:-30 242:-6 243:-20 250:-30 251:-30 253:-30 254:-20 255:-25 " +
        "400:-30 401:-30 402:17 403:-30 410:13 411:13 412:13 413:13 414:13 415:13 416:13 417:13 " +
        "420:-20 421:-12 422:3 423:-15 424:3 425:-15 426:3 427:-3 " +
        "710:-20 711:-20 712:-20 713:-20 714:-20 715:-20 716:-20 717:-20 " +
        "310:6 311:6 312:6 313:6 314:6 315:6 316:6 320:6 321:6 322:6 323:6 324:6 325:6 326:6 327:6 328:6 329:6 " +
        "330:6 331:6 332:6 333:6 334:6 335:6 336:6 337:6 340:6 341:6 342:6 343:6 344:6 345:6 346:6 347:6 348:6 349:6 " +
        "350:6 351:6 352:6 353:6 354:6 355:6 356:6 357:6 360:6 361:6 362:6 363:6 364:6 365:6 366:6 367:6 368:6 369:6 " +
        "390:-15 391:-18 392:-15 393:-18 394:4 395:6 703:-30 723:-30 " +
        "4300:-35 4301:-35 4302:-70 4303:-70 4304:-70 4305:-70 4306:-70 4307:2 4308:-30 4309:20 4310:-35 4311:-35 " +
        "4312:-70 4313:-70 4314:-70 4315:-70 4316:-70 4317:2 4318:-20 4319:-30 4320:-35 4321:1 4322:1 4323:1 4324:10 " +
        "4325:10 4326:6 4330:-7 4331:-7 4332:-7 4333:-7 " +
        "7001:13 7002:-30 7003:10 7004:-4 7005:-12 7006:6 7007:-12 7008:-3 7009:-10 7010:-2 7011:-10 7020:-20 " +
        "7021:-20 7022:-20 7023:-30 7040:4 7041:-4 7240:-20 7241:2 7242:-25 7250:8 7251:12 7252:1 7253:-40 7254:-40 " +
        "7255:-10 7256:-90 7257:-70 7258:3 7259:-40 " +
        "8001:14 8002:-20 8003:-30 8004:-30 8005:6 8006:18 8007:-34 8008:-12 8009:-50 8010:-30 8011:-12 8012:-20 " +
        "8013:-25 8014:-25 8017:18 8018:18 8019:-10 8020:-25 8026:18 8030:-90 8040:15 8041:15 8042:32 8043:-20 " +
        "8110:-70 8111:4 8112:-70 8200:-70";

    private static readonly (string Prefix, int FieldSize)[] Ais = Table
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(e => e.Split(':'))
        .Select(p => (p[0], int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)))
        .ToArray();

    /// <summary>Klammerschreibweise, oder null, wenn der Inhalt kein gültiger GS1-Datensatz ist (dann bleibt die Rohform).</summary>
    public static string? Bracketed(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var rest = raw.AsSpan();
        var sb = new StringBuilder(raw.Length + 16);
        while (rest.Length > 0)
        {
            int match = -1;
            for (int i = 0; i < Ais.Length; i++)
                if (rest.StartsWith(Ais[i].Prefix, StringComparison.Ordinal)) { match = i; break; }
            if (match < 0) return null;

            var (prefix, size) = Ais[match];
            int aiLength = (prefix[0] == '3' && "1234569".Contains(prefix[1])) || prefix is "703" or "723" ? 4 : prefix.Length;
            if (rest.Length < aiLength) return null;
            sb.Append('(').Append(rest[..aiLength]).Append(')');
            rest = rest[aiLength..];

            int fieldSize = Math.Abs(size);
            if (size < 0)
            {
                int gs = rest.IndexOf(GroupSeparator);
                fieldSize = Math.Min(gs < 0 ? rest.Length : gs, fieldSize);
            }
            if (fieldSize == 0 || rest.Length < fieldSize) return null;
            sb.Append(rest[..fieldSize]);
            rest = rest[fieldSize..];
            // Ein Trennzeichen nach einem Feld wird toleriert, ob nötig oder nicht (GS1 General Specifications 7.8.6.3)
            if (rest.Length > 0 && rest[0] == GroupSeparator) rest = rest[1..];
        }
        return sb.ToString();
    }
}
