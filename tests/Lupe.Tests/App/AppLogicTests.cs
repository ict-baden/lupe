using Lupe.Core.Screen;

namespace Lupe.Tests.App;

/// <summary>Reine Logik der App (ohne Fenster), direkt aus der gebauten Lupe.dll – siehe <see cref="AppInternals"/>.</summary>
public sealed class AppLogicTests
{
    // ================= Fensterlage (Auswahl mehrerer Codes) =================

    private static (int X, int Y) CenterOn(int x, int y, int width, int height, PixelRect workArea) =>
        ((int, int))AppInternals.Call("Lupe.Services.Placement", "CenterOn", x, y, width, height, workArea)!;

    [AppFact]
    public void Fenster_mittig_auf_der_Auswahl()
    {
        Assert.Equal((800, 350), CenterOn(1000, 500, 400, 300, new PixelRect(0, 0, 1920, 1040)));
    }

    [AppFact]
    public void Monitor_ueber_dem_Hauptmonitor_bleibt_bei_negativem_Y()
    {
        // Auswahl oben am Rand eines Monitors über dem Hauptmonitor: Das Fenster darf nicht auf Y = 0 (anderer Monitor) rutschen
        var top = new PixelRect(0, -1080, 1920, 1040);
        var (x, y) = CenterOn(960, -1000, 780, 630, top);
        Assert.Equal(570, x);
        Assert.Equal(-1080, y);
    }

    [AppFact]
    public void Monitor_links_vom_Hauptmonitor_bleibt_ganz_darauf()
    {
        var left = new PixelRect(-1920, 0, 1920, 1040);
        var (x, y) = CenterOn(-100, 500, 780, 630, left);
        Assert.Equal(-780, x); // rechter Rand genau an der Monitorgrenze, nicht darüber hinaus
        Assert.Equal(185, y);
    }

    [AppFact]
    public void Zu_grosses_Fenster_zeigt_die_linke_obere_Ecke()
    {
        Assert.Equal((0, 0), CenterOn(960, 520, 3000, 2000, new PixelRect(0, 0, 1920, 1040)));
        Assert.Equal((-2560, -300), CenterOn(-1280, 400, 3000, 2000, new PixelRect(-2560, -300, 2560, 1400)));
    }

    // ================= Formate aus den Einstellungen =================

    private static IReadOnlyList<string>? Usable(string[] wanted, string[]? known) =>
        (IReadOnlyList<string>?)AppInternals.Call("Lupe.Services.FormatFilter", "Usable", wanted, known);

    [AppFact]
    public void Leere_Formatliste_heisst_alle()
    {
        Assert.Null(Usable([], ["QR_CODE", "EAN_13"]));
    }

    [AppFact]
    public void Unbekannte_Formate_aus_der_Datei_fallen_weg()
    {
        Assert.Equal(["QR_CODE"], Usable(["qr_code", "GIBT_ES_NICHT"], ["QR_CODE", "EAN_13"]));
        Assert.Equal(["QR_CODE", "EAN_13"], Usable([" EAN_13 ", "QR_CODE"], ["QR_CODE", "EAN_13"]));
    }

    [AppFact]
    public void Nur_unbekannte_Formate_heisst_alle_statt_keins()
    {
        Assert.Null(Usable(["GIBT_ES_NICHT"], ["QR_CODE", "EAN_13"]));
    }

    [AppFact]
    public void Ohne_bekannte_Liste_bleiben_die_Formate_unveraendert()
    {
        Assert.Equal(["IRGENDWAS"], Usable(["IRGENDWAS"], null));
    }

    // ================= Ausgabezeile der Befehlszeile =================

    private static string Escape(string text) => (string)AppInternals.Call("Lupe.Cli.CodeLine", "Escape", text)!;

    [AppFact]
    public void Steuerzeichen_werden_maskiert_der_Schraegstrich_nicht()
    {
        Assert.Equal(@"a\r\nb\tc\x1Dd\x7F\x00e\", Escape("a\r\nb\tc\u001Dd\u007F\0e\\"));
    }

    [AppFact]
    public void Gewoehnlicher_Inhalt_bleibt_wie_er_ist()
    {
        Assert.Equal("https://example.org/a?b=1", Escape("https://example.org/a?b=1"));
        Assert.Equal(@"WIFI:T:WPA;S:Netz\;1;P:geheim;;", Escape(@"WIFI:T:WPA;S:Netz\;1;P:geheim;;"));
        Assert.Equal("Grüße 😀", Escape("Grüße 😀"));
    }

    // ================= Einzeilige Anzeige in Listen =================

    private static string OneLine(string text, int max) => (string)AppInternals.Call("Lupe.Labels", "OneLine", text, max)!;

    [AppFact]
    public void Einzeilig_mit_Zeilenumbruch_als_Zeichen_und_gekuerzt()
    {
        Assert.Equal("BEGIN⏎N:Muster", OneLine("BEGIN\r\nN:Muster", 400));
        Assert.Equal("a b·c", OneLine("a\tb\u001Dc", 400));
        Assert.Equal("abcde …", OneLine("abcdefgh", 5));
    }
}
