// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Lupe.Tests;

/// <summary>
/// Die Tests prüfen die deutschen Texte (Strings.de.resx) und deutsche Zahlen- und Datumsformate – unabhängig davon,
/// in welcher Sprache der Rechner läuft. Tests für andere Sprachen setzen die Kultur selbst.
/// </summary>
internal static class TestCulture
{
    public static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    [ModuleInitializer]
    internal static void UseGerman()
    {
        CultureInfo.DefaultThreadCurrentCulture = German;
        CultureInfo.DefaultThreadCurrentUICulture = German;
        CultureInfo.CurrentCulture = German;
        CultureInfo.CurrentUICulture = German;
        // Lupe als eigener Prozess (Befehlszeile, Fenster) erbt die Variable und spricht dann ebenfalls Deutsch
        // Tastenkürzel in Windows-Schreibweise (Win+Umschalt+Q), auch auf dem Mac; die Mac-Tests wählen ⌥⌘Q ausdrücklich
        Lupe.Core.Input.HotkeyText.MacStyle = false;
        Environment.SetEnvironmentVariable(Lupe.Core.Localization.UiLanguage.EnvironmentVariable, "de");
    }
}
