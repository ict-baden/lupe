// SPDX-License-Identifier: MIT
namespace Lupe.Tests.TestSupport;

/// <summary>
/// Test für das Verhalten der macOS-Anbindung auf anderen Systemen; unter macOS übersprungen statt still bestanden (dort
/// prüfen die Systemtests das echte Verhalten).
/// </summary>
public sealed class NotMacOSFactAttribute : FactAttribute
{
    public NotMacOSFactAttribute()
    {
        if (OperatingSystem.IsMacOS()) Skip = "Nicht unter macOS (dort echt, siehe MacOSSystemTests).";
    }
}
