// SPDX-License-Identifier: MIT
using Lupe.Cli;
using Lupe.Core.Decoding;
using Lupe.Core.Model;
using Lupe.Tests.Decoding;
using Lupe.Tests.TestSupport;

namespace Lupe.Tests.Cli;

/// <summary>
/// <c>lesen</c> im eigenen Prozess, mit eingesetzter Bildquelle und Erkennung: Exitcodes, Ausgabe und der Abbruch, wenn
/// die Erkennung nicht lädt (LUP-0001). Läuft auf jedem System.
/// </summary>
public sealed class CommandLineReadTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("lupe-cli-");
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();
    private int _loaded;

    public void Dispose()
    {
        try { _folder.Delete(recursive: true); }
        catch (IOException) { }
    }

    /// <summary>Zwei vorhandene Dateien; ihr Inhalt zählt nicht, die Bildquelle liefert <paramref name="image"/>.</summary>
    private int Read(PixelImage image, Func<IReadOnlyList<string>?, BarcodeDecoder> createDecoder)
    {
        var first = Path.Combine(_folder.FullName, "a.png");
        var second = Path.Combine(_folder.FullName, "b.png");
        File.WriteAllBytes(first, [0]);
        File.WriteAllBytes(second, [0]);
        return CommandLine.Read([first, second], _out, _err,
            (_, _) => { Interlocked.Increment(ref _loaded); return Task.FromResult(image); }, createDecoder);
    }

    private string[] ErrorLines => _err.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [Theory]
    [InlineData("DllNotFound")]
    [InlineData("TypeInitialization")]
    public void Ladefehler_meldet_LUP_0001_mit_Exitcode_3_und_liest_kein_weiteres_Bild(string kind)
    {
        int exit = Read(TestCodes.Render("Hallo", BarcodeFormat.QR_CODE), formats => new BarcodeDecoder(formats)
        {
            NativeProblem = () => null,
            Reader = (_, _) => throw ZXingLibraryTests.LoadFailure(kind, "libZXing.so: version `GLIBC_2.38' not found"),
        });

        Assert.Equal(CommandLine.ExitError, exit);
        Assert.NotEqual(CommandLine.ExitNothing, exit);
        Assert.Equal(1, _loaded); // das zweite Bild wird nicht mehr gelesen
        Assert.Equal("", _out.ToString());
        var line = Assert.Single(ErrorLines);
        Assert.StartsWith("LUP-0001: ", line);
        Assert.Contains("GLIBC_2.38", line);
    }

    [Fact]
    public void Ladefehler_aus_der_Vorabpruefung_ebenso()
    {
        int exit = Read(TestCodes.Render("Hallo", BarcodeFormat.QR_CODE),
            formats => new BarcodeDecoder(formats) { NativeProblem = () => ZXingLibrary.Message("version `GLIBC_2.38' not found") });

        Assert.Equal(CommandLine.ExitError, exit);
        Assert.Equal(1, _loaded);
        Assert.StartsWith("LUP-0001: ", Assert.Single(ErrorLines));
    }

    [Fact]
    public void Mit_ladbarer_Erkennung_beide_Bilder_und_Exitcode_0()
    {
        int exit = Read(TestCodes.Render("Hallo", BarcodeFormat.QR_CODE), formats => new BarcodeDecoder(formats));

        Assert.Equal(CommandLine.ExitFound, exit);
        Assert.Equal(2, _loaded);
        Assert.Equal(["QR_CODE\tHallo", "QR_CODE\tHallo"], _out.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Assert.Empty(ErrorLines);
    }

    [Fact]
    public void Nichts_gefunden_ist_Exitcode_1_ohne_Fehlercode()
    {
        var blank = new PixelImage(64, 64, Enumerable.Repeat((byte)255, 64 * 64 * 4).ToArray());
        int exit = Read(blank, formats => new BarcodeDecoder(formats));

        Assert.Equal(CommandLine.ExitNothing, exit);
        Assert.Equal(2, _loaded);
        Assert.Equal(2, ErrorLines.Length);
        Assert.DoesNotContain(ErrorLines, l => l.StartsWith("LUP-", StringComparison.Ordinal));
    }
}
