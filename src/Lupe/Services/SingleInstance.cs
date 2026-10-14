using Lupe.Interop;

namespace Lupe.Services;

/// <summary>
/// Nur eine Lupe je Windows-Sitzung. Ein zweiter Start meldet sich über ein benanntes Ereignis beim ersten, der
/// daraufhin sein Fenster nach vorn holt, und beendet sich.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    // „Local\“ = je Sitzung: mehrere angemeldete Benutzer (Terminalserver) haben jeder ihre eigene Lupe
    private const string MutexName = @"Local\ICT-Baden.Lupe";
    private const string ShowEventName = @"Local\ICT-Baden.Lupe.Fenster";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private RegisteredWaitHandle? _wait;
    private bool _disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle show)
    {
        _mutex = mutex;
        _show = show;
    }

    /// <summary>Erste Instanz: Besitz übernehmen. Läuft schon eine, null.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
        if (!created)
        {
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName));
    }

    /// <summary>Zweite Instanz: die erste bitten, ihr Fenster zu zeigen.</summary>
    public static void SignalFirst()
    {
        // Diese Instanz wurde gerade vom Benutzer gestartet und darf deshalb den Vordergrund vergeben – die erste
        // Instanz bekommt dieses Recht, sonst blinkte ihr Fenster nur in der Taskleiste
        Native.AllowSetForegroundWindow(Native.ASFW_ANY);
        try
        {
            using var show = EventWaitHandle.OpenExisting(ShowEventName);
            show.Set();
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
        {
            // Die erste Instanz startet gerade erst oder beendet sich – dann gibt es nichts nach vorn zu holen
        }
    }

    /// <summary>Ruft <paramref name="onShow"/> (auf einem Hintergrund-Thread) bei jedem weiteren Start auf.</summary>
    public void Listen(Action onShow)
    {
        _wait?.Unregister(null);
        _wait = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) => onShow(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wait?.Unregister(null);
        _show.Dispose();
        try { _mutex.ReleaseMutex(); }
        catch (ApplicationException) { } // auf einem anderen Thread freigegeben – Windows räumt beim Beenden auf
        _mutex.Dispose();
    }
}
