using Lupe.Core.Platform;

namespace Lupe.Platform.MacOS;

/// <summary>Autostart unter macOS – noch nicht umgesetzt.</summary>
public sealed class MacOSAutostart : IAutostart
{
    public bool IsSupported => false;
    public bool IsEnabled => false;
    public string? Set(bool enabled) => enabled ? "Autostart ist unter macOS noch nicht verfügbar." : null;
}

/// <summary>Berechtigungen und Systemeinstellungen unter macOS – noch keine Prüfungen.</summary>
public sealed class MacOSProblems : IPlatformProblems
{
    public IReadOnlyList<PlatformProblem> Check() => [];
}
