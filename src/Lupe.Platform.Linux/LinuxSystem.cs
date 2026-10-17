using Lupe.Core.Platform;

namespace Lupe.Platform.Linux;

/// <summary>Autostart unter Linux – noch nicht umgesetzt.</summary>
public sealed class LinuxAutostart : IAutostart
{
    public bool IsSupported => false;
    public bool IsEnabled => false;
    public string? Set(bool enabled) => enabled ? "Autostart ist unter Linux noch nicht verfügbar." : null;
}

/// <summary>Berechtigungen und Systemeinstellungen unter Linux – noch keine Prüfungen.</summary>
public sealed class LinuxProblems : IPlatformProblems
{
    public IReadOnlyList<PlatformProblem> Check() => [];
}
