// SPDX-License-Identifier: MIT
using System.Text;
using Lupe.Core.Decoding;
using Lupe.Core.Input;
using Lupe.Core.Model;
using Lupe.Core.Resources;
using Lupe.Services;

namespace Lupe.Cli;

/// <summary>
/// Befehlszeile derselben Exe: <c>lesen</c> (auch <c>read</c>), <c>--hilfe</c>, <c>--version</c> und <c>--info</c>. Für
/// Skripte zählt der Exitcode (0 = mindestens ein Code gefunden, 1 = keiner, 3 = Fehler oder Abbruch).
/// </summary>
internal static class CommandLine
{
    public const int ExitFound = 0;
    public const int ExitNothing = 1;
    public const int ExitError = 3;

    private static readonly string[] HelpWords = ["--hilfe", "-hilfe", "hilfe", "--help", "help", "-h", "-?", "/?", "/h"];

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
            var lower = command.ToLowerInvariant();
            if (lower is "--version" or "--info")
            {
                if (args.Length > 1) throw new UsageException(string.Format(Strings.App_Cli_StandsAlone, command, CommandName));
                Console.WriteLine(lower == "--version" ? VersionLine() : InfoText());
                return ExitFound;
            }
            return lower switch
            {
                "lesen" or "read" => Read(args.Skip(1).ToArray()),
                "--screenshots" => throw new UsageException(string.Format(Strings.App_Cli_UsageScreenshots, CommandName)),
                "--demo" or "--hintergrund" or "--background" or "--bildschirm" or "--screen"
                    => throw new UsageException(string.Format(Strings.App_Cli_StandsAlone, command, CommandName)),
                _ => throw new UsageException(string.Format(Strings.App_Cli_UnknownCommand, command)),
            };
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine(string.Format(Strings.App_Cli_Error, ex.Message));
            Console.Error.WriteLine(string.Format(Strings.App_Cli_HelpHint, CommandName));
            return ExitError;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine(Strings.App_Cli_Cancelled);
            return ExitError;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(string.Format(Strings.App_Cli_Error, ex.Message));
            return ExitError;
        }
    }

    // ================= lesen =================

    private static int Read(string[] args) => Read(args, Console.Out, Console.Error, ImageFile.LoadAsync, formats => new BarcodeDecoder(formats));

    /// <summary>
    /// <c>lesen</c> mit austauschbarer Ausgabe, Bildquelle und Erkennung (Tests setzen hier etwa einen Ladefehler der
    /// Erkennung ein).
    /// </summary>
    internal static int Read(string[] args, TextWriter output, TextWriter error,
        Func<string, CancellationToken, Task<PixelImage>> load, Func<IReadOnlyList<string>?, BarcodeDecoder> createDecoder)
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
                    throw new UsageException(Strings.App_Cli_FormatNeedsValue);
                formatArgs.Add(args[++i]);
            }
            else if (arg.StartsWith("--format=", StringComparison.OrdinalIgnoreCase)) formatArgs.Add(arg["--format=".Length..]);
            else if (arg.StartsWith("--", StringComparison.Ordinal)) throw new UsageException(string.Format(Strings.App_Cli_UnknownOption, arg));
            else inputs.Add(arg);
        }
        if (inputs.Count == 0) throw new UsageException(string.Format(Strings.App_Cli_NoImage, CommandName, Strings.App_Cli_ExampleFile));

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
                        error.WriteLine(problem);
                        failed = true;
                        continue;
                    }
                    IReadOnlyList<DecodedCode> codes;
                    try
                    {
                        // Laden auf einem Pool-Thread: Main ist ein STA-Thread, und WinRT-Aufrufe (Windows) sollen nicht
                        // auf einen blockierten STA-Thread zurückmüssen
                        var image = Task.Run(() => load(path, cts.Token), cts.Token).GetAwaiter().GetResult();
                        decoder ??= createDecoder(formats);
                        codes = decoder.Decode(image, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (DecoderUnavailableException ex)
                    {
                        // Die Erkennung lädt auf diesem System nicht – kein „nichts gefunden“, und jedes weitere Bild
                        // scheiterte genauso: abbrechen mit Fehlercode und Exitcode 3
                        error.WriteLine(ex.Message);
                        return ExitError;
                    }
                    catch (Exception ex)
                    {
                        error.WriteLine(string.Format(Strings.App_Cli_ReadFailed, path, ex.Message));
                        failed = true;
                        continue;
                    }
                    // Ausgabe außerhalb: Bricht die Pipe ab (… | Select-Object -First 1), ist das kein Fehler dieses Bildes
                    foreach (var code in codes) output.WriteLine(CodeLine.Format(code));
                    if (codes.Count == 0) error.WriteLine(string.Format(Strings.App_Cli_NoCodeFound, IsPattern(input) ? Path.GetFileName(path) : input));
                    found += codes.Count;
                }
            }
            output.Flush();
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
                ?? throw new UsageException(string.Format(Strings.App_Cli_UnknownFormat, w, string.Join(", ", all)));
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
            return [("", string.Format(Strings.App_Cli_InvalidPath, input))];
        }

        var name = Path.GetFileName(full);
        if (!IsPattern(name))
            return [File.Exists(full) ? (full, null) : ("", Directory.Exists(full) ? string.Format(Strings.App_Cli_IsFolder, input, Path.Combine(input, "*.png")) : string.Format(Strings.App_Cli_FileNotFound, input))];

        var folder = Path.GetDirectoryName(full);
        if (folder is null || !Directory.Exists(folder)) return [("", string.Format(Strings.App_Cli_FolderNotFound, Path.GetDirectoryName(input)))];
        var options = new EnumerationOptions { MatchType = MatchType.Simple, IgnoreInaccessible = true, RecurseSubdirectories = false };
        var files = Directory.GetFiles(folder, name, options).Order(StringComparer.OrdinalIgnoreCase).ToList();
        return files.Count == 0 ? [("", string.Format(Strings.App_Cli_NoMatch, input))] : files.Select(f => (f, (string?)null));
    }

    private static bool IsPattern(string input) => Path.GetFileName(input).IndexOfAny(['*', '?']) >= 0;

    private static void PrintHelp()
    {
        string formats;
        try { formats = string.Join(", ", BarcodeDecoder.AllFormats); }
        catch (Exception) { formats = Strings.App_Cli_FormatsFallback; }
        var hotkey = HotkeyText.Format(Hotkey.PlatformDefault);

        Console.WriteLine(HelpText(AppInfo.Version, hotkey, formats));
    }

    /// <summary>Die Hilfe für dieses System: Befehlsname, Bildformate, Beispielpfad und (nur Windows) das Warten mit start /wait.</summary>
    internal static string HelpText(string version, string hotkey, string formats) => string.Format(Strings.App_Cli_Help, version, hotkey, formats,
        CommandName,
        OperatingSystem.IsWindows() ? Strings.App_Cli_ImagesWindows : OperatingSystem.IsMacOS() ? Strings.App_Cli_ImagesMac : Strings.App_Cli_ImagesLinux,
        ExamplePattern, OperatingSystem.IsWindows() ? Strings.App_Cli_WindowsWait : "", Strings.App_Cli_ExampleFile);

    /// <summary>
    /// So ruft man Lupe auf diesem System auf: Lupe.exe (Windows), lupe (Linux: .deb und install.sh legen den Befehl an),
    /// unter macOS das Programm im Bündel (es gibt keinen Befehl „lupe“ im PATH).
    /// </summary>
    internal static string CommandName => OperatingSystem.IsWindows() ? "Lupe.exe"
        : OperatingSystem.IsMacOS() ? MacCommand(Environment.ProcessPath)
        : "lupe";

    internal static string MacCommand(string? processPath) =>
        processPath is not null && processPath.Contains(".app/Contents/MacOS/", StringComparison.Ordinal) && !processPath.Contains("/AppTranslocation/", StringComparison.Ordinal)
            ? processPath
            : "/Applications/Lupe.app/Contents/MacOS/Lupe";

    /// <summary>Beispiel für Platzhalter im Dateinamen.</summary>
    internal static string ExamplePattern => OperatingSystem.IsWindows() ? @"C:\Scans\*.jpg" : "~/Scans/*.jpg";

    /// <summary>„Lupe 0.1.0 · Build abc1234 · .NET 10.0.1“ – für Fehlerberichte (<c>--version</c>).</summary>
    internal static string VersionLine() => "Lupe " + (AppInfo.Build.Length > 0
        ? string.Format(Strings.Page_Info_VersionBuild, AppInfo.Version, AppInfo.Build, Environment.Version.ToString(3))
        : string.Format(Strings.Page_Info_Version, AppInfo.Version, Environment.Version.ToString(3)));

    /// <summary>Die Angaben der Info-Seite als Text (<c>--info</c>): Version, Lizenz, Links, Datenschutz.</summary>
    internal static string InfoText() => string.Format(Strings.App_Cli_Info, VersionLine(), AppInfo.Copyright,
        Local(ThirdPartyNotices.LicenseFileName, AppInfo.LicenseUrl), Local(ThirdPartyNotices.FileName, AppInfo.RepositoryUrl + "/blob/main/THIRD-PARTY-NOTICES.txt"),
        AppInfo.RepositoryUrl, AppInfo.WebsiteUrl, AppInfo.ImprintUrl, AppInfo.PrivacyUrl, AppInfo.SupportUrl);

    /// <summary>Die Datei neben dem Programm, wenn es sie gibt (Pakete), sonst die Adresse im Repository.</summary>
    private static string Local(string fileName, string url)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        return File.Exists(path) ? path : url;
    }

    private sealed class UsageException(string message) : Exception(message);
}

