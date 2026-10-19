// SPDX-License-Identifier: MIT
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Lupe.Core.Model;
using Lupe.Core.Platform;
using Lupe.Core.Resources;
using Lupe.Core.Screen;

namespace Lupe.Platform.Windows.Screen;

/// <summary>Bildschirmfotos für den Scan eines Ausschnitts – DPI-genau über alle Monitore.</summary>
/// <remarks>
/// Alle Koordinaten sind physische Pixel des virtuellen Desktops (der Hauptmonitor beginnt bei 0,0; Monitore links
/// oder oberhalb davon haben negative Koordinaten). Das gilt unabhängig davon, wie das aufrufende Programm
/// DPI-eingestellt ist: Die Aufrufe laufen intern immer mit Per-Monitor-DPI.
/// </remarks>
public sealed unsafe class WindowsScreenCapture : IScreenCapture
{
    /// <summary>Alle Monitore, von links nach rechts (bei gleicher Position von oben nach unten).</summary>
    public IReadOnlyList<MonitorArea> Monitors()
    {
        using var dpi = DpiScope.PerMonitor();
        var handles = MonitorHandles();
        if (handles.Count == 0) return [];

        var friendlyNames = DisplayNames.Query();
        var raw = new List<(PixelRect Bounds, PixelRect Work, double Scale, bool Primary, string Device)>(handles.Count);
        foreach (nint handle in handles)
        {
            var info = new Native.MONITORINFOEXW { cbSize = sizeof(Native.MONITORINFOEXW) };
            if (!Native.GetMonitorInfoW(handle, &info)) continue; // Monitor ist inzwischen weg
            double scale = 1.0;
            if (Native.GetDpiForMonitor(handle, Native.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                scale = dpiX / 96.0;
            raw.Add((ToPixelRect(info.rcMonitor), ToPixelRect(info.rcWork), scale,
                (info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0, DisplayNames.FixedText(info.szDevice, 32)));
        }

        raw.Sort((a, b) => a.Bounds.X != b.Bounds.X ? a.Bounds.X.CompareTo(b.Bounds.X) : a.Bounds.Y.CompareTo(b.Bounds.Y));
        var result = new List<MonitorArea>(raw.Count);
        var usedNumbers = new HashSet<int>();
        for (int i = 0; i < raw.Count; i++)
        {
            var m = raw[i];
            int number = DeviceNumber(m.Device) ?? 0;
            // Nummer aus „\\.\DISPLAY2“ wie in den Windows-Anzeigeeinstellungen; fehlt sie oder ist doppelt, die Position.
            if (number <= 0 || !usedNumbers.Add(number))
            {
                number = i + 1;
                while (!usedNumbers.Add(number)) number++;
            }
            result.Add(new MonitorArea(m.Bounds, m.Work, m.Scale, m.Primary, ScreenCapture.MonitorName(number, friendlyNames.GetValueOrDefault(m.Device))));
        }
        return result;
    }

    /// <summary>Der ganze virtuelle Desktop (alle Monitore).</summary>
    public PixelRect VirtualScreen
    {
        get
        {
            using var dpi = DpiScope.PerMonitor();
            // Vereinigung der Monitor-Rechtecke – dieselbe Quelle wie Monitors(), damit beides zusammenpasst.
            bool any = false;
            int left = 0, top = 0, right = 0, bottom = 0;
            foreach (nint handle in MonitorHandles())
            {
                var info = new Native.MONITORINFOEXW { cbSize = sizeof(Native.MONITORINFOEXW) };
                if (!Native.GetMonitorInfoW(handle, &info)) continue;
                var r = info.rcMonitor;
                if (!any) { (left, top, right, bottom) = (r.Left, r.Top, r.Right, r.Bottom); any = true; continue; }
                left = Math.Min(left, r.Left);
                top = Math.Min(top, r.Top);
                right = Math.Max(right, r.Right);
                bottom = Math.Max(bottom, r.Bottom);
            }
            if (any) return new PixelRect(left, top, right - left, bottom - top);
            return new PixelRect(
                Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN), Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN),
                Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN), Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN));
        }
    }

    /// <summary>GDI-Kopie vom Bildschirm (mit CAPTUREBLT, damit durchscheinende Fenster mitkommen).</summary>
    public bool TryCapture(PixelRect area, [NotNullWhen(true)] out PixelImage? image, out string? error)
    {
        ScreenCapture.Validate(area);
        using var dpi = DpiScope.PerMonitor();
        nint screen = Native.GetDC(0);
        if (screen == 0)
        {
            image = null;
            error = Strings.Win_Screen_Unreadable;
            return false;
        }
        try
        {
            return CopyFromDc(screen, area, Native.SRCCOPY | Native.CAPTUREBLT, out image, out error);
        }
        finally
        {
            Native.ReleaseDC(0, screen);
        }
    }

    /// <summary>
    /// Kopiert <paramref name="area"/> aus einem Gerätekontext (Bildschirm oder Speicher) in ein neues BGRA-Bild:
    /// über einen DIB-Abschnitt von oben nach unten, 32 Bit, Alpha danach auf 255 (GDI lässt ihn undefiniert).
    /// </summary>
    internal static bool CopyFromDc(nint source, PixelRect area, uint rop, [NotNullWhen(true)] out PixelImage? image, out string? error)
    {
        ScreenCapture.Validate(area);
        image = null;
        int width = area.Width, height = area.Height;

        nint memory = Native.CreateCompatibleDC(source);
        if (memory == 0)
        {
            error = string.Format(Strings.Win_Screen_GdiError, Marshal.GetLastPInvokeError());
            return false;
        }
        nint bitmap = 0, previous = 0;
        try
        {
            var info = new Native.BITMAPINFO
            {
                bmiHeader = new Native.BITMAPINFOHEADER
                {
                    biSize = (uint)sizeof(Native.BITMAPINFOHEADER),
                    biWidth = width,
                    biHeight = -height, // negativ = Zeilen von oben nach unten, wie PixelImage
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = Native.BI_RGB,
                },
            };
            bitmap = Native.CreateDIBSection(memory, &info, Native.DIB_RGB_COLORS, out nint bits, 0, 0);
            if (bitmap == 0 || bits == 0)
            {
                error = string.Format(Strings.Win_Screen_OutOfMemory, width, height);
                return false;
            }
            previous = Native.SelectObject(memory, bitmap);
            if (!Native.BitBlt(memory, 0, 0, width, height, source, area.X, area.Y, rop))
            {
                int code = Marshal.GetLastPInvokeError();
                error = code != 0
                    ? string.Format(Strings.Win_Screen_DesktopUnreadableCode, code)
                    : Strings.Win_Screen_DesktopUnreadable;
                return false;
            }
            Native.GdiFlush();

            var pixels = new byte[width * height * 4];
            new ReadOnlySpan<byte>((void*)bits, pixels.Length).CopyTo(pixels);
            ScreenCapture.MakeOpaque(pixels);
            image = new PixelImage(width, height, pixels);
            error = null;
            return true;
        }
        finally
        {
            if (previous != 0) Native.SelectObject(memory, previous);
            if (bitmap != 0) Native.DeleteObject(bitmap);
            Native.DeleteDC(memory);
        }
    }

    /// <summary>Die Nummer aus einem GDI-Gerätenamen wie „\\.\DISPLAY12“, sonst null.</summary>
    internal static int? DeviceNumber(string device)
    {
        int end = device.Length, start = end;
        while (start > 0 && char.IsAsciiDigit(device[start - 1])) start--;
        if (start == end || !device.AsSpan(0, start).EndsWith("DISPLAY", StringComparison.OrdinalIgnoreCase)) return null;
        return int.TryParse(device.AsSpan(start), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : null;
    }

    private static PixelRect ToPixelRect(Native.RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    private static List<nint> MonitorHandles()
    {
        var handles = new List<nint>();
        var gc = GCHandle.Alloc(handles);
        try
        {
            if (!Native.EnumDisplayMonitors(0, null, &OnMonitor, GCHandle.ToIntPtr(gc)))
                Debug.WriteLine($"Lupe: EnumDisplayMonitors fehlgeschlagen ({Marshal.GetLastPInvokeError()}).");
        }
        finally
        {
            gc.Free();
        }
        return handles;
    }

    [UnmanagedCallersOnly]
    private static int OnMonitor(nint monitor, nint dc, Native.RECT* rect, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is List<nint> handles) handles.Add(monitor);
        return 1; // weiter aufzählen
    }
}
