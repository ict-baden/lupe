// SPDX-License-Identifier: MIT
using Lupe.Core.Platform;
using Lupe.Core.Resources;
using Lupe.Platform.Windows.Camera;
using Microsoft.Win32;

namespace Lupe.Platform.Windows;

/// <summary>„Mit Windows starten“ – pro Benutzer über HKCU\…\Run, startet Lupe nur im Infobereich.</summary>
public sealed class WindowsAutostart : IAutostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Lupe";

    public bool IsSupported => true;

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return false;
            }
        }
    }

    public string? Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                // Über „dotnet Lupe.dll“ gestartet, wäre der Prozess dotnet.exe – das startete beim Anmelden nicht Lupe
                if (string.IsNullOrEmpty(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    return Strings.Win_Autostart_PathUnknown;
                key.SetValue(ValueName, $"\"{exe}\" --hintergrund");
            }
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return string.Format(enabled ? Strings.Win_Autostart_EnableFailed : Strings.Win_Autostart_DisableFailed, ex.Message);
        }
    }
}

/// <summary>Bekannte Hindernisse unter Windows: gesperrter Kamerazugriff in den Datenschutzeinstellungen.</summary>
public sealed class WindowsProblems : IPlatformProblems
{
    public IReadOnlyList<PlatformProblem> Check()
    {
        var privacy = CameraPrivacy.Read();
        if (privacy == CameraPrivacyState.Allowed) return [];
        string message = CameraProblems.Describe(new CameraProblem(CameraProblemKind.AccessDenied), null, privacy);
        return [new PlatformProblem(PlatformFeature.Camera, message, "ms-settings:privacy-webcam")];
    }
}
