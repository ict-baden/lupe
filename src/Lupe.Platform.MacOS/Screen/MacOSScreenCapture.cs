using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Lupe.Core.Model;
using Lupe.Core.Platform;
using Lupe.Core.Resources;
using Lupe.Core.Screen;
using Lupe.Platform.MacOS.Imaging;
using Lupe.Platform.MacOS.Native;

namespace Lupe.Platform.MacOS.Screen;

/// <summary>Ein Bildschirm, wie CoreGraphics ihn meldet: Lage in Punkten (globale Koordinaten, Hauptbildschirm bei 0,0, y nach unten), Größe in Pixeln.</summary>
/// <param name="Index">Reihenfolge von <c>CGGetActiveDisplayList</c> (0 = Hauptbildschirm); <c>screencapture -D</c> zählt ab 1 genauso.</param>
internal sealed record RawDisplay(uint Id, int Index, double X, double Y, double Width, double Height, int PixelWidth, int PixelHeight, bool IsMain);

/// <summary>Ein Bildschirm im Pixelraum von Lupe.</summary>
internal sealed record MacMonitor(RawDisplay Display, MonitorArea Area);

/// <summary>
/// Rechnet die Bildschirme in einen gemeinsamen Pixelraum um. macOS legt Bildschirme in Punkten an; Lupe arbeitet in
/// Pixeln. Gemeinsamer Faktor ist der größte Skalierungsfaktor aller Bildschirme (Retina meist 2): Punkte × Faktor.
/// So überlappt nichts, und auf Bildschirmen mit kleinerem Faktor wird das Foto entsprechend hochgerechnet.
/// </summary>
internal static class DisplayLayout
{
    public static double CommonScale(IReadOnlyList<RawDisplay> displays)
    {
        double scale = 1;
        foreach (var d in displays)
            if (d.Width > 0 && d.PixelWidth > 0) scale = Math.Max(scale, Math.Round(d.PixelWidth / d.Width * 4) / 4);
        return scale;
    }

    public static IReadOnlyList<MacMonitor> Build(IReadOnlyList<RawDisplay> displays)
    {
        double scale = CommonScale(displays);
        var result = new List<MacMonitor>(displays.Count);
        foreach (var d in displays.Where(d => d.Width > 0 && d.Height > 0).OrderBy(d => d.X).ThenBy(d => d.Y))
        {
            int x = (int)Math.Round(d.X * scale), y = (int)Math.Round(d.Y * scale);
            int r = (int)Math.Round((d.X + d.Width) * scale), b = (int)Math.Round((d.Y + d.Height) * scale);
            var bounds = new PixelRect(x, y, r - x, b - y);
            string name = string.Format(d.IsMain ? Strings.Mac_Screen_MainMonitorNumbered : Strings.Mac_Screen_MonitorNumbered, d.Index + 1);
            result.Add(new MacMonitor(d, new MonitorArea(bounds, bounds, scale, d.IsMain, name)));
        }
        return result;
    }

    public static PixelRect Union(IEnumerable<PixelRect> rects)
    {
        bool any = false;
        int l = 0, t = 0, r = 0, b = 0;
        foreach (var rect in rects)
        {
            if (!any)
            {
                (l, t, r, b) = (rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height);
                any = true;
                continue;
            }
            l = Math.Min(l, rect.X);
            t = Math.Min(t, rect.Y);
            r = Math.Max(r, rect.X + rect.Width);
            b = Math.Max(b, rect.Y + rect.Height);
        }
        return any ? new PixelRect(l, t, r - l, b - t) : default;
    }

    /// <summary>
    /// Kopiert den Teil von <paramref name="shot"/> (Foto des ganzen Bildschirms <paramref name="monitor"/>), der in
    /// <paramref name="area"/> liegt, an die passende Stelle von <paramref name="target"/> (BGRA, Größe von area).
    /// Hat das Foto eine andere Größe als der Bildschirm im Pixelraum, wird nächstgelegen skaliert.
    /// </summary>
    public static void Blit(PixelImage shot, PixelRect monitor, PixelRect area, byte[] target)
    {
        int l = Math.Max(area.X, monitor.X), t = Math.Max(area.Y, monitor.Y);
        int r = Math.Min(area.X + area.Width, monitor.X + monitor.Width), b = Math.Min(area.Y + area.Height, monitor.Y + monitor.Height);
        if (r <= l || b <= t || monitor.Width <= 0 || monitor.Height <= 0) return;
        bool same = shot.Width == monitor.Width && shot.Height == monitor.Height;
        int targetStride = area.Width * 4;
        for (int y = t; y < b; y++)
        {
            int sy = same ? y - monitor.Y : Math.Min(shot.Height - 1, (int)((long)(y - monitor.Y) * shot.Height / monitor.Height));
            int dst = (y - area.Y) * targetStride + (l - area.X) * 4;
            if (same)
            {
                Buffer.BlockCopy(shot.Bgra, sy * shot.Stride + (l - monitor.X) * 4, target, dst, (r - l) * 4);
                continue;
            }
            for (int x = l; x < r; x++, dst += 4)
            {
                int sx = Math.Min(shot.Width - 1, (int)((long)(x - monitor.X) * shot.Width / monitor.Width));
                int src = sy * shot.Stride + sx * 4;
                target[dst] = shot.Bgra[src];
                target[dst + 1] = shot.Bgra[src + 1];
                target[dst + 2] = shot.Bgra[src + 2];
                target[dst + 3] = 255;
            }
        }
    }
}

