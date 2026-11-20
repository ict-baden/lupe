// SPDX-License-Identifier: MIT
namespace Lupe.Tests.App;

/// <summary>
/// <c>Lupe.exe --screenshots</c> rendert alle Seiten, das Auswahl-Overlay und den Ergebnis-Hinweis per
/// RenderTargetBitmap – ohne sichtbaren Desktop, also auch in einer Dienst-Sitzung. Die Kamera-Seite zeigt dabei einen
/// echt erkannten Code (Demo-Bild), die Datei-Seite einen Lieferschein mit mehreren Codes.
/// </summary>
public sealed class ScreenshotTests
{
    [AppFact]
    public void Rendert_alle_Seiten_Overlay_und_Hinweis()
    {
        using var temp = new TempFolder();
        var r = AppExe.Run(TimeSpan.FromMinutes(3), "--screenshots", temp.Path, "--theme", "hell");
        Assert.True(r.ExitCode == 0, $"Exitcode {r.ExitCode}: {r.Err}");
        Assert.Contains("8 Bilder geschrieben", r.Out);

        // 1100 × 740 Punkte bei 144 dpi
        foreach (var slug in new[] { "kamera", "bildschirm", "datei", "verlauf", "einstellungen", "info" })
        {
            var file = temp.File($"{slug}-hell.png");
            Assert.True(File.Exists(file), $"{slug}-hell.png fehlt");
            Assert.Equal((1650, 1110), AppExe.PngSize(file));
        }
        // Overlay in Bildpixeln des Demo-Desktops, Hinweis in seiner eigenen Größe
        Assert.Equal((1600, 900), AppExe.PngSize(temp.File("auswahl-hell.png")));
        Assert.True(File.Exists(temp.File("treffer-hell.png")));
        Assert.False(File.Exists(temp.File("kamera-dunkel.png")), "Nur das helle Theme war verlangt.");
    }

    [AppFact]
    public void Overlay_Bild_enthaelt_den_lesbaren_Code_der_Demo_Rechnung()
    {
        // Ende-zu-Ende: Was das Overlay zeigt, liest die Befehlszeile derselben Exe wieder
        using var temp = new TempFolder();
        Assert.Equal(0, AppExe.Run(TimeSpan.FromMinutes(3), "--screenshots", temp.Path, "--theme", "dunkel").ExitCode);
        var r = AppExe.Run("lesen", temp.File("auswahl-dunkel.png"), "--format", "QR_CODE");
        Assert.Equal(0, r.ExitCode);
        Assert.Equal(["QR_CODE\thttps://example.org/rechnung/2026-118"], r.Lines);
    }

    [AppFact]
    public void Unbekanntes_Theme_ist_Fehler()
    {
        using var temp = new TempFolder();
        var r = AppExe.Run("--screenshots", temp.Path, "--theme", "bunt");
        Assert.Equal(3, r.ExitCode);
        Assert.Empty(Directory.GetFiles(temp.Path));
    }
}
