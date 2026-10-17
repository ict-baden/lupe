using System.Diagnostics.CodeAnalysis;
using Lupe.Core.Camera;
using Lupe.Core.Input;
using Lupe.Core.Model;
using Lupe.Core.Screen;

namespace Lupe.Core.Platform;

/// <summary>
/// Plattform ohne Systemfunktionen: Kameras leer, Bildschirmfoto, Tippen, Tastenkürzel und Autostart „nicht verfügbar“.
/// Gilt, wenn keine Plattform-Assembly gefunden wurde, und dient den noch leeren Umsetzungen als Grundlage.
/// Erkennung, Verlauf, Einstellungen und Befehlszeile funktionieren trotzdem.
/// </summary>
public class UnsupportedPlatform : IPlatform
{
    public UnsupportedPlatform() : this("unbekanntes System") { }

    /// <param name="name">Systemname für die Meldungen („Linux“, „macOS“ …).</param>
    public UnsupportedPlatform(string name)
    {
        Name = name;
        Cameras = new UnsupportedCameras();
        Screen = new UnsupportedScreen($"Bildschirmfotos sind auf {name} (noch) nicht verfügbar.");
        Keyboard = new UnsupportedKeyboard($"Eintippen ist auf {name} (noch) nicht verfügbar – den Text stattdessen kopieren.");
        KeyboardLayout = new UnsupportedKeyboardLayout();
        Hotkeys = new UnsupportedHotkeys($"Globale Tastenkürzel sind auf {name} (noch) nicht verfügbar – Lupe über das Symbol im Infobereich oder das Menü öffnen.");
        ActiveWindow = new UnsupportedActiveWindow();
        Autostart = new UnsupportedAutostart($"Autostart ist auf {name} (noch) nicht verfügbar.");
        Images = new UnsupportedImageDecoder($"Bilddateien lassen sich auf {name} (noch) nicht lesen.");
        Problems = new NoProblems();
    }

    public string Name { get; protected init; }
    public ICameraProvider Cameras { get; protected init; }
    public IScreenCapture Screen { get; protected init; }
    public IKeyboardOutput Keyboard { get; protected init; }
    public IKeyboardLayout KeyboardLayout { get; protected init; }
    public IGlobalHotkeyProvider Hotkeys { get; protected init; }
    public IActiveWindow ActiveWindow { get; protected init; }
    public IAutostart Autostart { get; protected init; }
    public IImageDecoder Images { get; protected init; }
    public IPlatformProblems Problems { get; protected init; }
}

/// <summary>Keine Kameras.</summary>
public sealed class UnsupportedCameras : ICameraProvider
{
    public Task<IReadOnlyList<CameraInfo>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<CameraInfo>>([]);
    }

    public IFrameSource Open(CameraInfo camera) =>
        throw new NotSupportedException("Kameras sind auf diesem System (noch) nicht verfügbar.");
}

/// <summary>Kein Bildschirm.</summary>
public sealed class UnsupportedScreen(string reason) : IScreenCapture
{
    public IReadOnlyList<MonitorArea> Monitors() => [];
    public PixelRect VirtualScreen => default;

    public bool TryCapture(PixelRect area, [NotNullWhen(true)] out PixelImage? image, out string? error)
    {
        image = null;
        error = reason;
        return false;
    }
}

/// <summary>Kein Tippen: <see cref="ForegroundProblem"/> nennt immer den Grund.</summary>
public sealed class UnsupportedKeyboard(string reason) : IKeyboardOutput
{
    public int Send(ReadOnlySpan<KeyEvent> events, out int errorCode)
    {
        errorCode = -1;
        return 0;
    }

    public bool IsDown(int virtualKey) => false;
    public void Pause(int milliseconds, CancellationToken ct) { if (milliseconds > 0) ct.WaitHandle.WaitOne(milliseconds); }
    public string? ForegroundProblem() => reason;
    public string DescribeRejection(int errorCode) => reason;
}

/// <summary>Kein Wissen über das Layout.</summary>
public sealed class UnsupportedKeyboardLayout : IKeyboardLayout
{
    public char? KeyChar(int virtualKey) => null;
    public int? KeyForChar(char c) => null;
    public bool AltGrIsCtrlAlt => false;
    public string? AltGrText(int virtualKey, bool shift) => null;
}

/// <summary>Keine globalen Tastenkürzel.</summary>
public sealed class UnsupportedHotkeys(string reason) : IGlobalHotkeyProvider
{
    public string? UnsupportedReason => reason;
    public IGlobalHotkey Register(Hotkey hotkey) => new FailedHotkey(reason);
}

/// <summary>Ein Kürzel, das nicht angemeldet werden konnte.</summary>
public sealed class FailedHotkey(string error) : IGlobalHotkey
{
    public event EventHandler? Pressed { add { } remove { } }
    public bool IsRegistered => false;
    public string? Error => error;
    public void Dispose() { }
}

/// <summary>Vordergrundfenster unbekannt.</summary>
public sealed class UnsupportedActiveWindow : IActiveWindow
{
    public bool IsSupported => false;
    public nint Foreground() => 0;
    public bool IsOwnWindow(nint window) => false;
    public Task<string?> ActivateAsync(nint window, CancellationToken ct = default) =>
        Task.FromResult<string?>("Das Fenster von vorher lässt sich auf diesem System nicht nach vorn holen – nichts eingetippt.");
}

/// <summary>Kein Autostart.</summary>
public sealed class UnsupportedAutostart(string reason) : IAutostart
{
    public bool IsSupported => false;
    public bool IsEnabled => false;
    public string? Set(bool enabled) => enabled ? reason : null;
}

/// <summary>Kein Bild-Decoder: jede Datei „nicht lesbar“.</summary>
public sealed class UnsupportedImageDecoder(string reason) : IImageDecoder
{
    public IReadOnlyList<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".bmp", ".gif"];

    public Task<PixelImage> DecodeAsync(Stream stream, string name, int maxPixels, CancellationToken ct) =>
        throw new InvalidDataException($"„{name}“ kann nicht gelesen werden: {reason}");

    public Exception Translate(Exception ex, string name) =>
        new InvalidDataException($"„{name}“ konnte nicht als Bild gelesen werden ({ex.Message.Trim()}).", ex);
}

/// <summary>Nichts bekannt.</summary>
public sealed class NoProblems : IPlatformProblems
{
    public IReadOnlyList<PlatformProblem> Check() => [];
}
