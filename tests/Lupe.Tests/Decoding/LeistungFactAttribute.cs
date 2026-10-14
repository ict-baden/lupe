namespace Lupe.Tests.Decoding;

/// <summary>
/// Zeitmessung mit festen Grenzen. Läuft nur auf Entwicklungsrechnern: GitHub-Runner haben wenige, geteilte Kerne und
/// schwanken zu stark (gemessen: Full HD ohne Code mehr als doppelt so langsam), ein roter Lauf sagte dort nichts über Lupe.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LeistungFactAttribute : FactAttribute
{
    public LeistungFactAttribute()
    {
        // GitHub Actions setzt CI=true; lokal mit LUPE_LEISTUNG=1 erzwingbar
        bool ci = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
        bool forced = Environment.GetEnvironmentVariable("LUPE_LEISTUNG") == "1";
        if (ci && !forced) Skip = "Zeitmessung nur auf Entwicklungsrechnern (in der CI zu ungleichmäßig).";
    }
}
