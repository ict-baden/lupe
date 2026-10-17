using Lupe.Core.Input;
using Lupe.Core.Platform;

namespace Lupe.Platform.Linux.Input;

/// <summary>Tippen unter Linux – noch nicht umgesetzt (der Text lässt sich weiterhin kopieren).</summary>
public sealed class LinuxKeyboard : IKeyboardOutput
{
    private const string Unavailable = "Eintippen ist unter Linux noch nicht verfügbar – den Text stattdessen kopieren.";

    public int Send(ReadOnlySpan<KeyEvent> events, out int errorCode)
    {
        errorCode = -1;
        return 0;
    }

    public bool IsDown(int virtualKey) => false;

    public void Pause(int milliseconds, CancellationToken ct)
    {
        if (milliseconds > 0) ct.WaitHandle.WaitOne(milliseconds);
    }

    public string? ForegroundProblem() => Unavailable;
    public string DescribeRejection(int errorCode) => Unavailable;
}

/// <summary>Tastaturlayout unter Linux – noch unbekannt.</summary>
public sealed class LinuxKeyboardLayout : IKeyboardLayout
{
    public char? KeyChar(int virtualKey) => null;
    public int? KeyForChar(char c) => null;
    public bool AltGrIsCtrlAlt => false;
    public string? AltGrText(int virtualKey, bool shift) => null;
}

/// <summary>Globale Tastenkürzel unter Linux – noch nicht umgesetzt.</summary>
public sealed class LinuxHotkeys : IGlobalHotkeyProvider
{
    public string? UnsupportedReason =>
        "Globale Tastenkürzel sind unter Linux noch nicht verfügbar – Lupe über das Symbol im Infobereich oder das Menü öffnen.";

    public IGlobalHotkey Register(Lupe.Core.Model.Hotkey hotkey) => new FailedHotkey(UnsupportedReason!);
}

/// <summary>Vordergrundfenster unter Linux – noch unbekannt.</summary>
public sealed class LinuxActiveWindow : IActiveWindow
{
    public bool IsSupported => false;
    public nint Foreground() => 0;
    public bool IsOwnWindow(nint window) => false;

    public Task<string?> ActivateAsync(nint window, CancellationToken ct = default) =>
        Task.FromResult<string?>("Das Fenster von vorher lässt sich unter Linux noch nicht nach vorn holen – nichts eingetippt.");
}
