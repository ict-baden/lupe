using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Lupe.Core.Model;
using Lupe.Core.Platform;
using Lupe.Core.Screen;
using Lupe.Platform.Linux.Native;
using Lupe.Platform.Linux.Portal;
using SkiaSharp;
using Tmds.DBus.Protocol;

namespace Lupe.Platform.Linux.Screen;

/// <summary>
/// Bildschirmfotos unter Linux. X11: <c>XGetImage</c> auf das Wurzelfenster, Monitore über XRandR. Wayland: das
/// Screenshot-Portal (ohne Rückfrage-Dialog, <c>interactive=false</c>) – das liefert immer den ganzen Desktop; der
/// Ausschnitt wird danach herausgeschnitten.
/// </summary>
/// <remarks>
/// Koordinaten sind die des X-Servers (links oben im Gesamtbild = 0,0) bzw. unter Wayland die Pixel des Portal-Fotos.
/// Anders als unter Windows muss der Hauptmonitor also nicht bei 0,0 beginnen.
/// </remarks>
public sealed class LinuxScreenCapture : IScreenCapture
{
    private readonly Lock _gate = new();
    private nint _display;          // unter _gate
    private string? _displayError;
    private PixelRect? _lastPortalSize;

    public IReadOnlyList<MonitorArea> Monitors()
    {
        lock (_gate)
        {
            if (Display() is not { } d) return WaylandFallbackMonitors();
            var monitors = X11Monitors(d);
            if (LinuxSession.Kind == SessionKind.Wayland && _lastPortalSize is { } shot)
                monitors = ScaleTo(monitors, Union(monitors), shot);
            return monitors;
        }
    }

    public PixelRect VirtualScreen
    {
        get
        {
            var monitors = Monitors();
            return monitors.Count == 0 ? _lastPortalSize ?? default : Union(monitors);
        }
    }

    public bool TryCapture(PixelRect area, [NotNullWhen(true)] out PixelImage? image, out string? error)
    {
        if (LinuxSession.Kind == SessionKind.Wayland) return TryCapturePortal(area, out image, out error);
        lock (_gate)
        {
            if (Display() is not { } d)
            {
                image = null;
                error = $"Bildschirmfotos gehen nicht: {_displayError}";
                return false;
            }
            return TryCaptureX11(d, area, out image, out error);
        }
    }

    private nint? Display()
    {
        if (_display != 0) return _display;
        if (_displayError is not null) return null;
        _display = X11.TryOpen(out _displayError);
        return _display == 0 ? null : _display;
    }

    // ---------- X11 ----------

    internal static unsafe IReadOnlyList<MonitorArea> X11Monitors(nint display)
    {
        nint root = X11.XDefaultRootWindow(display);
        int screen = X11.XDefaultScreen(display);
        var rootRect = new PixelRect(0, 0, X11.XDisplayWidth(display, screen), X11.XDisplayHeight(display, screen));
        var work = WorkArea(display, root) ?? rootRect;
        var list = new List<MonitorArea>();
        try
        {
            XRRMonitorInfo* infos = X11.XRRGetMonitors(display, root, 1, out int count);
            if (infos != null)
            {
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        var m = infos[i];
                        if (m.width <= 0 || m.height <= 0) continue;
                        var bounds = new PixelRect(m.x, m.y, m.width, m.height);
                        list.Add(new MonitorArea(bounds, Intersect(bounds, work) ?? bounds, 1.0, m.primary != 0,
                            AtomName(display, m.name) ?? $"Bildschirm {i + 1}"));
                    }
                }
                finally
                {
                    X11.XRRFreeMonitors(infos);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Ohne libXrandr: ein Monitor über den ganzen Bildschirm
        }
        if (list.Count == 0) list.Add(new MonitorArea(rootRect, Intersect(rootRect, work) ?? rootRect, 1.0, true, "Bildschirm"));
        if (!list.Any(m => m.IsPrimary)) list[0] = list[0] with { IsPrimary = true };
        return list.OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ToList();
    }

    private static unsafe string? AtomName(nint display, nint atom)
    {
        if (atom == 0) return null;
        nint name = X11.XGetAtomName(display, atom);
        if (name == 0) return null;
        try { return Marshal.PtrToStringUTF8(name); }
        finally { X11.XFree((void*)name); }
    }

    /// <summary>Arbeitsfläche ohne Leisten (_NET_WORKAREA des Fenstermanagers), sonst null.</summary>
    private static PixelRect? WorkArea(nint display, nint root)
    {
        var values = X11.GetLongProperty(display, root, "_NET_WORKAREA", X11.XA_CARDINAL);
        if (values.Length < 4 || values[2] <= 0 || values[3] <= 0) return null;
        return new PixelRect((int)values[0], (int)values[1], (int)values[2], (int)values[3]);
    }

