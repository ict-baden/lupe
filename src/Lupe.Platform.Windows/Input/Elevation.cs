// SPDX-License-Identifier: MIT
using Lupe.Core.Input;
using Lupe.Core.Model;
using Lupe.Core.Resources;

namespace Lupe.Platform.Windows.Input;

/// <summary>
/// Erkennt, wann Windows Tastatureingaben stillschweigend verwirft: Läuft das Vordergrundfenster in einem Prozess mit
/// höherer Integritätsstufe als Lupe (z. B. eine als Administrator gestartete Eingabeaufforderung), blockiert die
/// Benutzeroberflächen-Isolation (UIPI) SendInput dorthin. SendInput meldet dabei trotzdem Erfolg – ohne diese Prüfung
/// hieße es „eingetippt“, obwohl nichts ankam.
/// </summary>
internal static unsafe class Elevation
{
    internal static string BlockedText => Strings.Win_Typing_ForegroundElevated;

    /// <summary>
    /// Der Grund (deutsch), warum Eingaben das Vordergrundfenster nicht erreichen würden, sonst null – auch dann, wenn sich
    /// die Rechte nicht feststellen lassen (lieber tippen als grundlos verweigern).
    /// </summary>
    public static string? ForegroundProblem()
    {
        nint window = Native.GetForegroundWindow();
        if (window == 0) return null;
        Native.GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0 || processId == (uint)Environment.ProcessId) return null;
        var (own, uiAccess) = OwnLevel();
        return Problem(own, uiAccess, Level(processId));
    }

    /// <summary>Blockiert UIPI? Nur wenn beide Stufen bekannt sind, Lupe kein UIAccess hat und das Ziel höher steht.</summary>
    internal static string? Problem(int? own, bool ownUiAccess, int? target)
        => !ownUiAccess && own is { } o && target is { } t && t > o ? BlockedText : null;

    /// <summary>Integritätsstufe (RID: 0x1000 niedrig, 0x2000 mittel, 0x3000 hoch, 0x4000 System) eines Prozesses, null wenn nicht lesbar.</summary>
    internal static int? Level(uint processId)
    {
        // PROCESS_QUERY_LIMITED_INFORMATION bekommt man auch für Prozesse mit höheren Rechten.
        nint process = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0) return null;
        try
        {
            return TokenLevel(process, out _);
        }
        finally
        {
            Native.CloseHandle(process);
        }
    }

    /// <summary>Die eigene Integritätsstufe und ob Lupe UIAccess hat (dann gilt UIPI nicht).</summary>
    internal static (int? Level, bool UiAccess) OwnLevel()
    {
        int? level = TokenLevel(Native.GetCurrentProcess(), out bool uiAccess);
        return (level, uiAccess);
    }

    private static int? TokenLevel(nint process, out bool uiAccess)
    {
        uiAccess = false;
        if (!Native.OpenProcessToken(process, Native.TOKEN_QUERY, out nint token)) return null;
        try
        {
            uint flag = 0;
            if (Native.GetTokenInformation(token, Native.TokenUIAccess, &flag, sizeof(uint), out _)) uiAccess = flag != 0;

            // TOKEN_MANDATORY_LABEL = SID_AND_ATTRIBUTES (Zeiger auf die SID + Attribute), die SID selbst folgt im selben Puffer.
            const int words = 32; // 256 Byte, eine SID hat höchstens 68
            nint* buffer = stackalloc nint[words];
            if (!Native.GetTokenInformation(token, Native.TokenIntegrityLevel, buffer, (uint)(words * sizeof(nint)), out _)) return null;
            nint sid = buffer[0];
            if (sid == 0) return null;
            byte count = *Native.GetSidSubAuthorityCount(sid);
            if (count == 0) return null;
            return (int)*Native.GetSidSubAuthority(sid, (uint)(count - 1));
        }
        finally
        {
            Native.CloseHandle(token);
        }
    }
}
