// SPDX-License-Identifier: MIT
using Lupe.Core.Decoding;
using Lupe.Tests.TestSupport;

namespace Lupe.Tests.Decoding;

/// <summary>
/// Lädt die native zxing-cpp-Bibliothek nicht (zu altes System, fehlende Laufzeit, kaputte Datei), ist das ein Fehler mit
/// Fehlercode LUP-0001 – kein „Kein Code gefunden“ und kein Absturz im Finalizer.
/// </summary>
public sealed class ZXingLibraryTests
{
    private static readonly System.Reflection.Assembly Binding = typeof(ZXingCpp.BarcodeReader).Assembly;

    [Fact]
    public void Bibliothek_laedt_und_hat_alle_Funktionen_die_ZXingCpp_aufruft()
    {
        Assert.Null(ZXingLibrary.Problem);
        Assert.Null(BarcodeDecoder.UnavailableReason);
        // Die Liste stammt aus den DllImport-Angaben von ZXingCpp – leer hieße, die Prüfung prüfte nichts
        var imports = ZXingLibrary.ImportsOf(Binding, ZXingLibrary.Name);
        Assert.Contains("ZXing_ReadBarcodes", imports);
        Assert.Contains("ZXing_ReaderOptions_new", imports);
        Assert.True(imports.Count > 50, $"nur {imports.Count} Funktionen gefunden");
    }

    [Fact]
    public void Fehlende_Bibliothek_meldet_Fehlercode_und_Grund()
    {
        var problem = ZXingLibrary.Probe("ZXing-gibt-es-nicht", Binding);
        Assert.NotNull(problem);
        Assert.StartsWith("LUP-0001: ", problem);
        Assert.Contains("ZXing-gibt-es-nicht", problem); // der Grund des Systems steht dabei
        Assert.Contains("Erkennungsbibliothek", problem);
    }

    [Fact]
    public void Kaputte_Bibliothek_meldet_Fehlercode()
    {
        var file = Path.Combine(Path.GetTempPath(), $"lupe-kaputt-{Guid.NewGuid():N}" +
            (OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so"));
        File.WriteAllBytes(file, [.. Enumerable.Range(0, 4096).Select(i => (byte)(i * 31))]);
        try
        {
            var problem = ZXingLibrary.ProbeFile(file);
            Assert.NotNull(problem);
            Assert.StartsWith("LUP-0001: ", problem);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Fremde_Bibliothek_ohne_die_Funktionen_meldet_Fehlercode_und_Namen()
    {
        // Eine Bibliothek, die lädt, aber nicht zu ZXingCpp passt (z. B. eine ältere zxing-cpp-Version): hier die C-Bibliothek
        var system = OperatingSystem.IsWindows() ? "kernel32.dll" : OperatingSystem.IsMacOS() ? "/usr/lib/libSystem.B.dylib" : "libc.so.6";
        var problem = ZXingLibrary.Probe(system, Binding, ZXingLibrary.Name);
        Assert.NotNull(problem);
        Assert.StartsWith("LUP-0001: ", problem);
        Assert.Contains("ZXing_", problem);
    }

    [Fact]
    public void Decoder_wirft_statt_nichts_gefunden()
    {
        var image = TestCodes.Render("Hallo", BarcodeFormat.QR_CODE);
        var decoder = new BarcodeDecoder { NativeProblem = () => ZXingLibrary.Message("version `GLIBC_2.38' not found") };
        var ex = Assert.Throws<DecoderUnavailableException>(() => decoder.Decode(image));
        Assert.Equal("LUP-0001", ex.Code);
        Assert.StartsWith("LUP-0001: ", ex.Message);
        Assert.Contains("GLIBC_2.38", ex.Message);
        // Ohne den Ladefehler liest derselbe Decoder den Code
        Assert.Equal("Hallo", Assert.Single(new BarcodeDecoder().Decode(image)).Text);
    }

    [Theory]
    [InlineData("DllNotFound")]
    [InlineData("EntryPointNotFound")]
    [InlineData("BadImageFormat")]
    [InlineData("TypeInitialization")]
    public void Ladefehler_mitten_im_parallelen_Lesen_kommt_unverpackt_an(string kind)
    {
        // Die Vorabprüfung bestand, der Fehler kommt erst in einer der parallelen Aufgaben (Parallel.ForEach verpackt ihn
        // in eine AggregateException) – heraus kommt trotzdem DecoderUnavailableException selbst
        var image = TestCodes.Render("Hallo", BarcodeFormat.QR_CODE);
        int calls = 0;
        var decoder = new BarcodeDecoder
        {
            NativeProblem = () => null,
            Reader = (_, _) =>
            {
                Interlocked.Increment(ref calls);
                throw LoadFailure(kind, "libZXing.so: version `GLIBC_2.38' not found");
            },
        };
        var ex = Assert.Throws<DecoderUnavailableException>(() => decoder.Decode(image));
        Assert.Equal("LUP-0001", ex.Code);
        Assert.StartsWith("LUP-0001: ", ex.Message);
        Assert.Contains("GLIBC_2.38", ex.Message);
        Assert.True(calls >= 1);
    }

    [Fact]
    public void Anderer_Fehler_im_Lesevorgang_kostet_nur_dessen_Ergebnis()
    {
        var image = TestCodes.Render("Hallo", BarcodeFormat.QR_CODE);
        var decoder = new BarcodeDecoder { NativeProblem = () => null, Reader = (_, _) => throw new InvalidOperationException("zu groß") };
        Assert.Empty(decoder.Decode(image));
    }

    internal static Exception LoadFailure(string kind, string message) => kind switch
    {
        "DllNotFound" => new DllNotFoundException(message),
        "EntryPointNotFound" => new EntryPointNotFoundException(message),
        "BadImageFormat" => new BadImageFormatException(message),
        "TypeInitialization" => new TypeInitializationException("ZXingCpp.Dll", new DllNotFoundException(message)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    [Theory]
    [InlineData(typeof(DllNotFoundException), true)]
    [InlineData(typeof(EntryPointNotFoundException), true)]
    [InlineData(typeof(BadImageFormatException), true)]
    [InlineData(typeof(InvalidOperationException), false)]
    [InlineData(typeof(ArgumentException), false)]
    public void Ladefehler_im_Lesevorgang_gelten_fuer_alle_Lesevorgaenge(Type type, bool loadFailure)
    {
        var ex = (Exception)Activator.CreateInstance(type, "x")!;
        Assert.Equal(loadFailure, BarcodeDecoder.IsLoadFailure(ex));
        // Auch verpackt in den Typinitialisierer einer Klasse von ZXingCpp
        Assert.Equal(loadFailure, BarcodeDecoder.IsLoadFailure(new TypeInitializationException("ZXingCpp.Dll", ex)));
    }
}
