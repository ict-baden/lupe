using Lupe.Core.Camera;
using static Lupe.Tests.Camera.CameraTestSupport;

namespace Lupe.Tests.Camera;

public sealed class RepeatFilterTests
{
    private readonly ManualTimeProvider _time = new();

    private RepeatFilter Filter(int ms) => new(TimeSpan.FromMilliseconds(ms), _time);

    private long At(long ms)
    {
        _time.Set(ms);
        return _time.GetTimestamp();
    }

    private static string[] Texts(IReadOnlyList<Lupe.Core.Model.DecodedCode> codes) => codes.Select(c => c.Text).ToArray();

    [Fact]
    public void Erste_Sichtung_wird_gemeldet_Wiederholung_in_der_Sperre_nicht()
    {
        var f = Filter(3000);
        Assert.Equal(["A"], Texts(f.Filter([Code("A")], At(0))));
        Assert.Empty(f.Filter([Code("A")], At(100)));
        Assert.Empty(f.Filter([Code("A")], At(2999)));
    }

    [Fact]
    public void Sperre_zählt_ab_letzter_Sichtung_nicht_ab_erster()
    {
        var f = Filter(3000);
        Assert.Single(f.Filter([Code("A")], At(0)));
        Assert.Empty(f.Filter([Code("A")], At(2500)));
        // 5000 ms nach der ersten, aber nur 2500 nach der letzten Sichtung → weiter gesperrt
        Assert.Empty(f.Filter([Code("A")], At(5000)));
        // 3000 ms ohne Sichtung → wieder melden
        Assert.Single(f.Filter([Code("A")], At(8000)));
    }

    [Fact]
    public void Nach_Ablauf_wird_erneut_gemeldet()
    {
        var f = Filter(1000);
        Assert.Single(f.Filter([Code("A")], At(0)));
        Assert.Single(f.Filter([Code("A")], At(1000)));
        Assert.Single(f.Filter([Code("A")], At(2500)));
    }

    [Fact]
    public void Text_und_Format_bilden_den_Schlüssel()
    {
        var f = Filter(3000);
        Assert.Single(f.Filter([Code("123", "EAN_8")], At(0)));
        Assert.Single(f.Filter([Code("123", "CODE_128")], At(10)));
        Assert.Single(f.Filter([Code("1234", "EAN_8")], At(20)));
        Assert.Empty(f.Filter([Code("123", "EAN_8"), Code("123", "CODE_128")], At(30)));
        // Groß-/Kleinschreibung ist Inhalt – „abc“ ist ein anderer Code als „ABC“
        Assert.Single(f.Filter([Code("abc")], At(40)));
        Assert.Single(f.Filter([Code("ABC")], At(50)));
    }

    [Fact]
    public void Mehrere_Codes_nur_die_neuen_Reihenfolge_bleibt_Doppelte_einmal()
    {
        var f = Filter(3000);
        Assert.Equal(["B", "A"], Texts(f.Filter([Code("B"), Code("A"), Code("B")], At(0))));
        Assert.Equal(["C", "D"], Texts(f.Filter([Code("A"), Code("C"), Code("B"), Code("D")], At(500))));
    }

    [Fact]
    public void Gesperrte_Sichtung_verlängert_nur_den_gesehenen_Code()
    {
        var f = Filter(1000);
        f.Filter([Code("A"), Code("B")], At(0));
        Assert.Empty(f.Filter([Code("A")], At(900)));
        // A zuletzt bei 900 gesehen, B bei 0
        Assert.Equal(["B"], Texts(f.Filter([Code("A"), Code("B")], At(1500))));
    }

    [Fact]
    public void Sperre_null_meldet_jede_Sichtung()
    {
        var f = Filter(0);
        Assert.Single(f.Filter([Code("A")], At(0)));
        Assert.Single(f.Filter([Code("A")], At(0)));
        Assert.Single(f.Filter([Code("A")], At(1)));
    }

    [Fact]
    public void Clear_vergisst_alles()
    {
        var f = Filter(3000);
        f.Filter([Code("A")], At(0));
        f.Clear();
        Assert.Single(f.Filter([Code("A")], At(1)));
    }

    [Fact]
    public void Abgelaufene_Einträge_werden_aufgeräumt()
    {
        var f = Filter(100);
        for (int i = 0; i < 1000; i++) f.Filter([Code($"Code {i}")], At(i * 10));
        Assert.InRange(f.Count, 1, 11);
    }

    [Fact]
    public void Nullwerte_im_Ergebnis_werden_übersprungen()
    {
        var f = Filter(3000);
        Assert.Equal(["A"], Texts(f.Filter([null!, Code("A")], At(0))));
    }

    [Fact]
    public void Negative_Sperre_ist_ein_Programmierfehler()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RepeatFilter(TimeSpan.FromMilliseconds(-1), _time));
    }
}
