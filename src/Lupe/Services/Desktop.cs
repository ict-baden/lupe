// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Lupe.Core.Decoding;
using Lupe.Core.Input;
using Lupe.Core.Model;
using Lupe.Core.Resources;
using Lupe.Core.Platform;
using Lupe.Interop;

namespace Lupe.Services;

/// <summary>
/// Kurzer Ton bei einem Treffer – ein eigener heller „Piep“ wie an der Kasse. Windows: aus dem Speicher (winmm);
/// Linux: paplay/pw-play/aplay; macOS: afplay. Fehlt ein Abspieler oder ein Audiogerät, eben ohne Ton.
/// </summary>
internal static class Beep
{
    private static readonly Lazy<byte[]> Wave = new(CreateWave);
    private static GCHandle _pinned;
    private static string? _file;

    public static void Play()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // SND_ASYNC liest aus dem Speicher, während Lupe weiterläuft – der Puffer bleibt deshalb fest angeheftet
                if (!_pinned.IsAllocated) _pinned = GCHandle.Alloc(Wave.Value, GCHandleType.Pinned);
                Native.PlaySound(_pinned.AddrOfPinnedObject(), 0, Native.SND_MEMORY | Native.SND_ASYNC | Native.SND_NODEFAULT);
                return;
            }
            _file ??= WriteTempWave();
            string[] players = OperatingSystem.IsMacOS() ? ["/usr/bin/afplay"] : ["paplay", "pw-play", "aplay"];
            foreach (var player in players)
            {
                if (!OperatingSystem.IsMacOS() && FindOnPath(player) is null) continue;
                var info = new ProcessStartInfo(OperatingSystem.IsMacOS() ? player : FindOnPath(player)!) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
                if (player == "aplay") info.ArgumentList.Add("-q");
                info.ArgumentList.Add(_file);
                Process.Start(info)?.Dispose();
                return;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            // Kein Audiogerät oder kein Abspieler – dann eben ohne Ton
        }
    }

    /// <summary>
    /// Der Ton als Datei für den Abspieler – in einem eigenen, neu angelegten Ordner (unter Linux und macOS nur für den
    /// Benutzer, 0700), nicht unter einem festen Namen im gemeinsamen /tmp: Dort könnte ein anderer Benutzer vorher einen
    /// Symlink anlegen, und Lupe überschriebe dessen Ziel mit dem Ton. Der Ordner wird beim Beenden entfernt.
    /// </summary>
    internal static string WriteTempWave()
    {
        var folder = Directory.CreateTempSubdirectory("lupe-");
        var path = Path.Combine(folder.FullName, "treffer.wav");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(Wave.Value);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { folder.Delete(recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        };
        return path;
    }

    private static string? FindOnPath(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>WAV im Speicher: 90 ms bei 1900 Hz, weich ein- und ausgeblendet, 16 Bit mono.</summary>
    internal static byte[] CreateWave()
    {
        const int rate = 44100;
        const double seconds = 0.09, frequency = 1900, volume = 0.22;
        int samples = (int)(rate * seconds);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + samples * 2);
        w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);
        w.Write((short)1);      // PCM
        w.Write((short)1);      // mono
        w.Write(rate);
        w.Write(rate * 2);      // Bytes je Sekunde
        w.Write((short)2);      // Block
        w.Write((short)16);     // Bit
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(samples * 2);
        int fade = rate / 200; // 5 ms
        for (int i = 0; i < samples; i++)
        {
            double envelope = Math.Min(1, Math.Min(i, samples - 1 - i) / (double)fade);
            w.Write((short)(Math.Sin(2 * Math.PI * frequency * i / rate) * volume * envelope * short.MaxValue));
        }
        w.Flush();
        return ms.ToArray();
    }
}

/// <summary>Zwischenablage, Browser und Dateimanager – mit Fehlermeldung statt Ausnahme.</summary>
internal static class Shell
{
    private static Window? _clipboardHost;

    /// <summary>
    /// Ein Fenster für die Zwischenablage (Avalonia erreicht sie nur über ein Fenster): ein offenes von Lupe, sonst ein
    /// unsichtbares – im Infobereich ohne Hauptfenster gibt es sonst keins.
    /// </summary>
    internal static IClipboard? Clipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { Windows: { Count: > 0 } windows }
            && windows.FirstOrDefault(w => w.IsVisible) is { Clipboard: { } visible })
            return visible;
        _clipboardHost ??= new Window { ShowInTaskbar = false, Title = Strings.App_Shell_ClipboardWindowTitle };
        return _clipboardHost.Clipboard;
    }

    /// <summary>Text in die Zwischenablage. Fehlermeldung oder null.</summary>
    public static async Task<string?> CopyAsync(string text)
    {
        // Andere Programme halten die Zwischenablage manchmal kurz offen – ein zweiter Anlauf genügt
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (Clipboard() is not { } clipboard) return Strings.App_Shell_ClipboardUnavailable;
                await clipboard.SetTextAsync(text);
                return null;
            }
            catch (Exception ex) when (ex is ExternalException or InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException)
            {
                if (attempt >= 1) return string.Format(Strings.App_Shell_ClipboardBusy, ex.Message);
                await Task.Delay(40);
            }
        }
    }

    /// <summary>Öffnet einen http(s)-Link im Standardbrowser. Andere Schemas werden nie geöffnet. Fehlermeldung oder null.</summary>
    public static string? OpenLink(Uri link)
    {
        // LinkRules liefert nur http(s) – hier zur Sicherheit noch einmal geprüft, bevor die Shell etwas startet
        if (!link.IsAbsoluteUri || (link.Scheme != Uri.UriSchemeHttps && link.Scheme != Uri.UriSchemeHttp) || link.IsUnc)
            return Strings.App_Shell_OnlyHttpLinks;
        try
        {
            Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return string.Format(Strings.App_Shell_BrowserFailed, ex.Message);
        }
    }

    /// <summary>Ordner im Dateimanager zeigen (Datei markiert, wenn es sie gibt und das System das kann).</summary>
    public static string? ShowInExplorer(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{(Directory.Exists(path) ? path : Path.GetDirectoryName(path))}\"";
                Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true })?.Dispose();
            }
            else if (OperatingSystem.IsMacOS())
            {
                var info = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
                if (File.Exists(path)) info.ArgumentList.Add("-R");
                info.ArgumentList.Add(path);
                Process.Start(info)?.Dispose();
            }
            else
            {
                var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
                var info = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                info.ArgumentList.Add(folder);
                Process.Start(info)?.Dispose();
            }
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return string.Format(Strings.App_Shell_FolderFailed, ex.Message);
        }
    }

    /// <summary>Öffnet eine Datei mit dem zugehörigen Programm (Lizenztexte im Texteditor).</summary>
    public static void OpenFile(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            var info = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            info.ArgumentList.Add(path);
            Process.Start(info)?.Dispose();
        }
        else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }
}

