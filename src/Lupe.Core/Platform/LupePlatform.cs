// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Lupe.Core.Platform;

/// <summary>
/// Die Plattform, mit der Lupe gerade läuft. Die App setzt sie beim Start mit <see cref="Use"/> (empfohlen, ohne
/// Reflexion). Ist keine gesetzt, sucht <see cref="Current"/> beim ersten Zugriff die zum laufenden System passende
/// Assembly (Lupe.Platform.Windows, Lupe.Platform.Linux oder Lupe.Platform.MacOS) neben Lupe.Core und nimmt den Typ aus
/// ihrem <see cref="LupePlatformAttribute"/>. Fehlt sie, gilt <see cref="UnsupportedPlatform"/>: alles „nicht verfügbar“.
/// </summary>
public static class LupePlatform
{
    private static readonly Lock Gate = new();
    private static IPlatform? s_current;

    /// <summary>Die aktive Plattform (nie null).</summary>
    public static IPlatform Current
    {
        get
        {
            var current = Volatile.Read(ref s_current);
            if (current is not null) return current;
            lock (Gate)
            {
                s_current ??= Discover() ?? new UnsupportedPlatform();
                return s_current;
            }
        }
    }

    /// <summary>Setzt die Plattform (App-Start, Tests). null setzt zurück – der nächste Zugriff sucht erneut.</summary>
    public static void Use(IPlatform? platform)
    {
        lock (Gate) s_current = platform;
    }

    /// <summary>Name der Plattform-Assembly für das laufende System, null bei einem unbekannten System.</summary>
    public static string? AssemblyNameForThisOs() =>
        OperatingSystem.IsWindows() ? "Lupe.Platform.Windows"
        : OperatingSystem.IsLinux() ? "Lupe.Platform.Linux"
        : OperatingSystem.IsMacOS() ? "Lupe.Platform.MacOS"
        : null;

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Plattform-Typ ist per Attribut referenziert und wird von der App ohnehin über Use gesetzt.")]
    private static IPlatform? Discover()
    {
        string? name = AssemblyNameForThisOs();
        if (name is null) return null;
        try
        {
            var assembly = Assembly.Load(new AssemblyName(name));
            var type = assembly.GetCustomAttribute<LupePlatformAttribute>()?.PlatformType;
            if (type is null) return null;
            return Activator.CreateInstance(type) as IPlatform;
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException
            or TypeLoadException or MissingMethodException or TargetInvocationException)
        {
            Trace.TraceWarning($"Lupe: Plattform „{name}“ nicht ladbar: {ex.GetType().Name} {ex.Message}");
            return null;
        }
    }
}
