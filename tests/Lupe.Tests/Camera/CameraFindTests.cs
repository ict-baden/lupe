using Lupe.Core.Camera;


namespace Lupe.Tests.Camera;

/// <summary><see cref="Cameras.Find"/>: die gespeicherte Kamera (Kennung oder Name) in der aktuellen Liste wiederfinden.</summary>
public sealed class CameraFindTests
{
    private static readonly CameraInfo Laptop = new(@"\\?\USB#VID_04F2&PID_B6DD#MI_00#{e5323777}\GLOBAL", "Integrated Camera");
    private static readonly CameraInfo Logitech = new(@"\\?\USB#VID_046D&PID_0825#Grüße#{e5323777}\GLOBAL", "Logitech HD Webcam C270");
    private static readonly CameraInfo[] Both = [Laptop, Logitech];

    [Fact]
    public void Findet_nach_Kennung()
        => Assert.Same(Logitech, Cameras.Find(Both, Logitech.Id));

    [Fact]
    public void Kennung_ohne_Ruecksicht_auf_Gross_und_Kleinschreibung()
        => Assert.Same(Logitech, Cameras.Find(Both, Logitech.Id.ToLowerInvariant()));

    [Fact]
    public void Findet_nach_Namen_wenn_die_Kennung_sich_geaendert_hat()
        => Assert.Same(Logitech, Cameras.Find(Both, "  logitech hd webcam c270 "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ohne_Angabe_die_erste(string? key)
        => Assert.Same(Laptop, Cameras.Find(Both, key));

    [Fact]
    public void Gerade_nicht_angeschlossen_die_erste()
        => Assert.Same(Laptop, Cameras.Find(Both, "Dokumentenkamera"));

    [Fact]
    public void Leere_Liste_ergibt_null()
        => Assert.Null(Cameras.Find([], Logitech.Id));

    [Fact]
    public void Kennung_geht_vor_Namen()
    {
        // Eine Kamera heißt zufällig so wie die Kennung einer anderen
        var odd = new CameraInfo("andere-id", Logitech.Id);
        Assert.Same(Logitech, Cameras.Find([odd, Logitech], Logitech.Id));
    }

    [Fact]
    public void Null_als_Liste_ist_ein_Programmierfehler()
        => Assert.Throws<ArgumentNullException>(() => Cameras.Find(null!, "x"));
}
