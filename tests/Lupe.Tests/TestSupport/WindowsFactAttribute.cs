// SPDX-License-Identifier: MIT
namespace Lupe.Tests.TestSupport;

/// <summary>
/// Test, der Windows-Verhalten voraussetzt (z. B. Dateisperren über FileShare, die Linux und macOS nicht kennen, oder
/// Windows-Pfade); anderswo übersprungen.
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Nur unter Windows (Dateisperren bzw. Pfade wie unter Windows).";
    }
}
