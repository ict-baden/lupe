// SPDX-License-Identifier: MIT
using Lupe.Tests.TestSupport;
using System.Text;
using Lupe.Core.Settings;

namespace Lupe.Tests.Settings;

/// <summary>Atomares Schreiben (Temp-Datei + Ersetzen) mit echten Dateien und echten Sperren.</summary>
public sealed class AtomicFileTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private string FilePath => _temp.File("datei.json");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Unter_Linux_und_macOS_nur_fuer_den_Benutzer_lesbar()
    {
        if (OperatingSystem.IsWindows()) return; // Windows: Rechte erbt der Ordner %LocalAppData%\Lupe
        var path = _temp.File("neu", "verlauf.json");

        AtomicFile.Write(path, "geheim"u8.ToArray());

        Assert.Equal(AtomicFile.OwnerOnlyFolder, File.GetUnixFileMode(Path.GetDirectoryName(path)!));
        Assert.Equal(AtomicFile.OwnerOnlyFile, File.GetUnixFileMode(path));
        // Auch beim Ersetzen einer vorhandenen, lesbaren Datei
        File.SetUnixFileMode(path, AtomicFile.OwnerOnlyFile | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        AtomicFile.Write(path, "neu"u8.ToArray());
        Assert.Equal(AtomicFile.OwnerOnlyFile, File.GetUnixFileMode(path));
    }

    [Fact]
    public void Export_folgt_der_umask_und_behaelt_die_Rechte_einer_vorhandenen_Datei()
    {
        if (OperatingSystem.IsWindows()) return;
        var reference = _temp.File("vergleich.csv");
        File.WriteAllText(reference, "x"); // neue Datei wie jedes Programm: 0666 ohne umask
        var path = _temp.File("export.csv");

        AtomicFile.Write(path, "neu"u8.ToArray(), ownerOnly: false);
        Assert.Equal(File.GetUnixFileMode(reference), File.GetUnixFileMode(path));

        // Eine vorhandene Datei (z. B. für die Gruppe freigegeben) behält beim Überschreiben ihre Rechte
        var shared = AtomicFile.OwnerOnlyFile | UnixFileMode.GroupRead | UnixFileMode.GroupWrite;
        File.SetUnixFileMode(path, shared);
        AtomicFile.Write(path, "noch neuer"u8.ToArray(), ownerOnly: false);
        Assert.Equal(shared, File.GetUnixFileMode(path));
        Assert.Equal("noch neuer", File.ReadAllText(path));
    }

    [Fact]
    public void Schreibt_neue_Datei_und_legt_Ordner_an()
    {
        var path = _temp.File("a", "b", "c.json");

        AtomicFile.Write(path, "hallo"u8.ToArray());

        Assert.Equal("hallo", File.ReadAllText(path));
    }

    [Fact]
    public void Ersetzt_vorhandene_Datei_vollstaendig()
    {
        File.WriteAllText(FilePath, new string('x', 100_000));

        AtomicFile.Write(FilePath, "kurz"u8.ToArray());

        Assert.Equal("kurz", File.ReadAllText(FilePath));
        Assert.Equal(["datei.json"], _temp.FileNames());
    }

    [Fact]
    public void Fehler_mitten_im_Schreiben_laesst_die_alte_Datei_unveraendert()
    {
        File.WriteAllText(FilePath, "alt und heil");

        var ex = Assert.Throws<InvalidOperationException>(() => AtomicFile.Write(FilePath, stream =>
        {
            stream.Write(Encoding.UTF8.GetBytes(new string('n', 50_000)));
            throw new InvalidOperationException("Abbruch mitten im Schreiben");
        }));

        Assert.Equal("Abbruch mitten im Schreiben", ex.Message);
        Assert.Equal("alt und heil", File.ReadAllText(FilePath));
        Assert.Equal(["datei.json"], _temp.FileNames()); // Temp-Datei weggeräumt
    }

    [WindowsFact]
    public void Dauerhaft_gesperrte_Zieldatei_wirft_und_bleibt_heil()
    {
        File.WriteAllText(FilePath, "alt");

        Exception ex;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ex = Assert.ThrowsAny<Exception>(() => AtomicFile.Write(FilePath, "neu"u8.ToArray()));
        }

        Assert.True(ex is IOException or UnauthorizedAccessException, ex.GetType().Name);
        Assert.Equal("alt", File.ReadAllText(FilePath));
        Assert.Equal(["datei.json"], _temp.FileNames());
        Assert.Contains("anderen Programm", FileProblems.Describe(ex));
    }

    [Fact]
    public void Kurze_Sperre_wird_abgewartet()
    {
        File.WriteAllText(FilePath, "alt");
        var locked = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        var release = new Thread(() =>
        {
            Thread.Sleep(50);
            locked.Dispose();
        });
        release.Start();

        AtomicFile.Write(FilePath, "neu"u8.ToArray());
        release.Join();

        Assert.Equal("neu", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Lesen_sperrt_andere_nicht_aus()
    {
        File.WriteAllText(FilePath, "inhalt");
        // Ein anderes Programm hat die Datei zum Schreiben offen (und erlaubt Lesen)
        using var writer = new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Equal("inhalt", Encoding.UTF8.GetString(AtomicFile.ReadAllBytes(FilePath)));
    }

    [Fact]
    public void Lesen_einer_fehlenden_Datei_meldet_FileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() => AtomicFile.ReadAllBytes(_temp.File("fehlt.json")));
        Assert.Throws<DirectoryNotFoundException>(() => AtomicFile.ReadAllBytes(_temp.File("fehlt", "auch.json")));
    }

    [Fact]
    public void Lesen_einer_riesigen_Datei_wird_abgelehnt()
    {
        using (var stream = new FileStream(FilePath, FileMode.Create))
        {
            stream.SetLength(AtomicFile.MaxReadBytes + 1); // dünn besetzt, belegt kaum Platz
        }

        var ex = Assert.Throws<IOException>(() => AtomicFile.ReadAllBytes(FilePath));

        Assert.Contains("ungewöhnlich groß", ex.Message);
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException), "Zugriff verweigert")]
    [InlineData(typeof(DirectoryNotFoundException), "Ordner nicht gefunden")]
    [InlineData(typeof(FileNotFoundException), "Datei nicht gefunden")]
    [InlineData(typeof(PathTooLongException), "Pfad zu lang")]
    public void Fehler_werden_deutsch_beschrieben(Type type, string expected)
    {
        var ex = (Exception)Activator.CreateInstance(type)!;

        Assert.Contains(expected, FileProblems.Describe(ex));
        Assert.True(FileProblems.IsExpected(ex));
    }

    [Theory]
    [InlineData(0x80070070, "Kein Platz mehr auf dem Datenträger")]
    [InlineData(0x80070027, "Kein Platz mehr auf dem Datenträger")]
    [InlineData(0x80070013, "schreibgeschützt")]
    [InlineData(0x80070015, "nicht bereit")]
    [InlineData(0x80070035, "Netzwerkpfad")]
    [InlineData(0x80070043, "Netzwerkpfad")]
    [InlineData(0x8007007B, "Ungültiger Datei- oder Ordnername")]
    [InlineData(0x80070020, "anderen Programm")]
    [InlineData(0x80070021, "anderen Programm")]
    public void Typische_Systemfehler_werden_deutsch_beschrieben(uint hresult, string expected)
    {
        var ex = new IOException("english system text", unchecked((int)hresult));

        Assert.Contains(expected, FileProblems.Describe(ex));
        Assert.True(FileProblems.IsExpected(ex));
    }

    [Fact]
    public void Unbekannter_Systemfehler_behaelt_die_Meldung_des_Systems()
    {
        Assert.Equal("etwas Seltenes", FileProblems.Describe(new IOException("etwas Seltenes", unchecked((int)0x80070299))));
        Assert.Contains("kürzer", FileProblems.Describe(new EndOfStreamException()));
    }

    [Fact]
    public void Programmierfehler_sind_keine_erwarteten_Dateiprobleme()
    {
        Assert.False(FileProblems.IsExpected(new InvalidOperationException()));
        Assert.False(FileProblems.IsExpected(new NullReferenceException()));
    }
}