    internal static unsafe bool TryCaptureX11(nint display, PixelRect area, [NotNullWhen(true)] out PixelImage? image, out string? error)
    {
        image = null;
        error = null;
        int screen = X11.XDefaultScreen(display);
        var rootRect = new PixelRect(0, 0, X11.XDisplayWidth(display, screen), X11.XDisplayHeight(display, screen));
        var bgra = new byte[checked(area.Width * area.Height * 4)];
        for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 255; // außerhalb schwarz
        if (Intersect(area, rootRect) is not { } part)
        {
            image = new PixelImage(area.Width, area.Height, bgra);
            return true;
        }

        XImage* xi = null;
        int code = X11Errors.Trap(display, () =>
            xi = X11.XGetImage(display, X11.XDefaultRootWindow(display), part.X, part.Y, (uint)part.Width, (uint)part.Height, nuint.MaxValue, X11.ZPixmap));
        if (xi == null)
        {
            error = code != 0 ? $"Der X-Server hat das Bildschirmfoto abgelehnt (Fehler {code})." : "Der X-Server hat kein Bildschirmfoto geliefert.";
            return false;
        }
        try
        {
            if (!CopyPixels(*xi, bgra, area.Width, part.X - area.X, part.Y - area.Y, out error)) return false;
        }
        finally
        {
            if (xi->data != 0) X11.XFree((void*)xi->data);
            X11.XFree(xi);
        }
        image = new PixelImage(area.Width, area.Height, bgra);
        return true;
    }

    /// <summary>Kopiert ein ZPixmap-XImage (24/32 Bit, beliebige 8-Bit-Farbmasken) an die Stelle im BGRA-Ziel.</summary>
    private static unsafe bool CopyPixels(in XImage xi, byte[] dst, int dstWidth, int dx, int dy, out string? error)
    {
        error = null;
        if (xi.bits_per_pixel != 32 && xi.bits_per_pixel != 24)
        {
            error = $"Die Farbtiefe des Bildschirms ({xi.depth} Bit) wird nicht unterstützt.";
            return false;
        }
        int rs = Shift((ulong)xi.red_mask), gs = Shift((ulong)xi.green_mask), bs = Shift((ulong)xi.blue_mask);
        int bpp = xi.bits_per_pixel / 8;
        byte* src = (byte*)xi.data;
        bool bgrx = bpp == 4 && xi.byte_order == 0 && rs == 16 && gs == 8 && bs == 0;
        for (int y = 0; y < xi.height; y++)
        {
            byte* line = src + (long)y * xi.bytes_per_line;
            int o = ((dy + y) * dstWidth + dx) * 4;
            if (bgrx)
            {
                new ReadOnlySpan<byte>(line, xi.width * 4).CopyTo(dst.AsSpan(o, xi.width * 4));
                for (int x = 0; x < xi.width; x++) dst[o + x * 4 + 3] = 255;
                continue;
            }
            for (int x = 0; x < xi.width; x++, o += 4)
            {
                byte* p = line + x * bpp;
                uint pixel = bpp == 4
                    ? (xi.byte_order == 0 ? (uint)(p[0] | p[1] << 8 | p[2] << 16 | p[3] << 24) : (uint)(p[3] | p[2] << 8 | p[1] << 16 | p[0] << 24))
                    : (xi.byte_order == 0 ? (uint)(p[0] | p[1] << 8 | p[2] << 16) : (uint)(p[2] | p[1] << 8 | p[0] << 16));
                dst[o] = (byte)(pixel >> bs);
                dst[o + 1] = (byte)(pixel >> gs);
                dst[o + 2] = (byte)(pixel >> rs);
                dst[o + 3] = 255;
            }
        }
        return true;
    }

    private static int Shift(ulong mask)
    {
        if (mask == 0) return 0;
        int s = System.Numerics.BitOperations.TrailingZeroCount(mask);
        // Masken mit mehr als 8 Bit (z. B. 10 Bit je Farbe): die obersten 8 nehmen
        int bits = System.Numerics.BitOperations.PopCount(mask);
        return s + Math.Max(0, bits - 8);
    }

    // ---------- Wayland ----------

    private IReadOnlyList<MonitorArea> WaylandFallbackMonitors() =>
        _lastPortalSize is { } shot ? [new MonitorArea(shot, shot, 1.0, true, "Bildschirm")] : [];

    private bool TryCapturePortal(PixelRect area, [NotNullWhen(true)] out PixelImage? image, out string? error)
    {
        image = null;
        PixelRect assumed = VirtualScreen;
        SKBitmap shot;
        try
        {
            shot = Task.Run(() => PortalScreenshot.TakeAsync()).GetAwaiter().GetResult();
        }
        catch (PortalException ex)
        {
            error = ex.Message;
            return false;
        }
        using (shot)
        {
            var size = new PixelRect(0, 0, shot.Width, shot.Height);
            lock (_gate) _lastPortalSize = size;
            if (assumed.Width <= 0 || assumed.Height <= 0) assumed = size;
            image = Crop(shot, assumed, area);
            error = null;
            return true;
        }
    }

