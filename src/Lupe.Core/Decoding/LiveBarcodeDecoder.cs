using Lupe.Core.Model;

namespace Lupe.Core.Decoding;

/// <summary>
/// Erkennung für den Kamerastrom (z. B. als Erkennungsfunktion für <c>ScanLoop</c>): derselbe Leser wie
/// <see cref="BarcodeDecoder"/>, aber nur dessen erste Stufe ohne Schräglage (das ganze Bild, dazu invertiert für helle
/// Strichcodes auf dunklem Grund) und auf wenigen Kernen.
/// <para>
/// Die gedrehten und vergrößerten Varianten kosten je Bild ein Vielfaches – bei Dauerbetrieb mehrere Kerne Last, obwohl
/// das nächste Bild ohnehin eine neue Chance ist und der Benutzer den Code von selbst gerader hält. Deshalb laufen hier
/// höchstens <see cref="DefaultParallelism"/> Aufgaben gleichzeitig. Für Bildschirmausschnitte und Dateien bleibt
/// <see cref="BarcodeDecoder"/> die richtige Wahl.
/// </para>
/// <para>Threadsicher.</para>
/// </summary>
public sealed class LiveBarcodeDecoder
{
    private readonly BarcodeDecoder _decoder;

    /// <param name="formats">Erlaubte Formate in ZXing-Schreibweise; null oder leer = alle (wie bei <see cref="BarcodeDecoder"/>).</param>
    public LiveBarcodeDecoder(IReadOnlyList<string>? formats = null) =>
        _decoder = new BarcodeDecoder(formats) { Live = true, MaxParallelism = DefaultParallelism };

    /// <summary>Gleichzeitige Aufgaben je Bild: ein Viertel der Kerne, mindestens eine, höchstens zwei.</summary>
    internal static int DefaultParallelism => Math.Clamp(Environment.ProcessorCount / 4, 1, 2);

    /// <summary>Alle Codes im Kamerabild, ohne Doppelte, in Lesereihenfolge; leer, wenn keiner gefunden wurde.</summary>
    public IReadOnlyList<DecodedCode> Decode(PixelImage frame, CancellationToken ct = default) => _decoder.Decode(frame, ct);
}