/// <summary>
/// Bildschirmfotos unter macOS mit <c>/usr/sbin/screencapture -x</c> (je betroffenem Bildschirm ein PNG, gelesen mit
/// ImageIO). <c>CGDisplayCreateImage</c> gibt es ab macOS 15 nicht mehr. Ohne die Berechtigung „Bildschirmaufnahme“
/// liefert macOS nur den Schreibtischhintergrund – deshalb wird vorher geprüft und sonst nach der Berechtigung gefragt.
/// </summary>
public sealed class MacOSScreenCapture : IScreenCapture
{
    internal const string Tool = "/usr/sbin/screencapture";
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(15);

    public IReadOnlyList<MonitorArea> Monitors() => Layout().Select(m => m.Area).ToList();

    public PixelRect VirtualScreen => DisplayLayout.Union(Monitors().Select(m => m.Bounds));

    public bool TryCapture(PixelRect area, [NotNullWhen(true)] out PixelImage? image, out string? error)
    {
        image = null;
        if (!OperatingSystem.IsMacOS())
        {
            error = Strings.Mac_NotHere_Screenshots;
            return false;
        }
        if (!MacOSPermissions.ScreenCapture())
        {
            MacOSPermissions.RequestScreenCapture();
            error = MacOSPermissions.ScreenDenied;
            return false;
        }
        IReadOnlyList<MacMonitor> monitors;
        try
        {
            monitors = Layout();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            error = Strings.Mac_Screen_CoreGraphicsMissing;
            return false;
        }
        if (monitors.Count == 0)
        {
            error = Strings.Mac_Screen_NoDisplay;
            return false;
        }

        var pixels = new byte[checked(area.Width * area.Height * 4)];
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255; // außerhalb der Bildschirme schwarz
        foreach (var monitor in monitors)
        {
            var b = monitor.Area.Bounds;
            bool hits = area.X < b.X + b.Width && b.X < area.X + area.Width && area.Y < b.Y + b.Height && b.Y < area.Y + area.Height;
            if (!hits) continue;
            if (!CaptureDisplay(monitor.Display, out var shot, out error)) return false;
            DisplayLayout.Blit(shot, b, area, pixels);
        }
        image = new PixelImage(area.Width, area.Height, pixels);
        error = null;
        return true;
    }

    /// <summary>Argumente für <c>screencapture</c>: ohne Ton (-x), ohne Mauszeiger, nur der Bildschirm (-D, ab 1), PNG.</summary>
    internal static IReadOnlyList<string> Arguments(RawDisplay display, string file) =>
        ["-x", "-t", "png", "-D", (display.Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), file];

    private static bool CaptureDisplay(RawDisplay display, [NotNullWhen(true)] out PixelImage? shot, out string? error)
    {
        shot = null;
        string file = Path.Combine(Path.GetTempPath(), $"lupe-{Environment.ProcessId}-{Guid.NewGuid():N}.png");
        try
        {
            var start = new ProcessStartInfo(Tool) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var arg in Arguments(display, file)) start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            if (process is null)
            {
                error = Strings.Mac_Screen_ToolStartFailed;
                return false;
            }
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(ToolTimeout))
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                error = Strings.Mac_Screen_ToolTimeout;
                return false;
            }
            if (process.ExitCode != 0 || !File.Exists(file))
            {
                string detail = stderr.Wait(1000) ? stderr.Result.Trim() : "";
                error = string.Format(Strings.Mac_Screen_ToolFailed, detail.Length > 0 ? detail : string.Format(Strings.Mac_Screen_ExitCode, process.ExitCode));
                return false;
            }
            var bytes = File.ReadAllBytes(file);
            shot = ImageIOReader.Decode(bytes, Strings.Mac_Screen_ScreenshotName, int.MaxValue, largestFrame: false);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
            or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        {
            Trace.TraceWarning($"Lupe: screencapture fehlgeschlagen: {ex}");
            error = string.Format(Strings.Mac_Screen_Failed, ex.Message);
            return false;
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static IReadOnlyList<MacMonitor> Layout()
    {
        if (!OperatingSystem.IsMacOS()) return [];
        return DisplayLayout.Build(ReadDisplays());
    }

    private static unsafe List<RawDisplay> ReadDisplays()
    {
        const uint Max = 32;
        uint* ids = stackalloc uint[(int)Max];
        uint count = 0;
        if (CG.CGGetActiveDisplayList(Max, ids, &count) != 0) return [];
        uint main = CG.CGMainDisplayID();
        var list = new List<RawDisplay>((int)count);
        for (int i = 0; i < count; i++)
        {
            uint id = ids[i];
            var bounds = CG.CGDisplayBounds(id);
            int pw = (int)Math.Round(bounds.Width), ph = (int)Math.Round(bounds.Height);
            nint mode = CG.CGDisplayCopyDisplayMode(id);
            if (mode != 0)
            {
                pw = (int)CG.CGDisplayModeGetPixelWidth(mode);
                ph = (int)CG.CGDisplayModeGetPixelHeight(mode);
                CG.CGDisplayModeRelease(mode);
            }
            list.Add(new RawDisplay(id, i, bounds.X, bounds.Y, bounds.Width, bounds.Height, pw, ph, id == main));
        }
        return list;
    }
}
