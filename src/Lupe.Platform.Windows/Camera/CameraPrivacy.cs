// SPDX-License-Identifier: MIT
using Lupe.Core.Camera;
using Lupe.Core.Model;
using Microsoft.Win32;

namespace Lupe.Platform.Windows.Camera;

/// <summary>Was die Windows-Datenschutzeinstellungen zum Kamerazugriff von Lupe sagen.</summary>
internal enum CameraPrivacyState
{
    /// <summary>Nichts gefunden, das den Zugriff sperrt.</summary>
    Allowed,
    /// <summary>Gruppenrichtlinie sperrt die Kamera (AllowCamera = 0 oder LetAppsAccessCamera = Force Deny).</summary>
    PolicyBlocked,
    /// <summary>„Kamerazugriff“ für das ganze Gerät aus (nur mit Administratorrechten änderbar).</summary>
    DeviceOff,
    /// <summary>Für diesen Benutzer ist der Kamerazugriff für Apps aus.</summary>
    AppsOff,
    /// <summary>„Desktop-Apps den Zugriff auf die Kamera erlauben“ ist aus.</summary>
    DesktopAppsOff,
    /// <summary>Gerade Lupe wurde der Zugriff entzogen (Liste der Desktop-Apps in den Einstellungen).</summary>
    ThisAppOff,
}

/// <summary>
/// Liest die Kamera-Datenschutzeinstellungen aus der Registry (nur lesend), damit die Fehlermeldung bei
/// „Zugriff verweigert“ genau sagen kann, welcher Schalter aus ist.
/// </summary>
internal static class CameraPrivacy
{
    private const string ConsentKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";
    private const string CameraPolicyKey = @"SOFTWARE\Policies\Microsoft\Camera";
    private const string AppPrivacyPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy";

    /// <summary>Aktueller Zustand; bei Lesefehlern <see cref="CameraPrivacyState.Allowed"/> (= nichts Genaues bekannt).</summary>
    public static CameraPrivacyState Read()
    {
        try
        {
            string? appKey = AppConsentKeyName(Environment.ProcessPath);
            return Evaluate(
                allowCameraPolicy: ReadInt(Registry.LocalMachine, CameraPolicyKey, "AllowCamera"),
                letAppsAccessCameraPolicy: ReadInt(Registry.LocalMachine, AppPrivacyPolicyKey, "LetAppsAccessCamera"),
                deviceConsent: ReadString(Registry.LocalMachine, ConsentKey, "Value"),
                userConsent: ReadString(Registry.CurrentUser, ConsentKey, "Value"),
                desktopAppsConsent: ReadString(Registry.CurrentUser, ConsentKey + @"\NonPackaged", "Value"),
                thisAppConsent: appKey is null ? null : ReadString(Registry.CurrentUser, ConsentKey + @"\NonPackaged\" + appKey, "Value"));
        }
        catch (Exception)
        {
            return CameraPrivacyState.Allowed;
        }
    }

    /// <summary>Wertet die Registry-Werte aus; die strengste Sperre gewinnt.</summary>
    public static CameraPrivacyState Evaluate(int? allowCameraPolicy, int? letAppsAccessCameraPolicy, string? deviceConsent,
        string? userConsent, string? desktopAppsConsent, string? thisAppConsent)
    {
        if (allowCameraPolicy == 0 || letAppsAccessCameraPolicy == 2) return CameraPrivacyState.PolicyBlocked;
        if (IsDeny(deviceConsent)) return CameraPrivacyState.DeviceOff;
        if (IsDeny(userConsent)) return CameraPrivacyState.AppsOff;
        if (IsDeny(desktopAppsConsent)) return CameraPrivacyState.DesktopAppsOff;
        if (IsDeny(thisAppConsent)) return CameraPrivacyState.ThisAppOff;
        return CameraPrivacyState.Allowed;
    }

    /// <summary>Windows legt Desktop-Apps unter ihrem Pfad ab, „\“ durch „#“ ersetzt (C:#Programme#Lupe#Lupe.exe).</summary>
    public static string? AppConsentKeyName(string? processPath) =>
        string.IsNullOrWhiteSpace(processPath) ? null : processPath.Replace('\\', '#');

    private static bool IsDeny(string? value) => string.Equals(value?.Trim(), "Deny", StringComparison.OrdinalIgnoreCase);

    private static string? ReadString(RegistryKey hive, string path, string name)
    {
        using var key = hive.OpenSubKey(path, writable: false);
        return key?.GetValue(name) as string;
    }

    private static int? ReadInt(RegistryKey hive, string path, string name)
    {
        using var key = hive.OpenSubKey(path, writable: false);
        return key?.GetValue(name) is int value ? value : null;
    }
}
