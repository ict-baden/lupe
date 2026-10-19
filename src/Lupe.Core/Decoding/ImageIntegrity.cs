// SPDX-License-Identifier: MIT
using Lupe.Core.Resources;

namespace Lupe.Core.Decoding;

/// <summary>
/// Erkennt abgeschnittene PNG-, JPEG- und GIF-Dateien (abgebrochener Download oder Kopiervorgang). Die Windows-Decoder
/// lesen solche Dateien ohne Fehler und füllen den fehlenden Rest grau oder weiß – Lupe meldete dann „Kein Code gefunden“,
/// obwohl in Wahrheit die Datei kaputt ist. BMP und TIFF lehnt Windows selbst ab.
/// </summary>
/// <remarks>
/// Geprüft wird nur der Aufbau, nicht der Inhalt: PNG bis zum IEND-Block, JPEG bis zur Endmarke (EOI) – über alle Scans,
/// also auch progressiv –, GIF bis zum Ende der Daten des ersten Bildes (nur das wird gelesen). Was danach folgt, ist egal
/// (manche Kameras hängen Daten an). Andere Formate gelten als vollständig.
/// </remarks>
internal static class ImageIntegrity
{
    /// <summary>Warum die Datei unvollständig ist (für die Meldung), sonst null. Liest ab der aktuellen Position und stellt sie wieder her.</summary>
    public static string? Truncation(Stream stream)
    {
        if (!stream.CanSeek) return null;
        long start = stream.Position;
        try
        {
            var reader = new Reader(stream);
            Span<byte> head = stackalloc byte[8];
            int n = reader.Read(head);
            if (n >= 8 && head.SequenceEqual((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
                return Png(reader);
            if (n >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
            {
                reader.Position = 2;
                return Jpeg(reader);
            }
            if (n >= 6 && head[..3].SequenceEqual("GIF"u8) && (head[3..6].SequenceEqual("87a"u8) || head[3..6].SequenceEqual("89a"u8)))
            {
                reader.Position = 6;
                return Gif(reader);
            }
            return null;
        }
        finally
        {
            stream.Position = start;
        }
    }

    private static string Cut => Strings.Core_Image_EndsMidImage;

    /// <summary>Blöcke (Länge, Typ, Daten, Prüfsumme) bis IEND.</summary>
    private static string? Png(Reader r)
    {
        Span<byte> header = stackalloc byte[8];
        while (true)
        {
            if (r.Read(header) < 8) return Cut;
            uint length = (uint)(header[0] << 24 | header[1] << 16 | header[2] << 8 | header[3]);
            if (length > int.MaxValue) return null; // unsinnige Länge: soll der Decoder beurteilen
            if (header[4..8].SequenceEqual("IEND"u8)) return null;
            long next = r.Position + length + 4;
            if (next > r.Length) return Cut;
            r.Position = next;
        }
    }

    /// <summary>Marken bis EOI; nach jedem Scan (SOS) die komprimierten Daten bis zur nächsten echten Marke überspringen.</summary>
    private static string? Jpeg(Reader r)
    {
        while (true)
        {
            int b = r.ReadByte();
            if (b < 0) return Cut;
            if (b != 0xFF) return null; // kein Markenanfang: unüblicher Aufbau, das beurteilt der Decoder
            int marker;
            do marker = r.ReadByte(); while (marker == 0xFF); // Füllbytes
            if (marker < 0) return Cut;
            if (marker == 0xD9) return null; // EOI
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7)) continue; // ohne Länge
            int hi = r.ReadByte(), lo = r.ReadByte();
            if (lo < 0) return Cut;
            int length = hi << 8 | lo;
            if (length < 2) return null;
            long next = r.Position + length - 2;
            if (next > r.Length) return Cut;
            r.Position = next;
            if (marker != 0xDA) continue;

            // Komprimierte Daten: 0xFF 0x00 ist ein maskiertes 0xFF, RSTn gehört zum Scan, alles andere ist die nächste Marke
            while (true)
            {
                int c = r.ReadByte();
                if (c < 0) return Cut;
                if (c != 0xFF) continue;
                int d;
                do d = r.ReadByte(); while (d == 0xFF);
                if (d < 0) return Cut;
                if (d == 0x00 || d is >= 0xD0 and <= 0xD7) continue;
                r.Position -= 2; // zurück auf 0xFF der Marke
                break;
            }
        }
    }

    /// <summary>Kopf, Farbtabellen und Erweiterungen bis zum Ende der Bilddaten des ersten Bildes.</summary>
    private static string? Gif(Reader r)
    {
        Span<byte> screen = stackalloc byte[7];
        Span<byte> descriptor = stackalloc byte[9];
        if (r.Read(screen) < 7) return Cut;
        if (!Skip(r, ColorTable(screen[4]))) return Cut;
        while (true)
        {
            int block = r.ReadByte();
            switch (block)
            {
                case < 0:
                    return Cut;
                case 0x3B: // Ende ohne Bild – soll der Decoder melden
                    return null;
                case 0x21: // Erweiterung: Kennung, dann Unterblöcke
                    if (r.ReadByte() < 0 || !SkipSubBlocks(r)) return Cut;
                    break;
                case 0x2C: // Bild: Beschreibung, lokale Farbtabelle, LZW-Startgröße, Daten
                    if (r.Read(descriptor) < 9) return Cut;
                    if (!Skip(r, ColorTable(descriptor[8]))) return Cut;
                    if (r.ReadByte() < 0 || !SkipSubBlocks(r)) return Cut;
                    return null;
                default:
                    return null; // unbekannter Aufbau – der Decoder entscheidet
            }
        }
    }

    private static int ColorTable(byte packed) => (packed & 0x80) == 0 ? 0 : 3 * (1 << ((packed & 0x07) + 1));

    private static bool Skip(Reader r, long count)
    {
        if (r.Position + count > r.Length) return false;
        r.Position += count;
        return true;
    }

    /// <summary>Unterblöcke (Länge, Daten) bis zum Block der Länge 0.</summary>
    private static bool SkipSubBlocks(Reader r)
    {
        while (true)
        {
            int size = r.ReadByte();
            if (size < 0) return false;
            if (size == 0) return true;
            if (!Skip(r, size)) return false;
        }
    }

    /// <summary>Gepuffertes Lesen mit Springen (der Datenstrom selbst wird nur blockweise gelesen).</summary>
    private sealed class Reader(Stream stream)
    {
        private readonly byte[] _buffer = new byte[1 << 16];
        private readonly long _origin = stream.Position;
        private long _bufferStart;
        private int _bufferLength;
        private long _position;

        public long Length { get; } = stream.Length - stream.Position;

        public long Position
        {
            get => _position;
            set => _position = Math.Max(0, value);
        }

        public int ReadByte()
        {
            if (_position >= Length) return -1;
            if (_position < _bufferStart || _position >= _bufferStart + _bufferLength) Fill();
            return _bufferLength == 0 ? -1 : _buffer[_position++ - _bufferStart];
        }

        public int Read(Span<byte> target)
        {
            int n = 0;
            while (n < target.Length)
            {
                int b = ReadByte();
                if (b < 0) break;
                target[n++] = (byte)b;
            }
            return n;
        }

        private void Fill()
        {
            stream.Position = _origin + _position;
            _bufferStart = _position;
            _bufferLength = 0;
            int read;
            while (_bufferLength < _buffer.Length && (read = stream.Read(_buffer, _bufferLength, _buffer.Length - _bufferLength)) > 0)
                _bufferLength += read;
        }
    }
}
