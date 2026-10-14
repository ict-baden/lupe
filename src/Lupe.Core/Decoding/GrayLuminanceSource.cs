using ZXing;

namespace Lupe.Core.Decoding;

/// <summary>
/// ZXing-Helligkeitsquelle über einem <see cref="GrayImage"/> – ohne System.Drawing und ohne Kopie für Ausschnitte
/// (ZXing schneidet bei der Mehrfachsuche viel zu). Prüft bei jedem Zeilenzugriff den Abbruch, damit eine laufende
/// Erkennung schnell endet.
/// </summary>
internal sealed class GrayLuminanceSource : LuminanceSource
{
    private readonly byte[] _pixels;
    private readonly int _stride;
    private readonly int _left;
    private readonly int _top;
    private readonly CancellationToken _ct;
    private byte[]? _matrix;

    public GrayLuminanceSource(GrayImage image, CancellationToken ct = default)
        : this(image.Pixels, image.Width, 0, 0, image.Width, image.Height, ct)
    {
    }

    private GrayLuminanceSource(byte[] pixels, int stride, int left, int top, int width, int height, CancellationToken ct)
        : base(width, height)
    {
        _pixels = pixels;
        _stride = stride;
        _left = left;
        _top = top;
        _ct = ct;
    }

    public override byte[] getRow(int y, byte[] row)
    {
        _ct.ThrowIfCancellationRequested();
        if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y), "Zeile außerhalb des Bildes.");
        int width = Width;
        if (row == null || row.Length < width) row = new byte[width];
        Buffer.BlockCopy(_pixels, (_top + y) * _stride + _left, row, 0, width);
        return row;
    }

    public override byte[] Matrix
    {
        get
        {
            _ct.ThrowIfCancellationRequested();
            int width = Width, height = Height;
            if (_left == 0 && _top == 0 && width == _stride && _pixels.Length == width * height) return _pixels;
            if (_matrix != null) return _matrix;
            var m = new byte[width * height];
            for (int y = 0; y < height; y++)
                Buffer.BlockCopy(_pixels, (_top + y) * _stride + _left, m, y * width, width);
            return _matrix = m;
        }
    }

    public override bool CropSupported => true;

    public override LuminanceSource crop(int left, int top, int width, int height)
    {
        if (left < 0 || top < 0 || width <= 0 || height <= 0 || left + width > Width || top + height > Height)
            throw new ArgumentException("Ausschnitt liegt nicht im Bild.");
        return new GrayLuminanceSource(_pixels, _stride, _left + left, _top + top, width, height, _ct);
    }

    public override bool RotateSupported => true;

    /// <summary>90° gegen den Uhrzeigersinn, wie ZXing es für liegende Strichcodes erwartet.</summary>
    public override LuminanceSource rotateCounterClockwise()
    {
        _ct.ThrowIfCancellationRequested();
        int width = Width, height = Height;
        var rotated = new byte[width * height];
        // Neue Breite = alte Höhe; Spalte x (von rechts) wird zur Zeile
        for (int y = 0; y < height; y++)
        {
            int srcRow = (_top + y) * _stride + _left;
            for (int x = 0; x < width; x++)
                rotated[(width - 1 - x) * height + y] = _pixels[srcRow + x];
        }
        return new GrayLuminanceSource(rotated, height, 0, 0, height, width, _ct);
    }

    public override bool InversionSupported => true;

    public override LuminanceSource invert() => new InvertedLuminanceSource(this);
}
