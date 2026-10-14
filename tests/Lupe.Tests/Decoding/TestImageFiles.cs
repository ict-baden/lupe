using Lupe.Core.Model;
using Windows.Graphics.Imaging;

namespace Lupe.Tests.Decoding;

/// <summary>
/// Schreibt echte Bilddateien für die Lade-Tests – über die Windows-Encoder (WinRT BitmapEncoder), ICO von Hand.
/// Jede Instanz hat ihren eigenen Temp-Ordner, der beim Dispose verschwindet.
/// </summary>
internal sealed class TestImageFiles : IDisposable
{
    public TestImageFiles()
    {
        // Eigener Ordner direkt im Temp-Verzeichnis – nach Dispose bleibt nichts zurück
        Folder = Path.Combine(Path.GetTempPath(), "Lupe.Tests-Bilder-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Folder);
    }

    public string Folder { get; }

    public string PathFor(string fileName) => Path.Combine(Folder, fileName);

    /// <summary>Kodiert ein oder mehrere Bilder (Frames) mit dem angegebenen Windows-Encoder.</summary>
    public async Task<string> WriteAsync(string fileName, Guid encoderId, ushort? exifOrientation = null, params PixelImage[] frames)
    {
        var path = PathFor(fileName);
        await using (var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            using var ras = file.AsRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(encoderId, ras);
            for (int i = 0; i < frames.Length; i++)
            {
                if (i > 0) await encoder.GoToNextFrameAsync();
                var img = frames[i];
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)img.Width, (uint)img.Height, 96, 96, Tight(img));
            }
            if (exifOrientation is { } o)
            {
                await encoder.BitmapProperties.SetPropertiesAsync(
                    [new KeyValuePair<string, BitmapTypedValue>("System.Photo.Orientation", new BitmapTypedValue(o, Windows.Foundation.PropertyType.UInt16))]);
            }
            await encoder.FlushAsync();
        }
        return path;
    }

    public Task<string> PngAsync(string fileName, PixelImage img) => WriteAsync(fileName, BitmapEncoder.PngEncoderId, null, img);

    /// <summary>Symboldatei mit mehreren Größen; jedes Bild als eingebettetes PNG (seit Windows Vista erlaubt).</summary>
    public async Task<string> IcoAsync(string fileName, params PixelImage[] images)
    {
        var pngs = new List<byte[]>();
        foreach (var img in images)
        {
            var tmp = await PngAsync($"ico-teil-{Guid.NewGuid():N}.png", img);
            pngs.Add(await File.ReadAllBytesAsync(tmp));
        }
        var path = PathFor(fileName);
        await using var file = new FileStream(path, FileMode.Create);
        using var w = new BinaryWriter(file);
        w.Write((ushort)0); // reserviert
        w.Write((ushort)1); // Typ: Symbol
        w.Write((ushort)images.Length);
        int offset = 6 + 16 * images.Length;
        for (int i = 0; i < images.Length; i++)
        {
            w.Write((byte)(images[i].Width >= 256 ? 0 : images[i].Width));
            w.Write((byte)(images[i].Height >= 256 ? 0 : images[i].Height));
            w.Write((byte)0); // Farben
            w.Write((byte)0); // reserviert
            w.Write((ushort)1); // Ebenen
            w.Write((ushort)32); // Bit je Pixel
            w.Write(pngs[i].Length);
            w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs) w.Write(png);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // Aufräumen ist Kür – eine noch offene Datei soll keinen Test scheitern lassen
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static byte[] Tight(PixelImage img)
    {
        if (img.Stride == img.Width * 4 && img.Bgra.Length == img.Width * img.Height * 4) return img.Bgra;
        var px = new byte[img.Width * img.Height * 4];
        for (int y = 0; y < img.Height; y++) Buffer.BlockCopy(img.Bgra, y * img.Stride, px, y * img.Width * 4, img.Width * 4);
        return px;
    }
}
