using System.Diagnostics;

namespace Lupe.Core.Camera;

/// <summary>
/// Ruft Ereignisempfänger einzeln auf: Ein fehlerhafter Empfänger (z. B. die Vorschau) darf weder die anderen noch den
/// Kamera-Thread mitreißen. Der Fehler landet im Trace.
/// </summary>
internal static class SafeEvents
{
    public static void Raise<T>(EventHandler<T>? handler, object sender, T args, string eventName)
    {
        if (handler is null) return;
        foreach (var single in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<T>)single)(sender, args);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Lupe: Fehler im Empfänger von {eventName}: {ex}");
            }
        }
    }
}
