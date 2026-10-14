using Windows.Media.Capture.Frames;

namespace Lupe.Core.Camera;

/// <summary>Art eines Kameraproblems – bestimmt die Meldung und ob ein zweiter Versuch im geteilten Modus lohnt.</summary>
internal enum CameraProblemKind
{
    Unknown,
    /// <summary>Überhaupt keine Kamera angeschlossen.</summary>
    NoCamera,
    /// <summary>Diese Kamera gibt es (gerade) nicht.</summary>
    NotFound,
    /// <summary>Während des Betriebs weggefallen (abgezogen, Treiber neu gestartet).</summary>
    Disconnected,
    /// <summary>Ein anderes Programm benutzt die Kamera.</summary>
    InUse,
    /// <summary>Ein anderes Programm hat die laufende Kamera übernommen.</summary>
    Preempted,
    /// <summary>Datenstrom wurde von Windows beendet (übernommen oder abgezogen, genaue Ursache unbekannt).</summary>
    StreamShutdown,
    /// <summary>Datenschutz/Richtlinie verweigert den Zugriff.</summary>
    AccessDenied,
    /// <summary>Kein Farbbild (z. B. reine Infrarot-Kamera).</summary>
    NoColorStream,
    /// <summary>Kein lesbares Bildformat.</summary>
    FormatNotSupported,
    /// <summary>Windows N/KN ohne Media Feature Pack.</summary>
    MediaFeaturesMissing,
}

internal sealed record CameraProblem(CameraProblemKind Kind, int HResult = 0, string? Detail = null)
{
    /// <summary>Lohnt ein zweiter Versuch im geteilten Lesemodus (SharedReadOnly)?</summary>
    public bool RetryShared => Kind is CameraProblemKind.InUse or CameraProblemKind.Preempted or CameraProblemKind.Unknown
        or CameraProblemKind.FormatNotSupported or CameraProblemKind.StreamShutdown;
}

/// <summary>Ordnet Fehler aus Media Foundation/WinRT ein und formuliert deutsche, handlungsleitende Meldungen.</summary>
internal static class CameraProblems
{
    // Win32 als HRESULT
    private const int E_ACCESSDENIED = unchecked((int)0x80070005);
    private const int E_FILE_NOT_FOUND = unchecked((int)0x80070002);
    private const int E_PATH_NOT_FOUND = unchecked((int)0x80070003);
    private const int E_NOT_READY = unchecked((int)0x80070015);
    private const int E_GEN_FAILURE = unchecked((int)0x8007001F);
    private const int E_SHARING_VIOLATION = unchecked((int)0x80070020);
    private const int E_MOD_NOT_FOUND = unchecked((int)0x8007007E);
    private const int E_BUSY = unchecked((int)0x800700AA);
    private const int E_DEVICE_NOT_CONNECTED = unchecked((int)0x8007048F);
    private const int E_NOT_FOUND = unchecked((int)0x80070490);
    private const int E_NO_SYSTEM_RESOURCES = unchecked((int)0x800705AA);
    private const int E_DEVICE_REMOVED = unchecked((int)0x80070651);
    private const int E_DEVICE_IN_USE = unchecked((int)0x80070964);
    private const int REGDB_E_CLASSNOTREG = unchecked((int)0x80040154);
    private const int CLASS_E_CLASSNOTAVAILABLE = unchecked((int)0x80040111);
    // Media Foundation (mferror.h)
    private const int MF_E_NOT_FOUND = unchecked((int)0xC00D36D5);
    private const int MF_E_UNSUPPORTED_FORMAT = unchecked((int)0xC00D3E98);
    private const int MF_E_INVALIDMEDIATYPE = unchecked((int)0xC00D36B4);
    private const int MF_E_HW_MFT_FAILED_START_STREAMING = unchecked((int)0xC00D3704);
    private const int MF_E_SHUTDOWN = unchecked((int)0xC00D3E85);
    private const int MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED = unchecked((int)0xC00D3EA2);
    private const int MF_E_VIDEO_RECORDING_DEVICE_PREEMPTED = unchecked((int)0xC00D3EA3);
    private const int MF_E_VIDEO_DEVICE_LOCKED = unchecked((int)0xC00D4E24);
    private const int MF_E_NO_CAPTURE_DEVICES_AVAILABLE = unchecked((int)0xC00DABE0);
    private const int MF_E_CAPTURE_SOURCE_NO_VIDEO_STREAM_PRESENT = unchecked((int)0xC00DABE7);
    private const int MF_E_UNSUPPORTED_CAPTURE_DEVICE_PRESENT = unchecked((int)0xC00DABED);
    private const int MF_E_CAMERA_PRIVACY_NOT_ALLOWED = unchecked((int)0xC00DB798);

