using Lupe.Core.Model;

namespace Lupe.Core.Decoding;

/// <summary>
/// Erkennung für den Kamerastrom (z. B. als Erkennungsfunktion für <c>ScanLoop</c>): dieselben Leser wie
/// <see cref="BarcodeDecoder"/>, aber nur dessen schnelle erste Stufe (ganzes Bild bzw. Übersicht und Kacheln, normal und
/// für 2D-Codes invertiert, Data Matrix/Aztec auch abseits der Mitte) und auf wenigen Kernen.
/// <para>
/// Die gründliche zweite Stufe (gedrehte, vergrößerte, verkleinerte Varianten) kostet bei einem leeren 720p-Bild rund
/// eine Rechensekunde – bei Dauerbetrieb mehrere Kerne Last, obwohl das nächste Bild ohnehin eine neue Chance ist.
/// Auch die erste Stufe braucht je 720p-Bild rund 120 ms Rechenzeit; über alle Kerne verteilt wären das bei einer
/// Erkennung direkt nach der anderen dauerhaft 4–5 belegte Kerne. Deshalb laufen hier höchstens
/// <see cref="DefaultParallelism"/> Aufgaben gleichzeitig (gemessen: 720p in rund 60 ms je Bild, Dauerlast unter zwei
/// Kernen). Für Bildschirmausschnitte und Dateien bleibt <see cref="BarcodeDecoder"/> die richtige Wahl.
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
