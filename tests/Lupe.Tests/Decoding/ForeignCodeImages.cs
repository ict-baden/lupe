// SPDX-License-Identifier: MIT
using Lupe.Core.Decoding;
using Lupe.Core.Model;

namespace Lupe.Tests.Decoding;

/// <summary>
/// Fertige Bilder von Codes, für die ZXing.Net keinen Schreiber hatte (GS1 DataBar alias RSS, MaxiCode) – erzeugt mit
/// zxing-cpp 3.1.1 (Python: <c>zxingcpp.write_barcode_to_image(zxingcpp.create_barcode(text, format), scale)</c>), als PNG
/// eingebettet. Seit Lupe selbst mit zxing-cpp schreibt, laufen diese Formate auch durch die Rundlauftests; die Bilder
/// bleiben als feste, geladene Dateien (PNG über <see cref="ImageFile"/>) und für die gestapelten Varianten.
/// <list type="bullet">
/// <item>Rss14, Rss14Gestapelt: DataBar bzw. DataBar Stacked mit GTIN „0950110153001“, Maßstab 3.</item>
/// <item>RssExpanded, RssExpandedGestapelt: „(01)98898765432106(3202)012345(15)991231“, einzeilig bzw. gestapelt, Maßstab 2.</item>
/// <item>MaxiCode: „Lupe MaxiCode 4711“, Maßstab 2.</item>
/// </list>
/// </summary>
internal static class ForeignCodeImages
{
    public const string Gtin = "09501101530010";
    public const string Gs1 = "(01)98898765432106(3202)012345(15)991231";
    public const string MaxiText = "Lupe MaxiCode 4711";

    public static Task<PixelImage> LoadAsync(string name) => ImageFile.LoadAsync(
        new MemoryStream(Convert.FromBase64String(name switch
        {
            nameof(Rss14) => Rss14,
            nameof(Rss14Gestapelt) => Rss14Gestapelt,
            nameof(RssExpanded) => RssExpanded,
            nameof(RssExpandedGestapelt) => RssExpandedGestapelt,
            nameof(MaxiCode) => MaxiCode,
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        })), name + ".png", ImageFile.MaxPixels, CancellationToken.None);

    public const string Rss14 =
        "iVBORw0KGgoAAAANSUhEUgAAASAAAACWCAAAAABbDAqAAAAA9klEQVR42u3QOw6AQAhAQfD+d8ZiG/ajsTbzKoNIzGRFZETF1Jh8mee88LTfF7" +
        "I99LfVPunHl51loR88tt8//sm+M+ZX6DVAgAABAgQIECBAAgQIECBAgAABEiBAgAABAgQIkAABAgQIECBAgAAJECBAgAABAgRIgAABAgQIECBA" +
        "AgQIECBAgAABAiRAgAABAgQIECABAgQIECBAgAAJECBAgAABAgQIkAABAgQIECBAgAQIECBAgAABAiRAgAABAgQIECBAAgQIECBAgAABEiBAgA" +
        "ABAgQIkAABAgQIECBAgAAJECBAgAABAgRIgAABAgQI0H+6AQGCGCx08RmNAAAAAElFTkSuQmCC";

    public const string Rss14Gestapelt =
        "iVBORw0KGgoAAAANSUhEUgAAAJYAAAAnCAAAAADI/O7uAAAAeUlEQVR42u3WsQrAIAyE4Uvf/52vg0vSaClOFn4HKTGVD+JwYSkkq6xR+VKP2r" +
        "Dqzw2RPvKp0y+XjlywYMGCBQsWrKNZ4ZpDHgFjY1fLLW5RZ3o/Q9weohaBczqRnjn9cm99EtN02ntGnSHCggULFixYsGDBgvVP1g3aRCtKk1Mx" +
        "SgAAAABJRU5ErkJggg==";

    public const string RssExpanded =
        "iVBORw0KGgoAAAANSUhEUgAAAZAAAABECAAAAACKDDydAAAAx0lEQVR42u3RMQ6EMAxFwe+9/529BbA4QdqaYl6BkAAnZKpT6eS8Jkkl5/1xd1" +
        "9n99vXs/3748k6qceEuU6POXlMu79dV8pv37W9kbHnfvzTv3+oZf48m9om9PiytznrGnPX62n2cu6dyid6VUCACAgQAQEiIEAEBIiACAgQAQEi" +
        "IEAEBIiACAgQAQEiIEAEBIiACAgQAQEiIEAEBIiACAgQAQEiIEAEBIiACAgQAQEiIEAEBIiACAgQAQEiIEAEBIiACMg7+wKfIiyI2YCY9wAAAA" +
        "BJRU5ErkJggg==";

    public const string RssExpandedGestapelt =
        "iVBORw0KGgoAAAANSUhEUgAAAMwAAACOCAAAAABY2bhJAAABSUlEQVR42u3bwW7DIBQEwKXq//8yPTit4T3SS6UerEGKZQUDGTDktGNmZCava5" +
        "KM5HV/3d3XtdxPf9fV9lfN3tNceljHmUs/ab3dbfeR8vO7r9YfeVCBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGB" +
        "gYGBgYGBgYGB+f8y5pYBS1KyXTWRli1rttbV9rPk0U6psjVRNlqWbLRk2z5SSpLtUSvzmaa8pNdM7Im+efzcmbzavj+X9t2+Wr+NkWPd8/fMaG" +
        "/gKRn5bu/UfdF7PSU+9x03206pezPbszXt+bSVyfHUWOcz5XRLcjxNRku6ps36aVZP70RN0o52+tXVHZn+NGFgYGBgYGBgYGBgYGBgYGBgYGBg" +
        "YGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGD+Vr4A1ICCFAf01SQAAAAASUVORK5CYII=";

    public const string MaxiCode =
        "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" +
        "HjQvwWL/8B9wBShXdC790AAAAASUVORK5CYII=";
}
