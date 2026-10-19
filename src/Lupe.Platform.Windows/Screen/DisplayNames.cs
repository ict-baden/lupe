// SPDX-License-Identifier: MIT
using System.Diagnostics;
using Lupe.Core.Model;
using Lupe.Core.Screen;

namespace Lupe.Platform.Windows.Screen;

/// <summary>
/// Liest die Klarnamen der angeschlossenen Monitore (z. B. „DELL U2720Q“) über die DisplayConfig-API und ordnet sie
/// den GDI-Gerätenamen („\\.\DISPLAY1“) zu. Ohne Zugriff (z. B. in Sitzung 0) oder bei Fehlern: leere Zuordnung.
/// </summary>
internal static unsafe class DisplayNames
{
    public static IReadOnlyDictionary<string, string> Query()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Native.DISPLAYCONFIG_PATH_INFO[]? paths = null;
            uint pathCount = 0;
            // Ändert sich die Anordnung zwischen den beiden Aufrufen, meldet Windows ERROR_INSUFFICIENT_BUFFER – dann neu versuchen.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (Native.GetDisplayConfigBufferSizes(Native.QDC_ONLY_ACTIVE_PATHS, out pathCount, out uint modeCount) != Native.ERROR_SUCCESS)
                    return result;
                paths = new Native.DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new Native.DISPLAYCONFIG_MODE_INFO[modeCount];
                int error;
                fixed (Native.DISPLAYCONFIG_PATH_INFO* pathPtr = paths)
                fixed (Native.DISPLAYCONFIG_MODE_INFO* modePtr = modes)
                    error = Native.QueryDisplayConfig(Native.QDC_ONLY_ACTIVE_PATHS, ref pathCount, pathPtr, ref modeCount, modePtr, 0);
                if (error == Native.ERROR_SUCCESS) break;
                if (error != Native.ERROR_INSUFFICIENT_BUFFER) return result;
                paths = null;
            }
            if (paths is null) return result;

            for (int i = 0; i < pathCount; i++)
            {
                ref readonly var path = ref paths[i];

                var source = new Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                source.header.type = Native.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                source.header.size = (uint)sizeof(Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME);
                source.header.adapterId = path.sourceInfo.adapterId;
                source.header.id = path.sourceInfo.id;
                if (Native.DisplayConfigGetDeviceInfo(&source.header) != Native.ERROR_SUCCESS) continue;

                var target = new Native.DISPLAYCONFIG_TARGET_DEVICE_NAME();
                target.header.type = Native.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                target.header.size = (uint)sizeof(Native.DISPLAYCONFIG_TARGET_DEVICE_NAME);
                target.header.adapterId = path.targetInfo.adapterId;
                target.header.id = path.targetInfo.id;
                if (Native.DisplayConfigGetDeviceInfo(&target.header) != Native.ERROR_SUCCESS) continue;

                string gdiName = FixedText(source.viewGdiDeviceName, 32);
                string friendly = FixedText(target.monitorFriendlyDeviceName, 64);
                // Bei Spiegelung (mehrere Monitore an einer Quelle) zählt der erste Name.
                if (gdiName.Length > 0 && friendly.Length > 0) result.TryAdd(gdiName, friendly);
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            Debug.WriteLine($"Lupe: Monitornamen nicht lesbar: {ex.Message}");
        }
        return result;
    }

    /// <summary>Text aus einem festen WCHAR-Puffer, höchstens bis zur ersten Null und nie über das Pufferende hinaus.</summary>
    internal static string FixedText(char* buffer, int capacity)
    {
        var span = new ReadOnlySpan<char>(buffer, capacity);
        int end = span.IndexOf('\0');
        return (end >= 0 ? span[..end] : span).ToString().Trim();
    }
}
