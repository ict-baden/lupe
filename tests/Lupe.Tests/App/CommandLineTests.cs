using Lupe.Tests.TestSupport;
using ZXing;

namespace Lupe.Tests.App;

/// <summary>
/// Befehlszeile der App (<c>Lupe.exe lesen …</c>, <c>--hilfe</c>), als echter Prozess wie aus einem Skript.
/// Exitcodes: 0 = mindestens ein Code, 1 = keiner, 3 = Fehler.
/// </summary>
public sealed class CommandLineTests
{
    // ================= ohne Erkennung =================

    [AppFact]
    public void Hilfe_beschreibt_Befehle_und_Exitcodes()
    {
        var r = AppExe.Run("--hilfe");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("lesen <bild>", r.Out);
        Assert.Contains("FORMAT<Tab>Inhalt", r.Out);
        Assert.Contains("--screenshots", r.Out);
        Assert.Contains("Exitcodes", r.Out);
        Assert.Contains("Win+Umschalt+Q", r.Out); // Standard-Tastenkürzel für den Bildschirm-Scan
        Assert.Equal("", r.Err);
    }

    [AppFact]
    public void Hilfe_auch_mit_Fragezeichen() => Assert.Equal(0, AppExe.Run("/?").ExitCode);

    [AppFact]
    public void Unbekannter_Befehl_ist_Fehler()
    {
        var r = AppExe.Run("zaubern");
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("Unbekannter Befehl", r.Err);
        Assert.Contains("--hilfe", r.Err);
        Assert.Equal("", r.Out);
    }

    [AppFact]
    public void Lesen_ohne_Bild_ist_Fehler()
    {
        var r = AppExe.Run("lesen");
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("mindestens ein Bild", r.Err);
    }

    [AppFact]
    public void Lesen_mit_unbekannter_Option_ist_Fehler()
    {
        var r = AppExe.Run("lesen", "bild.png", "--schnell");
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("Unbekannte Option", r.Err);
    }

    [AppFact]
    public void Format_ohne_Wert_ist_Fehler()
    {
        var r = AppExe.Run("lesen", "bild.png", "--format");
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("braucht einen Wert", r.Err);
    }

    [AppFact]
    public void Fehlende_Datei_ist_Fehler()
    {
        using var temp = new TempFolder();
        var r = AppExe.Run("lesen", temp.File("gibt-es-nicht.png"));
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("Datei nicht gefunden", r.Err);
        Assert.Equal("", r.Out);
    }

    [AppFact]
    public void Ordner_statt_Bild_ist_Fehler()
    {
        using var temp = new TempFolder();
        var r = AppExe.Run("lesen", temp.Path);
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("ist ein Ordner", r.Err);
    }

    [AppFact]
    public void Platzhalter_ohne_Treffer_ist_Fehler()
    {
        using var temp = new TempFolder();
        var r = AppExe.Run("lesen", temp.File("*.png"));
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("Keine Datei passt", r.Err);
    }

    [AppFact]
    public void Screenshots_ohne_Ordner_ist_Fehler()
    {
        var r = AppExe.Run("--screenshots");
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("--screenshots <ordner>", r.Err);
    }

    [AppFact]
    public void Demo_mit_weiteren_Angaben_ist_Fehler()
    {
        // „--demo“ allein startet die Oberfläche – mit Zusatz erklärt die Befehlszeile den Aufruf (kein Fenster)
        var r = AppExe.Run("--demo", "x");
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("steht allein", r.Err);
    }

    // ================= mit Erkennung (nach dem Zusammenführen) =================

    [AppCoreFact]
    public void Liest_QR_Code_als_Zeile_mit_Format_und_Inhalt()
    {
        using var temp = new TempFolder();
        var file = temp.File("qr.bmp");
        AppExe.WriteBmp(TestCodes.Embed(TestCodes.Render("https://example.org/lupe?id=42"), 800, 600, 250, 150), file);

        var r = AppExe.Run("lesen", file);
        Assert.Equal(0, r.ExitCode);
        Assert.Equal(["QR_CODE\thttps://example.org/lupe?id=42"], r.Lines);
    }

    [AppCoreFact]
    public void Mehrere_Codes_in_einem_Bild_je_eine_Zeile()
    {
        using var temp = new TempFolder();
        var canvas = TestCodes.Embed(TestCodes.Render("ERSTER"), 1200, 700, 60, 60);
        var ean = TestCodes.Render("4012345000009", BarcodeFormat.EAN_13, 360, 160, 10);
        canvas = Overlay(canvas, ean, 700, 420);
        var file = temp.File("zwei.bmp");
        AppExe.WriteBmp(canvas, file);

        var r = AppExe.Run("lesen", file);
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("QR_CODE\tERSTER", r.Lines);
        Assert.Contains("EAN_13\t4012345000009", r.Lines);
        Assert.Equal(2, r.Lines.Length);
    }