    /// <summary>
    /// Schneidet <paramref name="area"/> (in den Koordinaten von <paramref name="space"/>) aus dem Foto; passt das Foto nicht
    /// zur angenommenen Fläche (Skalierung), wird umgerechnet. Ergebnis immer genau so groß wie <paramref name="area"/>.
    /// </summary>
    internal static PixelImage Crop(SKBitmap shot, PixelRect space, PixelRect area)
    {
        double sx = (double)shot.Width / space.Width, sy = (double)shot.Height / space.Height;
        var src = SKRectI.Create((int)Math.Round((area.X - space.X) * sx), (int)Math.Round((area.Y - space.Y) * sy),
            Math.Max(1, (int)Math.Round(area.Width * sx)), Math.Max(1, (int)Math.Round(area.Height * sy)));
        var info = new SKImageInfo(area.Width, area.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var target = new SKBitmap(info);
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(SKColors.Black);
            using var image = SKImage.FromBitmap(shot);
            canvas.DrawImage(image, src, SKRect.Create(area.Width, area.Height), new SKSamplingOptions(SKFilterMode.Linear));
        }
        return Camera.FrameConversion.FromBitmap(target);
    }

    // ---------- Hilfen ----------

    internal static PixelRect Union(IReadOnlyList<MonitorArea> monitors)
    {
        if (monitors.Count == 0) return default;
        int l = monitors.Min(m => m.Bounds.X), t = monitors.Min(m => m.Bounds.Y);
        int r = monitors.Max(m => m.Bounds.X + m.Bounds.Width), b = monitors.Max(m => m.Bounds.Y + m.Bounds.Height);
        return new PixelRect(l, t, r - l, b - t);
    }

    internal static PixelRect? Intersect(PixelRect a, PixelRect b)
    {
        int l = Math.Max(a.X, b.X), t = Math.Max(a.Y, b.Y);
        int r = Math.Min(a.X + a.Width, b.X + b.Width), bo = Math.Min(a.Y + a.Height, b.Y + b.Height);
        return r > l && bo > t ? new PixelRect(l, t, r - l, bo - t) : null;
    }

    /// <summary>Monitore aus XWayland (logische Pixel) auf die Pixel des Portal-Fotos umrechnen.</summary>
    internal static IReadOnlyList<MonitorArea> ScaleTo(IReadOnlyList<MonitorArea> monitors, PixelRect from, PixelRect to)
    {
        if (from.Width <= 0 || from.Height <= 0 || (from.Width == to.Width && from.Height == to.Height)) return monitors;
        double sx = (double)to.Width / from.Width, sy = (double)to.Height / from.Height;
        PixelRect S(PixelRect r) => new((int)Math.Round((r.X - from.X) * sx), (int)Math.Round((r.Y - from.Y) * sy),
            (int)Math.Round(r.Width * sx), (int)Math.Round(r.Height * sy));
        return monitors.Select(m => m with { Bounds = S(m.Bounds), WorkArea = S(m.WorkArea), Scale = m.Scale * sx }).ToList();
    }
}

/// <summary>Das Screenshot-Portal: ein Foto des ganzen Desktops ohne Rückfrage, als PNG-Datei, die gleich wieder gelöscht wird.</summary>
internal static class PortalScreenshot
{
    public static async Task<SKBitmap> TakeAsync(CancellationToken ct = default)
    {
        using var portal = await PortalConnection.ConnectAsync(ct).ConfigureAwait(false);
        var options = new Dictionary<string, VariantValue> { ["interactive"] = VariantValue.Bool(false) };
        var response = await portal.RequestAsync("org.freedesktop.portal.Screenshot", "Screenshot", "sa{sv}",
            (ref MessageWriter w) => w.WriteString(""), options, TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
        if (!response.Ok)
            throw new PortalException(response.Code == 1
                ? "Das Bildschirmfoto wurde abgelehnt – in den Systemeinstellungen (Datenschutz → Bildschirmfotos) Lupe erlauben."
                : "Das Desktop-Portal hat kein Bildschirmfoto geliefert.");
        string? uri = response.GetString("uri");
        if (uri is null || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !parsed.IsFile)
            throw new PortalException("Das Desktop-Portal hat kein Bildschirmfoto geliefert.");
        string path = parsed.LocalPath;
        try
        {
            var bitmap = SKBitmap.Decode(path, DecodeInfo(path));
            return bitmap ?? throw new PortalException("Das Bildschirmfoto des Desktop-Portals ließ sich nicht lesen.");
        }
        finally
        {
            // Das Portal legt das Foto (z. B. unter ~/Bilder) ab – Lupe braucht es nur kurz
            try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static SKImageInfo DecodeInfo(string path)
    {
        using var codec = SKCodec.Create(path);
        if (codec is null) throw new PortalException("Das Bildschirmfoto des Desktop-Portals ließ sich nicht lesen.");
        return new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
    }
}