/// <summary>Eine Ausgabezeile von <c>lesen</c>: FORMAT, Tabulator, Inhalt mit maskierten Steuerzeichen.</summary>
internal static class CodeLine
{
    public static string Format(DecodedCode code) => $"{code.Format}\t{Escape(code.Text)}";

    /// <summary>
    /// Steuerzeichen so maskieren, dass jeder Code genau eine Zeile bleibt: \n, \r, \t und \xHH (auch C1-Steuerzeichen
    /// U+0080–U+009F wie NEL), Zeilen- und Absatztrenner (U+2028, U+2029) und Zeichen zur Schreibrichtung (U+200E/F,
    /// U+202A–U+202E, U+2066–U+2069, U+061C) als \uXXXX – sie könnten eine Zeile teilen oder die Anzeige im Terminal
    /// verdrehen. Der umgekehrte Schrägstrich selbst bleibt unverändert – WLAN-Codes (<c>WIFI:S:Netz\;…</c>) und Pfade
    /// bleiben so lesbar.
    /// </summary>
    public static string Escape(string text)
    {
        if (!text.Any(NeedsEscape)) return text;
        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\n': sb.Append(@"\n"); break;
                case '\r': sb.Append(@"\r"); break;
                case '\t': sb.Append(@"\t"); break;
                case < (char)0x20 or (>= (char)0x7F and <= (char)0x9F):
                    sb.Append(@"\x").Append(((int)c).ToString("X2", System.Globalization.CultureInfo.InvariantCulture)); break;
                case var other when NeedsEscape(other):
                    sb.Append(@"\u").Append(((int)other).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static bool NeedsEscape(char c) =>
        c < 0x20 || c is >= (char)0x7F and <= (char)0x9F
        || c is '\u2028' or '\u2029' or '\u200E' or '\u200F' or '\u061C'
        || c is >= '\u202A' and <= '\u202E' || c is >= '\u2066' and <= '\u2069';
}
