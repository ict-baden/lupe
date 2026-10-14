using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lupe.Core.Model;

namespace Lupe.Services;

/// <summary>Umwandlung zwischen <see cref="PixelImage"/> (Core) und WPF-Bildern.</summary>
internal static class Images
{
    /// <summary>Als WPF-Bild (eingefroren, also von jedem Thread aus nutzbar).</summary>
    public static BitmapSource ToBitmap(PixelImage image)
    {
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Bgra, image.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// WPF-Bild → BGRA. Durchsichtiges wird auf Weiß verrechnet (dunkler Code auf transparentem Grund bleibt
    /// erkennbar). Ist der Alphakanal überall 0 – das liefern manche Programme in der Zwischenablage –, gilt das
    /// Bild als undurchsichtig.
    /// </summary>
    public static PixelImage FromBitmap(BitmapSource source)
    {
        bool premultiplied = source.Format == PixelFormats.Pbgra32;
        BitmapSource bgra = source.Format == PixelFormats.Bgra32 || premultiplied ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = bgra.PixelWidth, height = bgra.PixelHeight, stride = width * 4;
        var px = new byte[stride * height];
        bgra.CopyPixels(px, stride, 0);

        bool anyAlpha = false;
        for (int i = 3; i < px.Length; i += 4)
        {
            if (px[i] != 0) { anyAlpha = true; break; }
        }
        for (int i = 0; i < px.Length; i += 4)
        {
            int a = anyAlpha ? px[i + 3] : 255;
            if (a < 255)
            {
                int white = 255 - a;
                for (int c = 0; c < 3; c++)
                    px[i + c] = (byte)Math.Min(255, (premultiplied ? px[i + c] : px[i + c] * a / 255) + white);
            }
            px[i + 3] = 255;
        }
        return new PixelImage(width, height, px, stride);
    }

    /// <summary>Ausschnitt (wird auf das Bild begrenzt). null, wenn nichts übrig bleibt.</summary>
    public static PixelImage? Crop(PixelImage image, int x, int y, int width, int height)
    {
        int left = Math.Clamp(x, 0, image.Width), top = Math.Clamp(y, 0, image.Height);
        int right = Math.Clamp(x + width, 0, image.Width), bottom = Math.Clamp(y + height, 0, image.Height);
        int w = right - left, h = bottom - top;
        if (w <= 0 || h <= 0) return null;
        var px = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
            Buffer.BlockCopy(image.Bgra, (top + row) * image.Stride + left * 4, px, row * w * 4, w * 4);
        return new PixelImage(w, h, px);
    }

    /// <summary>Zeichnet ein WPF-Element in ein Bild (für Demo-Bilder und Screenshots), 1 Punkt = 1 Pixel.</summary>
    public static PixelImage Render(FrameworkElement element, int width, int height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return FromBitmap(bitmap);
    }
}
