// SPDX-License-Identifier: MIT
using Lupe.Core.Screen;

namespace Lupe.Services;

/// <summary>Fensterlage in physischen Bildschirmpixeln – auch auf Monitoren links von oder über dem Hauptmonitor (negative Koordinaten).</summary>
internal static class Placement
{
    /// <summary>
    /// Linke obere Ecke für ein Fenster von <paramref name="width"/> × <paramref name="height"/> Pixeln, mittig auf
    /// (<paramref name="x"/>, <paramref name="y"/>) und ganz auf der Arbeitsfläche. Ist das Fenster größer als die
    /// Fläche, bleibt die linke obere Ecke sichtbar (dort sitzen Titel und Nummer 1).
    /// </summary>
    public static (int X, int Y) CenterOn(int x, int y, int width, int height, PixelRect workArea)
    {
        int left = x - width / 2, top = y - height / 2;
        left = Math.Max(workArea.X, Math.Min(left, workArea.X + workArea.Width - width));
        top = Math.Max(workArea.Y, Math.Min(top, workArea.Y + workArea.Height - height));
        return (left, top);
    }
}