    private const string PrivacyPath = "Einstellungen → Datenschutz und Sicherheit → Kamera";

    public static CameraProblemKind Classify(int hresult) => hresult switch
    {
        E_ACCESSDENIED or MF_E_CAMERA_PRIVACY_NOT_ALLOWED => CameraProblemKind.AccessDenied,
        E_FILE_NOT_FOUND or E_PATH_NOT_FOUND or E_NOT_FOUND or MF_E_NOT_FOUND or E_NOT_READY => CameraProblemKind.NotFound,
        MF_E_NO_CAPTURE_DEVICES_AVAILABLE => CameraProblemKind.NoCamera,
        MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED or MF_E_SHUTDOWN or E_DEVICE_REMOVED or E_DEVICE_NOT_CONNECTED
            or E_GEN_FAILURE => CameraProblemKind.Disconnected,
        MF_E_VIDEO_RECORDING_DEVICE_PREEMPTED => CameraProblemKind.Preempted,
        MF_E_HW_MFT_FAILED_START_STREAMING or MF_E_VIDEO_DEVICE_LOCKED or E_SHARING_VIOLATION or E_BUSY
            or E_NO_SYSTEM_RESOURCES or E_DEVICE_IN_USE => CameraProblemKind.InUse,
        MF_E_CAPTURE_SOURCE_NO_VIDEO_STREAM_PRESENT => CameraProblemKind.NoColorStream,
        MF_E_UNSUPPORTED_FORMAT or MF_E_INVALIDMEDIATYPE or MF_E_UNSUPPORTED_CAPTURE_DEVICE_PRESENT => CameraProblemKind.FormatNotSupported,
        REGDB_E_CLASSNOTREG or CLASS_E_CLASSNOTAVAILABLE or E_MOD_NOT_FOUND => CameraProblemKind.MediaFeaturesMissing,
        _ => CameraProblemKind.Unknown,
    };

    public static CameraProblem FromException(Exception ex) => ex switch
    {
        UnauthorizedAccessException => new(CameraProblemKind.AccessDenied, ex.HResult, ex.Message),
        DllNotFoundException or EntryPointNotFoundException or TypeLoadException
            => new(CameraProblemKind.MediaFeaturesMissing, ex.HResult, ex.Message),
        _ => new(Classify(ex.HResult), ex.HResult, ex.Message),
    };

    public static CameraProblem FromStartStatus(MediaFrameReaderStartStatus status) => status switch
    {
        MediaFrameReaderStartStatus.DeviceNotAvailable => new(CameraProblemKind.Disconnected, 0, "DeviceNotAvailable"),
        MediaFrameReaderStartStatus.OutputFormatNotSupported => new(CameraProblemKind.FormatNotSupported, 0, "OutputFormatNotSupported"),
        MediaFrameReaderStartStatus.ExclusiveControlNotAvailable => new(CameraProblemKind.InUse, 0, "ExclusiveControlNotAvailable"),
        _ => new(CameraProblemKind.Unknown, 0, status.ToString()),
    };

    /// <summary>
    /// Von zwei Fehlschlägen (exklusiv, dann geteilt) den aussagekräftigeren: ein konkreter schlägt „unbekannt“, sonst der erste.
    /// </summary>
    public static CameraProblem MoreSpecific(CameraProblem first, CameraProblem? second) =>
        second is not null && first.Kind == CameraProblemKind.Unknown && second.Kind != CameraProblemKind.Unknown ? second : first;

