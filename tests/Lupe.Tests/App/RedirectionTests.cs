using System.Diagnostics;
using System.Text;

namespace Lupe.Tests.App;

/// <summary>
/// Umleitung in Dateien wie in cmd (<c>Lupe.exe … &gt; codes.txt 2&gt; fehler.txt</c>): UTF-8 mit BOM, das BOM nur in eine
/// leere Datei und nur, wenn wirklich etwas geschrieben wird. cmd wartet bei <c>/c</c> auf das Fensterprogramm und
/// reicht dessen Exitcode durch – so prüfen die Tests den echten Weg eines Skripts.
/// </summary>
public sealed class RedirectionTests
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>Führt <c>"Lupe.exe" &lt;args&gt; &lt;umleitung&gt;</c> über cmd aus; Exitcode von Lupe.</summary>
    internal static int RunInCmd(string arguments, string redirection)
    {
        var exe = AppExe.Path ?? throw new InvalidOperationException("Lupe.exe nicht gefunden.");
        var info = new ProcessStartInfo("cmd.exe")
        {
            // /s: die äußeren Anführungszeichen entfernt cmd, der Rest bleibt wörtlich
            Arguments = $"/d /s /c \"\"{exe}\" {arguments} {redirection}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(info)!;
        if (!process.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"cmd /c Lupe.exe {arguments} lief länger als 60 s.");
        }
        return process.ExitCode;
    }

    [AppFact]
    public void Ausgabe_in_Datei_ist_UTF8_mit_BOM_und_leeres_stderr_bleibt_leer()
    {
        using var temp = new TempFolder();
        string output = temp.File("hilfe.txt"), errors = temp.File("fehler.txt");

        Assert.Equal(0, RunInCmd("--hilfe", $"> \"{output}\" 2> \"{errors}\""));

        var bytes = File.ReadAllBytes(output);
        Assert.Equal(Bom, bytes.Take(3));
        var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, 3, bytes.Length - 3);
        Assert.Contains("Oberfläche starten", text);
        Assert.Contains("FORMAT<Tab>Inhalt", text);
        // Kein Hinweis, kein Fehler – also auch kein BOM in fehler.txt
        Assert.Equal(0, new FileInfo(errors).Length);
    }

    [AppFact]
    public void Fehler_landen_in_stderr_die_Ausgabe_bleibt_leer()
    {
        using var temp = new TempFolder();
        string output = temp.File("codes.txt"), errors = temp.File("fehler.txt");

        Assert.Equal(3, RunInCmd($"lesen \"{temp.File("gibt-es-nicht.png")}\"", $"> \"{output}\" 2> \"{errors}\""));

        Assert.Equal(0, new FileInfo(output).Length);
        var bytes = File.ReadAllBytes(errors);
        Assert.Equal(Bom, bytes.Take(3));
        Assert.Contains("Datei nicht gefunden", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
    }

    [AppFact]
    public void Anhaengen_schreibt_kein_BOM_mitten_in_die_Datei()
    {
        using var temp = new TempFolder();
        var log = temp.File("protokoll.txt");
        File.WriteAllText(log, "Lauf 1\r\n", new UTF8Encoding(false));

        Assert.Equal(0, RunInCmd("--hilfe", $">> \"{log}\""));

        var bytes = File.ReadAllBytes(log);
        Assert.Equal("Lauf 1\r\n"u8.ToArray(), bytes.Take(8));
        Assert.False(ContainsBom(bytes), "Beim Anhängen darf kein BOM in die Datei.");
        Assert.Contains("Oberfläche starten", Encoding.UTF8.GetString(bytes));
    }

    [AppFact]
    public void Ausgabe_und_Fehler_in_dieselbe_Datei_mit_einem_BOM_am_Anfang()
    {
        using var temp = new TempFolder();
        var log = temp.File("alles.txt");

        Assert.Equal(3, RunInCmd($"lesen \"{temp.File("a.png")}\" \"{temp.File("b.png")}\"", $"> \"{log}\" 2>&1"));

        var bytes = File.ReadAllBytes(log);
        Assert.Equal(Bom, bytes.Take(3));
        Assert.False(ContainsBom(bytes.Skip(3).ToArray()), "Nur ein BOM, am Anfang.");
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.Contains("a.png", text);
        Assert.Contains("b.png", text);
    }

    private static bool ContainsBom(byte[] bytes)
    {
        for (int i = 0; i + 2 < bytes.Length; i++)
            if (bytes[i] == 0xEF && bytes[i + 1] == 0xBB && bytes[i + 2] == 0xBF) return true;
        return false;
    }
}
