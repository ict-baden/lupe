using System.Diagnostics;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using Lupe.Core.Decoding;
using Lupe.Core.Input;
using Lupe.Core.Model;
using Lupe.Interop;

namespace Lupe.Services;

/// <summary>Kurzer Ton bei einem Treffer – ein eigener heller „Piep“ wie an der Kasse, sonst der Windows-Hinweiston.</summary>
internal static class Beep
{
    private static readonly Lazy<SoundPlayer?> Player = new(Create);

    public static void Play()
    {
        try
        {
            if (Player.Value is { } player) player.Play();
            else SystemSounds.Asterisk.Play();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException or COMException)
        {
            // Kein Audiogerät (z. B. in einer Dienst-Sitzung) – dann eben ohne Ton
        }
    }

    private static SoundPlayer? Create()
    {
        try
        {
            var player = new SoundPlayer(new MemoryStream(CreateWave()));
            player.Load();
            return player;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
        {
            return null;
        }
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

/// <summary>Zwischenablage und Browser – mit Fehlermeldung statt Ausnahme.</summary>
internal static class Shell
{
    /// <summary>Text in die Zwischenablage. Fehlermeldung oder null.</summary>
    public static string? Copy(string text)
    {
        // Andere Programme halten die Zwischenablage manchmal kurz offen. WPF versucht es dann selbst schon
        // mehrmals; ein zweiter Anlauf genügt, länger soll die Oberfläche nicht stehen.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return null;
            }
            catch (ExternalException ex)
            {
                if (attempt >= 1) return $"Die Zwischenablage ist belegt ({ex.Message}).";
                Thread.Sleep(40);
            }
        }
    }

    /// <summary>Öffnet einen http(s)-Link im Standardbrowser. Andere Schemas werden nie geöffnet. Fehlermeldung oder null.</summary>
    public static string? OpenLink(Uri link)
    {
        // LinkRules liefert nur http(s) – hier zur Sicherheit noch einmal geprüft, bevor die Shell etwas startet
        if (!link.IsAbsoluteUri || (link.Scheme != Uri.UriSchemeHttps && link.Scheme != Uri.UriSchemeHttp) || link.IsUnc)
            return "Nur http- und https-Links werden geöffnet.";
        try
        {
            Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return $"Der Browser ließ sich nicht öffnen: {ex.Message}";
        }
    }

    /// <summary>Ordner im Explorer zeigen (Datei markiert, wenn es sie gibt).</summary>
    public static string? ShowInExplorer(string path)
    {
        try
        {
            var arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{(Directory.Exists(path) ? path : Path.GetDirectoryName(path))}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true })?.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return $"Der Ordner ließ sich nicht öffnen: {ex.Message}";
        }
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
        if (target == 0 || !Native.IsWindow(target)) return "Das Fenster von vorher gibt es nicht mehr.";
        if (Native.IsIconic(target)) Native.ShowWindow(target, Native.SW_RESTORE);
        Native.SetForegroundWindow(target);
        for (int i = 0; i < 20 && Native.GetForegroundWindow() != target; i++)
        {
            await Task.Delay(25);
            if (i == 8) Native.SetForegroundWindow(target);
        }
        if (Native.GetForegroundWindow() != target) return "Das Fenster von vorher ließ sich nicht aktivieren – nichts eingetippt.";
        try
        {
            // Wartet im Core, bis Strg/Alt des Tastenkürzels losgelassen sind – nicht auf dem Oberflächen-Thread. TryType statt
            // Type: Verwirft Windows die Eingabe (Zielfenster als Administrator, gesperrter Desktop), steht der Grund in der Meldung.
            return await Task.Run(() => KeyboardTyper.TryType(text, suffix, delayMs, out string? error) ? null : error ?? "Eintippen fehlgeschlagen.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return $"Eintippen fehlgeschlagen: {ex.Message}";
        }
    }
}

/// <summary>Was die Zwischenablage an Bild hergibt – genau eins davon ist gesetzt, oder nichts.</summary>
/// <param name="File">Pfad einer im Explorer kopierten Bilddatei.</param>
/// <param name="Png">PNG-Daten (legen Browser, Office und Snipping Tool zusätzlich ab – verlustfrei und mit Alpha).</param>
/// <param name="Bitmap">Sonst die Bitmap der Zwischenablage.</param>
internal sealed record ClipboardContent(string? File = null, byte[]? Png = null, PixelImage? Bitmap = null)
{
    public bool IsEmpty => File is null && Png is null && Bitmap is null;
}

/// <summary>Bild aus der Zwischenablage: PNG-Daten, Bitmap oder eine im Explorer kopierte Bilddatei.</summary>
internal static class ClipboardImage
{
    /// <summary>Liest die Zwischenablage (Oberflächen-Thread). Die PNG-Daten lädt danach <see cref="ImageFile"/> wie eine Datei.</summary>
    public static ClipboardContent Read()
    {
        var data = Clipboard.GetDataObject();
        if (data is null) return new();

        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var image = files.FirstOrDefault(IsImageFile);
            if (image is not null) return new(File: image);
        }

        if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream { Length: > 0 } png) return new(Png: png.ToArray());

        return ReadBitmap() is { } bitmap ? new(Bitmap: bitmap) : new();
    }

    /// <summary>Die Bitmap der Zwischenablage (auch als Ersatz, wenn die PNG-Daten unbrauchbar sind), sonst null.</summary>
    public static PixelImage? ReadBitmap() =>
        Clipboard.ContainsData(DataFormats.Bitmap) && Clipboard.GetImage() is { } bitmap ? Images.FromBitmap(bitmap) : null;

    public static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return false;
        try { return ImageFile.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ext.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff";
        }
    }
}
