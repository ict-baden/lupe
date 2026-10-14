using Lupe.Core.Model;

namespace Lupe.Core.Decoding;

/// <summary>Findet alle QR- und Barcodes in einem Bild (ZXing.Net), auch kleine, gedrehte und invertierte.</summary>
public sealed class BarcodeDecoder
{
    /// <param name="formats">Erlaubte Formate in ZXing-Schreibweise; null oder leer = alle.</param>
    public BarcodeDecoder(IReadOnlyList<string>? formats = null) => throw new NotImplementedException();

    /// <summary>Alle unterstützten Formate in ZXing-Schreibweise.</summary>
    public static IReadOnlyList<string> AllFormats => throw new NotImplementedException();

    /// <summary>Alle Codes im Bild, ohne Doppelte; leer, wenn keiner gefunden wurde.</summary>
    public IReadOnlyList<DecodedCode> Decode(PixelImage image, CancellationToken ct = default) => throw new NotImplementedException();
}
