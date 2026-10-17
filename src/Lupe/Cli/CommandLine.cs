using System.Text;
using Lupe.Core.Decoding;
using Lupe.Core.Input;
using Lupe.Core.Model;

namespace Lupe.Cli;

/// <summary>
/// Befehlszeile derselben Exe: <c>lesen</c> und <c>--hilfe</c>. Für Skripte zählt der Exitcode
/// (0 = mindestens ein Code gefunden, 1 = keiner, 3 = Fehler oder Abbruch).
/// </summary>
internal static class CommandLine
{
    public const int ExitFound = 0;
    public const int ExitNothing = 1;
    public const int ExitError = 3;

    private static readonly string[] HelpWords = ["--hilfe", "-hilfe", "hilfe", "--help", "-h", "-?", "/?", "/h"];

    public static int Run(string[] args)
    {
        ConsoleSession.Attach();
        try
        {
            var command = args.Length == 0 ? "--hilfe" : args[0].Trim();
            if (HelpWords.Contains(command, StringComparer.OrdinalIgnoreCase))
            {
                PrintHelp();
                return ExitFound;
            }
            return command.ToLowerInvariant() switch
            {
                "lesen" => Read(args.Skip(1).ToArray()),
                "--screenshots" => throw new UsageException("Aufruf: Lupe.exe --screenshots <ordner> [--theme hell|dunkel|beide]"),
                "--demo" => throw new UsageException("„--demo“ steht allein: Lupe.exe --demo"),
                "--hintergrund" => throw new UsageException("„--hintergrund“ steht allein: Lupe.exe --hintergrund"),
                _ => throw new UsageException($"Unbekannter Befehl „{command}“."),
            };
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine($"Fehler: {ex.Message}");
            Console.Error.WriteLine("Hilfe: Lupe.exe --hilfe");
            return ExitError;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Abgebrochen.");
            return ExitError;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fehler: {ex.Message}");
            return ExitError;
        }
    }

    // ================= lesen =================

