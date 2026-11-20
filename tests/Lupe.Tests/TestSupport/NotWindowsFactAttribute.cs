// SPDX-License-Identifier: MIT
namespace Lupe.Tests.TestSupport;

/// <summary>
/// Test für Linux und macOS (z. B. Unix-Dateirechte, Unix-Socket); unter Windows übersprungen statt still bestanden.
/// </summary>
public sealed class NotWindowsFactAttribute : FactAttribute
{
    public NotWindowsFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Nur unter Linux und macOS.";
    }
}
