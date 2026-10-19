// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Runtime.InteropServices;
using Lupe.Core.Camera;
using Lupe.Core.Resources;

namespace Lupe.Platform.MacOS.Camera;

/// <summary>
/// Die Kamera-Bibliothek libLupeCamera.dylib (Native/LupeCamera.swift, direkt AVFoundation). Fehlt sie – Build auf
/// Windows oder Linux –, gibt es unter macOS keine Kamera statt einer Ausnahme.
/// </summary>
internal static unsafe partial class NativeCamera
{
    private const string Library = "LupeCamera";

    // Fehlercodes wie in LupeCamera.swift
    public const int Ok = 0, Denied = 1, Restricted = 2, NotFound = 3, OpenFailed = 4, NoFormat = 5, Runtime = 6, Disconnected = 7;

    [LibraryImport(Library, EntryPoint = "lupe_cam_request_access")] private static partial int RequestAccessNative();
    [LibraryImport(Library, EntryPoint = "lupe_cam_list")] private static partial nint ListNative();
    [LibraryImport(Library, EntryPoint = "lupe_cam_free")] private static partial void Free(nint p);
    [LibraryImport(Library, EntryPoint = "lupe_cam_formats", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int FormatsNative(string id, int* triples, int capacity);
    [LibraryImport(Library, EntryPoint = "lupe_cam_open", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint Open(string id, int width, int height,
        delegate* unmanaged<nint, byte*, int, int, int, void> onFrame, delegate* unmanaged<nint, int, void> onError,
        nint context, out int error);
    [LibraryImport(Library, EntryPoint = "lupe_cam_start")] public static partial void Start(nint handle);
    [LibraryImport(Library, EntryPoint = "lupe_cam_stop")] public static partial void Stop(nint handle);
    [LibraryImport(Library, EntryPoint = "lupe_cam_close")] public static partial void Close(nint handle);

    /// <summary>Ist die Bibliothek da (und wir auf macOS)?</summary>
    public static bool Available => s_available.Value;

    private static readonly Lazy<bool> s_available = new(() =>
    {
        if (!OperatingSystem.IsMacOS()) return false;
        if (NativeLibrary.TryLoad(Library, typeof(NativeCamera).Assembly, null, out _)) return true;
        Trace.TraceWarning("Lupe: libLupeCamera.dylib fehlt – keine Kamera unter macOS.");
        return false;
    });

    /// <summary>Fragt bei Bedarf nach der Erlaubnis (Systemdialog) und wartet auf die Antwort.</summary>
    public static CameraAuthorization RequestAccess() =>
        Available ? (CameraAuthorization)RequestAccessNative() : CameraAuthorization.Unknown;

    /// <summary>Alle Kameras als (Kennung, Name).</summary>
    public static List<(string Id, string Name)> List()
    {
        if (!Available) return [];
        nint text = ListNative();
        if (text == 0) return [];
        try
        {
            return ParseList(Marshal.PtrToStringUTF8(text) ?? "");
        }
        finally
        {
            Free(text);
        }
    }

    /// <summary>„Kennung TAB Name“ je Zeile; leere oder kaputte Zeilen fallen weg.</summary>
    internal static List<(string Id, string Name)> ParseList(string text)
    {
        var list = new List<(string, string)>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            string id = line[..tab], name = line[(tab + 1)..].Trim();
            list.Add((id, name.Length == 0 ? id : name));
        }
        return list;
    }

    /// <summary>Die Bildgrößen der Kamera (Bilder kommen immer als BGRA); null, wenn es sie nicht gibt.</summary>
    public static List<VideoFormatCandidate>? Formats(string id)
    {
        if (!Available) return null;
        const int capacity = 256;
        var triples = new int[capacity * 3];
        int count;
        fixed (int* p = triples) count = FormatsNative(id, p, capacity);
        if (count < 0) return null;
        var list = new List<VideoFormatCandidate>(count);
        for (int i = 0; i < count; i++)
            list.Add(new VideoFormatCandidate(triples[i * 3], triples[i * 3 + 1], triples[i * 3 + 2] / 1000.0, "BGRA8"));
        return list;
    }

    /// <summary>Text zu einem Fehlercode aus der Bibliothek.</summary>
    public static string Describe(int code, string name) => code switch
    {
        Denied => MacOSPermissions.CameraDenied,
        Restricted => MacOSPermissions.CameraRestricted,
        NotFound or Disconnected => string.Format(Strings.Mac_Camera_NotConnected, name),
        NoFormat => string.Format(Strings.Mac_Camera_NoFormat, name),
        Runtime => string.Format(Strings.Mac_Camera_Failed, name),
        _ => string.Format(Strings.Mac_Camera_StartFailed, name),
    };
}