    private static int Read(string[] args)
    {
        var inputs = new List<string>();
        var formatArgs = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i].Trim();
            if (arg.Length == 0) continue;
            if (arg.Equals("--format", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].TrimStart().StartsWith("--", StringComparison.Ordinal))
                    throw new UsageException("„--format“ braucht einen Wert, z. B. --format QR_CODE.");
                formatArgs.Add(args[++i]);
            }
            else if (arg.StartsWith("--format=", StringComparison.OrdinalIgnoreCase)) formatArgs.Add(arg["--format=".Length..]);
            else if (arg.StartsWith("--", StringComparison.Ordinal)) throw new UsageException($"Unbekannte Option „{arg}“.");
            else inputs.Add(arg);
        }
        if (inputs.Count == 0) throw new UsageException("Bitte mindestens ein Bild angeben, z. B. Lupe.exe lesen etikett.png");

        // Formate vor den Dateien prüfen: ein Tippfehler soll nicht erst nach dem Lesen vieler Bilder auffallen
        var formats = ResolveFormats(formatArgs);

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            // Strg+C: sauber abbrechen (Exitcode 3) statt den Prozess hart zu beenden
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            bool failed = false;
            int found = 0;
            BarcodeDecoder? decoder = null;
            foreach (var input in inputs)
            {
                foreach (var (path, problem) in Expand(input))
                {
                    cts.Token.ThrowIfCancellationRequested();
                    if (problem is not null)
                    {
                        Console.Error.WriteLine(problem);
                        failed = true;
                        continue;
                    }
                    IReadOnlyList<DecodedCode> codes;
                    try
                    {
                        // Laden auf einem Pool-Thread: Main ist ein STA-Thread, und WinRT-Aufrufe (Windows) sollen nicht
                        // auf einen blockierten STA-Thread zurückmüssen
                        var image = Task.Run(() => ImageFile.LoadAsync(path, cts.Token), cts.Token).GetAwaiter().GetResult();
                        decoder ??= new BarcodeDecoder(formats);
                        codes = decoder.Decode(image, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"„{path}“ ließ sich nicht lesen: {ex.Message}");
                        failed = true;
                        continue;
                    }
                    // Ausgabe außerhalb: Bricht die Pipe ab (… | Select-Object -First 1), ist das kein Fehler dieses Bildes
                    foreach (var code in codes) Console.Out.WriteLine(CodeLine.Format(code));
                    if (codes.Count == 0) Console.Error.WriteLine($"Kein Code gefunden: {(IsPattern(input) ? Path.GetFileName(path) : input)}");
                    found += codes.Count;
                }
            }
            Console.Out.Flush();
            return failed ? ExitError : found > 0 ? ExitFound : ExitNothing;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>
    /// „QR_CODE,ean13“ → Formatnamen der Erkennung (ZXing-Schreibweise). Groß/klein, Bindestriche und Unterstriche sind
    /// egal („qr-code“, „EAN13“); „QR“, „MicroQR“ und „rMQR“ stehen für QR_CODE, MICRO_QR_CODE und RMQR_CODE.
    /// Leer = alle Formate (null).
    /// </summary>
    internal static IReadOnlyList<string>? ResolveFormats(IEnumerable<string> args)
    {
        var wanted = args.SelectMany(a => a.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();
        if (wanted.Count == 0) return null;

        var all = BarcodeDecoder.AllFormats;
        var result = new List<string>();
        foreach (var w in wanted)
        {
            var key = Simplify(w);
            if (key is "QR" or "MICROQR" or "RMQR") key += "CODE";
            var match = all.FirstOrDefault(f => Simplify(f) == key)
                ?? throw new UsageException($"Unbekanntes Format „{w}“. Möglich: {string.Join(", ", all)}.");
            if (!result.Contains(match)) result.Add(match);
        }
        return result;

        static string Simplify(string s) => new([.. s.ToUpperInvariant().Where(char.IsLetterOrDigit)]);
    }

    /// <summary>Eine Angabe zu Dateien: Pfad, oder Platzhalter im Dateinamen (*.png). Fehlt etwas, kommt die Meldung statt des Pfads.</summary>
    private static IEnumerable<(string Path, string? Problem)> Expand(string input)
    {
        string full;
        try { full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(input)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return [("", $"„{input}“ ist kein gültiger Pfad.")];
        }

        var name = Path.GetFileName(full);
        if (!IsPattern(name))
            return [File.Exists(full) ? (full, null) : ("", Directory.Exists(full) ? $"„{input}“ ist ein Ordner – bitte Bilder angeben, z. B. {Path.Combine(input, "*.png")}" : $"Datei nicht gefunden: {input}")];

        var folder = Path.GetDirectoryName(full);
        if (folder is null || !Directory.Exists(folder)) return [("", $"Ordner nicht gefunden: {Path.GetDirectoryName(input)}")];
        var options = new EnumerationOptions { MatchType = MatchType.Simple, IgnoreInaccessible = true, RecurseSubdirectories = false };
        var files = Directory.GetFiles(folder, name, options).Order(StringComparer.OrdinalIgnoreCase).ToList();
        return files.Count == 0 ? [("", $"Keine Datei passt zu „{input}“.")] : files.Select(f => (f, (string?)null));
    }

    private static bool IsPattern(string input) => Path.GetFileName(input).IndexOfAny(['*', '?']) >= 0;

    private static void PrintHelp()
    {
        string formats;
        try { formats = string.Join(", ", BarcodeDecoder.AllFormats); }
        catch (Exception) { formats = "QR_CODE, DATA_MATRIX, AZTEC, PDF_417, EAN_13, CODE_128 und weitere"; }
        var hotkey = HotkeyText.Format(Hotkey.Default);

        Console.WriteLine($$"""
            Lupe {{AppInfo.Version}} – QR- und Barcodes lesen

            Aufruf:
              Lupe.exe                              Oberfläche starten
              Lupe.exe lesen <bild> [<bild> …] [--format <format>]
              Lupe.exe --hintergrund                nur im Infobereich starten (für den Autostart)
              Lupe.exe --demo                       Oberfläche mit Demo-Kamera und Beispielverlauf
                                                    (speichert nichts, ohne globales Tastenkürzel)
              Lupe.exe --screenshots <ordner> [--theme hell|dunkel|beide]
              Lupe.exe --hilfe

            Bildschirm-Scan: {{hotkey}} (Standard, in den Einstellungen änderbar) liest einen
            Ausschnitt des Bildschirms – aus jedem Programm, solange Lupe läuft.

            „lesen“ sucht in jedem Bild alle Codes und schreibt je Code eine Zeile:
              FORMAT<Tab>Inhalt                     z. B. QR_CODE<Tab>https://example.org
            Steuerzeichen im Inhalt erscheinen maskiert: \n (Zeilenumbruch), \r, \t (Tabulator),
            andere als \xHH – etwa \x1D, das Trennzeichen in GS1-Codes. Hinweise („Kein Code
            gefunden“) und Fehler gehen nach stderr, die Ausgabe bleibt so gut weiterzuverarbeiten.

            Bilder: PNG, JPEG, BMP, GIF, TIFF und was Windows sonst lesen kann. Platzhalter im
            Dateinamen sind erlaubt, z. B. C:\Scans\*.jpg.

            Optionen für „lesen“:
              --format <format>   nur diese Formate suchen (mehrfach oder mit Komma getrennt),
                                  z. B. --format QR_CODE,EAN_13 (auch kurz: qr, ean13).
                                  Ohne Angabe: alle.
            Formate: {{formats}}

            Exitcodes:
              0  mindestens ein Code gefunden
              1  kein Code gefunden
              3  Fehler (Bild fehlt oder ist nicht lesbar, falscher Aufruf, Abbruch) – auch
                 wenn in anderen Bildern Codes gefunden wurden

            Beispiele:
              Lupe.exe lesen etikett.png
              Lupe.exe lesen C:\Scans\*.jpg --format QR_CODE > codes.txt     (cmd: UTF-8 mit BOM)

            Lupe.exe ist ein Fensterprogramm; damit die Eingabeaufforderung auf das Ende wartet:
              start /wait Lupe.exe lesen …        (cmd, Ausgabe in der Konsole)
              Lupe.exe lesen … | Out-Default      (PowerShell)
            Mit Umleitung (> codes.txt) ohne „start“ aufrufen – „start“ gibt die Umleitung nicht
            an Lupe weiter, die Datei bliebe leer. In .cmd-Dateien wartet cmd von selbst.
            """);
    }

    private sealed class UsageException(string message) : Exception(message);
}

/// <summary>Eine Ausgabezeile von <c>lesen</c>: FORMAT, Tabulator, Inhalt mit maskierten Steuerzeichen.</summary>
internal static class CodeLine
{
    public static string Format(DecodedCode code) => $"{code.Format}\t{Escape(code.Text)}";

    /// <summary>
    /// Steuerzeichen so maskieren, dass jeder Code genau eine Zeile bleibt: \n, \r, \t und \xHH. Der umgekehrte
    /// Schrägstrich selbst bleibt unverändert – WLAN-Codes (<c>WIFI:S:Netz\;…</c>) und Pfade bleiben so lesbar.
    /// </summary>
    public static string Escape(string text)
    {
        if (!text.Any(c => c < 0x20 || c == 0x7F)) return text;
        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\n': sb.Append(@"\n"); break;
                case '\r': sb.Append(@"\r"); break;
                case '\t': sb.Append(@"\t"); break;
                case < (char)0x20 or (char)0x7F: sb.Append(@"\x").Append(((int)c).ToString("X2", System.Globalization.CultureInfo.InvariantCulture)); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
