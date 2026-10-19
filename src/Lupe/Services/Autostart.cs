// SPDX-License-Identifier: MIT
using Lupe.Core.Platform;

namespace Lupe.Services;

/// <summary>„Mit Windows starten“ – über die Plattform (Windows: HKCU\…\Run), startet Lupe nur im Infobereich.</summary>
internal static class Autostart
{
    public static bool IsEnabled => LupePlatform.Current.Autostart.IsEnabled;

    /// <summary>Ein- oder ausschalten; Fehlermeldung auf Deutsch oder null.</summary>
    public static string? Set(bool enabled) => LupePlatform.Current.Autostart.Set(enabled);
}
