// SPDX-License-Identifier: MIT
using Lupe.Core.Model;
using Lupe.Core.Screen;

namespace Lupe.Platform.Windows.Screen;

/// <summary>
/// Schaltet den aufrufenden Thread vorübergehend auf „Per-Monitor-DPI (V2)“, damit Monitor-Rechtecke,
/// Systemmetriken und GDI-Kopien in physischen Pixeln arbeiten – egal, wie das Programm selbst DPI-eingestellt
/// ist (eine nicht DPI-fähige App sähe sonst hochskalierte, unscharfe Koordinaten). <see cref="Dispose"/> stellt
/// den vorherigen Zustand wieder her.
/// </summary>
internal readonly ref struct DpiScope
{
    private readonly nint _previous;

    private DpiScope(nint previous) => _previous = previous;

    public static DpiScope PerMonitor()
    {
        nint previous = 0;
        try
        {
            previous = Native.SetThreadDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            // V2 gibt es ab Windows 10 1703; davor die erste Per-Monitor-Fassung.
            if (previous == 0) previous = Native.SetThreadDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE);
        }
        catch (EntryPointNotFoundException)
        {
            // Sehr altes Windows: Thread-DPI gibt es nicht, dann gilt die Einstellung des Prozesses.
        }
        return new DpiScope(previous);
    }

    public void Dispose()
    {
        if (_previous != 0) Native.SetThreadDpiAwarenessContext(_previous);
    }
}