    /// <summary>Meldung für Menschen: was los ist und was zu tun ist.</summary>
    /// <param name="privacy">Nur für <see cref="CameraProblemKind.AccessDenied"/>; null = aus der Registry lesen.</param>
    public static string Describe(CameraProblem problem, string? cameraName, CameraPrivacyState? privacy = null)
    {
        string name = CameraList.CleanName(cameraName);
        string cam = $"Die Kamera „{name}“";
        return problem.Kind switch
        {
            CameraProblemKind.NoCamera =>
                "Keine Kamera gefunden. Schließen Sie eine Kamera an oder scannen Sie stattdessen einen Bildschirmausschnitt oder eine Bilddatei.",
            CameraProblemKind.NotFound =>
                $"{cam} wurde nicht gefunden. Ist sie angeschlossen und eingeschaltet? Wählen Sie sonst eine andere Kamera.",
            CameraProblemKind.Disconnected =>
                $"{cam} ist nicht mehr erreichbar – wurde sie abgezogen? Schließen Sie sie wieder an und starten Sie die Kamera erneut.",
            CameraProblemKind.InUse =>
                $"{cam} wird gerade von einem anderen Programm benutzt (z. B. Teams, Zoom oder die Kamera-App). Schließen Sie das Programm und starten Sie die Kamera erneut.",
            CameraProblemKind.Preempted =>
                $"{cam} wurde von einem anderen Programm übernommen. Schließen Sie das andere Programm und starten Sie die Kamera erneut.",
            CameraProblemKind.StreamShutdown =>
                $"{cam} wurde von Windows beendet – ein anderes Programm hat sie übernommen oder sie wurde abgezogen. Prüfen Sie das und starten Sie die Kamera erneut.",
            CameraProblemKind.AccessDenied => DescribeAccessDenied(name, privacy ?? CameraPrivacy.Read()),
            CameraProblemKind.NoColorStream =>
                $"{cam} liefert kein Farbbild (z. B. eine reine Infrarot-Kamera für Windows Hello). Wählen Sie eine andere Kamera.",
            CameraProblemKind.FormatNotSupported =>
                $"{cam} liefert kein Bildformat, das Lupe lesen kann. Wählen Sie eine andere Kamera oder aktualisieren Sie den Kameratreiber.",
            CameraProblemKind.MediaFeaturesMissing =>
                "Auf diesem Windows fehlen die Medienfunktionen (Windows-Ausgabe „N“). Installieren Sie das „Media Feature Pack“ unter Einstellungen → System → Optionale Features und starten Sie Lupe neu.",
            _ =>
                $"{cam} lässt sich nicht verwenden ({Code(problem)}). Ziehen Sie die Kamera ab und wieder an oder starten Sie Windows neu; hilft das nicht, wählen Sie eine andere Kamera.",
        };
    }

    private static string DescribeAccessDenied(string name, CameraPrivacyState privacy) => privacy switch
    {
        CameraPrivacyState.PolicyBlocked =>
            "Die Kamera ist auf diesem Gerät per Richtlinie gesperrt. Wenden Sie sich an Ihre IT-Abteilung.",
        CameraPrivacyState.DeviceOff =>
            $"Der Kamerazugriff ist für dieses Gerät ausgeschaltet. Schalten Sie unter {PrivacyPath} den „Kamerazugriff“ ein (dafür sind Administratorrechte nötig).",
        CameraPrivacyState.AppsOff =>
            $"Der Kamerazugriff für Apps ist ausgeschaltet. Schalten Sie unter {PrivacyPath} den Zugriff für Apps und für Desktop-Apps ein und starten Sie die Kamera erneut.",
        CameraPrivacyState.DesktopAppsOff =>
            $"Windows erlaubt Desktop-Apps keinen Kamerazugriff. Schalten Sie unter {PrivacyPath} „Desktop-Apps den Zugriff auf die Kamera erlauben“ ein und starten Sie die Kamera erneut.",
        CameraPrivacyState.ThisAppOff =>
            $"Windows hat Lupe den Kamerazugriff entzogen. Erlauben Sie Lupe unter {PrivacyPath} (Liste der Desktop-Apps) wieder den Zugriff und starten Sie die Kamera erneut.",
        _ =>
            $"Windows verweigert den Zugriff auf die Kamera „{name}“. Prüfen Sie unter {PrivacyPath}, ob der Kamerazugriff für Desktop-Apps erlaubt ist, und ob ein Kamera-Schalter oder eine Abdeckung am Gerät aktiv ist.",
    };

    private static string Code(CameraProblem problem)
    {
        var parts = new List<string>(2);
        if (problem.HResult != 0) parts.Add($"Fehler 0x{problem.HResult:X8}");
        string? detail = problem.Detail?.Trim();
        if (!string.IsNullOrEmpty(detail)) parts.Add(detail.Length > 160 ? detail[..160] + " …" : detail);
        return parts.Count == 0 ? "unbekannter Fehler" : string.Join(": ", parts);
    }
}