    [AppCoreFact]
    public void Steuerzeichen_im_Inhalt_werden_maskiert()
    {
        using var temp = new TempFolder();
        var file = temp.File("zeilen.bmp");
        AppExe.WriteBmp(TestCodes.Render("Zeile 1\nZeile 2\tEnde\u001D!", width: 400, height: 400), file);

        var r = AppExe.Run("lesen", file);
        Assert.Equal(0, r.ExitCode);
        Assert.Equal(["QR_CODE\tZeile 1\\nZeile 2\\tEnde\\x1D!"], r.Lines);
    }

    [AppCoreFact]
    public void Bild_ohne_Code_ergibt_Exitcode_1()
    {
        using var temp = new TempFolder();
        var file = temp.File("leer.bmp");
        AppExe.WriteBmp(TestCodes.Embed(TestCodes.Render("x"), 400, 300, 1000, 1000), file);

        var r = AppExe.Run("lesen", file);
        Assert.Equal(1, r.ExitCode);
        Assert.Equal("", r.Out);
        Assert.Contains("Kein Code gefunden", r.Err);
    }

    [AppCoreFact]
    public void Format_filtert_und_versteht_Kurzformen()
    {
        using var temp = new TempFolder();
        var file = temp.File("qr.bmp");
        AppExe.WriteBmp(TestCodes.Render("NUR QR"), file);

        Assert.Equal(1, AppExe.Run("lesen", file, "--format", "EAN_13").ExitCode);
        Assert.Equal(0, AppExe.Run("lesen", file, "--format", "qr").ExitCode);
        Assert.Equal(0, AppExe.Run("lesen", file, "--format=ean13,qr-code").ExitCode);
    }

    [AppCoreFact]
    public void Unbekanntes_Format_ist_Fehler_und_nennt_die_moeglichen()
    {
        using var temp = new TempFolder();
        var file = temp.File("qr.bmp");
        AppExe.WriteBmp(TestCodes.Render("X"), file);

        var r = AppExe.Run("lesen", file, "--format", "KEINS");
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("Unbekanntes Format", r.Err);
        Assert.Contains("QR_CODE", r.Err);
    }

    [AppCoreFact]
    public void Platzhalter_liest_alle_passenden_Dateien_sortiert()
    {
        using var temp = new TempFolder();
        AppExe.WriteBmp(TestCodes.Render("B-ZWEI"), temp.File("b.bmp"));
        AppExe.WriteBmp(TestCodes.Render("A-EINS"), temp.File("a.bmp"));
        File.WriteAllText(temp.File("notiz.txt"), "kein Bild");

        var r = AppExe.Run("lesen", temp.File("*.bmp"));
        Assert.Equal(0, r.ExitCode);
        Assert.Equal(["QR_CODE\tA-EINS", "QR_CODE\tB-ZWEI"], r.Lines);
    }

    [AppCoreFact]
    public void Fehler_bei_einem_Bild_ergibt_3_auch_wenn_andere_Codes_haben()
    {
        using var temp = new TempFolder();
        AppExe.WriteBmp(TestCodes.Render("GUT"), temp.File("gut.bmp"));
        File.WriteAllText(temp.File("kaputt.png"), "das ist kein PNG");

        var r = AppExe.Run("lesen", temp.File("gut.bmp"), temp.File("kaputt.png"));
        Assert.Equal(3, r.ExitCode);
        Assert.Equal(["QR_CODE\tGUT"], r.Lines);
        Assert.Contains("kaputt.png", r.Err);
    }

    /// <summary>Kopiert ein Bild an eine Stelle eines anderen (gleiches BGRA-Format).</summary>
    private static Core.Model.PixelImage Overlay(Core.Model.PixelImage canvas, Core.Model.PixelImage image, int x, int y)
    {
        var px = (byte[])canvas.Bgra.Clone();
        for (int row = 0; row < image.Height && y + row < canvas.Height; row++)
            Buffer.BlockCopy(image.Bgra, row * image.Stride, px, (y + row) * canvas.Stride + x * 4, Math.Min(image.Width, canvas.Width - x) * 4);
        return new Core.Model.PixelImage(canvas.Width, canvas.Height, px, canvas.Stride);
    }
}