/// <summary>Eintippen in ein anderes Fenster (das, das vor dem Bildschirm-Scan vorn war).</summary>
internal static class Typing
{
    /// <summary>
    /// Holt <paramref name="target"/> nach vorn und tippt den Text. Kommt das Fenster nicht in den Vordergrund,
    /// wird nichts getippt – sonst landete der Inhalt in einem fremden Fenster. Fehlermeldung oder null.
    /// </summary>
    public static async Task<string?> TypeIntoAsync(nint target, string text, string suffix, int delayMs)
    {
        if (await LupePlatform.Current.ActiveWindow.ActivateAsync(target) is { } notActive) return notActive;
        try
        {
            // Wartet im Core, bis Strg/Alt des Tastenkürzels losgelassen sind – nicht auf dem Oberflächen-Thread. TryType statt
            // Type: Verwirft das System die Eingabe (Zielfenster mit höheren Rechten, gesperrter Desktop), steht der Grund in der Meldung.
            // Kommt beim Tippen ein anderes Fenster nach vorn, bricht es ab. 0 (unbekannt, kurz beim Fensterwechsel) zählt nicht als Wechsel.
            var active = LupePlatform.Current.ActiveWindow;
            Func<bool>? stillOnTarget = target != TypeTargets.FocusedWindow && active.IsSupported
                ? () => active.Foreground() is var now && (now == 0 || now == target)
                : null;
            return await Task.Run(() => KeyboardTyper.TryType(text, suffix, delayMs, out string? error, stillOnTarget: stillOnTarget)
                ? null : error ?? Strings.App_Shell_TypingFailed);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return string.Format(Strings.App_Shell_TypingFailedWithMessage, ex.Message);
        }
    }
}

/// <summary>Was die Zwischenablage an Bild hergibt – genau eins davon ist gesetzt, oder nichts.</summary>
/// <param name="File">Pfad einer im Dateimanager kopierten Bilddatei.</param>
/// <param name="Png">PNG-Daten (legen Browser, Office und Snipping Tool zusätzlich ab – verlustfrei und mit Alpha).</param>
/// <param name="Bitmap">Sonst die Bitmap der Zwischenablage.</param>
internal sealed record ClipboardContent(string? File = null, byte[]? Png = null, PixelImage? Bitmap = null)
{
    public bool IsEmpty => File is null && Png is null && Bitmap is null;
}

/// <summary>Bild aus der Zwischenablage: kopierte Bilddatei, PNG-Daten oder Bitmap.</summary>
internal static class ClipboardImage
{
    // Windows legt PNG-Daten unter „PNG“ ab, X11/Wayland unter dem MIME-Typ, macOS als „public.png“
    private static readonly DataFormat<byte[]>[] PngFormats =
    [
        DataFormat.CreateBytesPlatformFormat(OperatingSystem.IsWindows() ? "PNG" : OperatingSystem.IsMacOS() ? "public.png" : "image/png"),
    ];

    /// <summary>Liest die Zwischenablage. Die PNG-Daten lädt danach <see cref="ImageFile"/> wie eine Datei.</summary>
    public static async Task<ClipboardContent> ReadAsync()
    {
        if (Shell.Clipboard() is not { } clipboard) return new();
        var formats = await clipboard.GetDataFormatsAsync();
        if (formats.Contains(DataFormat.File) && await clipboard.TryGetFilesAsync() is { } files)
        {
            var image = files.Select(f => f.TryGetLocalPath()).OfType<string>().FirstOrDefault(IsImageFile);
            if (image is not null) return new(File: image);
        }
        foreach (var png in PngFormats)
        {
            if (!formats.Contains(png)) continue;
            if (await clipboard.TryGetValueAsync(png) is { Length: > 0 } bytes) return new(Png: bytes);
        }
        return await ReadBitmapAsync() is { } bitmap ? new(Bitmap: bitmap) : new();
    }

    /// <summary>Die Bitmap der Zwischenablage (auch als Ersatz, wenn die PNG-Daten unbrauchbar sind), sonst null.</summary>
    public static async Task<PixelImage?> ReadBitmapAsync()
    {
        if (Shell.Clipboard() is not { } clipboard) return null;
        using var bitmap = await clipboard.TryGetBitmapAsync();
        return bitmap is null ? null : Images.FromBitmap(bitmap);
    }

    public static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return false;
        try
        {
            var known = ImageFile.Extensions;
            if (known.Count > 0) return known.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
        return ext.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".webp";
    }
}
