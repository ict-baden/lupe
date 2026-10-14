using System.Diagnostics;
using Lupe.Core.Input;

namespace Lupe.Tests.Input;

/// <summary>Erkennung von Zielfenstern mit höheren Rechten (UIPI verwirft dorthin Eingaben, ohne Fehler zu melden).</summary>
public sealed class ElevationTests
{
    private const int Low = 0x1000, Medium = 0x2000, MediumPlus = 0x2100, High = 0x3000, SystemLevel = 0x4000;

    [Theory]
    [InlineData(Medium, false, High, true)]         // Lupe normal, Ziel als Administrator
    [InlineData(Medium, false, SystemLevel, true)]
    [InlineData(Medium, false, MediumPlus, true)]
    [InlineData(Low, false, Medium, true)]
    [InlineData(Medium, false, Medium, false)]      // gleiche Stufe
    [InlineData(High, false, Medium, false)]        // Lupe als Administrator darf nach unten
    [InlineData(High, false, High, false)]
    [InlineData(Medium, false, Low, false)]
    [InlineData(Medium, true, High, false)]         // UIAccess hebt UIPI auf
    public void Nur_ein_hoeher_eingestuftes_Ziel_blockiert(int own, bool uiAccess, int target, bool blocked)
        => Assert.Equal(blocked ? Elevation.BlockedText : null, Elevation.Problem(own, uiAccess, target));

    [Fact]
    public void Unbekannte_Stufe_blockiert_nie()
    {
        Assert.Null(Elevation.Problem(null, false, High));
        Assert.Null(Elevation.Problem(Medium, false, null));
        Assert.Null(Elevation.Problem(null, false, null));
    }

    [Fact]
    public void Eigene_Stufe_ist_lesbar_und_dieselbe_wie_ueber_die_Prozessnummer()
    {
        var (own, _) = Elevation.OwnLevel();

        Assert.NotNull(own);
        Assert.InRange(own.Value, Low, SystemLevel);
        Assert.Equal(0, own.Value % 0x100);
        Assert.Equal(own, Elevation.Level((uint)Environment.ProcessId));
    }

    [Fact]
    public void Nicht_vorhandener_Prozess_ergibt_unbekannt()
    {
        Assert.Null(Elevation.Level(0));
        Assert.Null(Elevation.Level(0xFFFFFFF0));
    }

    [Fact]
    public void Systemprozess_steht_ueber_jedem_Benutzerprozess()
    {
        // services.exe läuft immer als SYSTEM. Ohne Administratorrechte lässt Windows seine Stufe nicht lesen – dann „unbekannt“.
        using var services = Process.GetProcessesByName("services").FirstOrDefault();
        Assert.NotNull(services);
        int? level = Elevation.Level((uint)services.Id);
        var (own, uiAccess) = Elevation.OwnLevel();

        if (level is null)
        {
            Assert.Null(Elevation.Problem(own, uiAccess, level));
            return;
        }
        Assert.Equal(SystemLevel, level);
        Assert.Equal(own < SystemLevel && !uiAccess ? Elevation.BlockedText : null, Elevation.Problem(own, uiAccess, level));
    }

    [Fact]
    public void Pruefung_des_Vordergrundfensters_wirft_nie()
    {
        string? problem = Elevation.ForegroundProblem();
        if (Native.GetForegroundWindow() == 0) Assert.Null(problem); // Sitzung 0: kein Vordergrundfenster
    }
}
