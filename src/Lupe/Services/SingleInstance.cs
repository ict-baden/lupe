using System.IO.Pipes;
using Lupe.Interop;

namespace Lupe.Services;

/// <summary>
/// Nur eine Lupe je Sitzung. Ein zweiter Start meldet sich beim ersten, der daraufhin sein Fenster nach vorn holt, und
/// beendet sich. Windows: benannter Mutex und benanntes Ereignis je Sitzung („Local\“). Linux und macOS: Sperrdatei
/// (flock) im Laufzeitordner des Benutzers und eine lokale Pipe (Unix-Socket) für das Signal.
/// </summary>
internal abstract class SingleInstance : IDisposable
{
    /// <summary>Erste Instanz: Besitz übernehmen. Läuft schon eine, null.</summary>
    public static SingleInstance? TryAcquire() => OperatingSystem.IsWindows() ? WindowsInstance.Acquire() : UnixInstance.Acquire();

    /// <summary>Zweite Instanz: die erste bitten, ihr Fenster zu zeigen.</summary>
    public static void SignalFirst()
    {
        if (OperatingSystem.IsWindows()) WindowsInstance.Signal();
        else UnixInstance.Signal();
    }

    /// <summary>Ruft <paramref name="onShow"/> (auf einem Hintergrund-Thread) bei jedem weiteren Start auf.</summary>
    public abstract void Listen(Action onShow);

    public abstract void Dispose();

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private sealed class WindowsInstance(Mutex mutex, EventWaitHandle show) : SingleInstance
    {
        // „Local\“ = je Sitzung: mehrere angemeldete Benutzer (Terminalserver) haben jeder ihre eigene Lupe
        private const string MutexName = @"Local\ICT-Baden.Lupe";
        private const string ShowEventName = @"Local\ICT-Baden.Lupe.Fenster";

        private RegisteredWaitHandle? _wait;
        private bool _disposed;

        public static SingleInstance? Acquire()
        {
            var mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
            if (!created)
            {
                mutex.Dispose();
                return null;
            }
            return new WindowsInstance(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName));
        }

        public static void Signal()
        {
            // Diese Instanz wurde gerade vom Benutzer gestartet und darf deshalb den Vordergrund vergeben – die erste
            // Instanz bekommt dieses Recht, sonst blinkte ihr Fenster nur in der Taskleiste
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ShowEventName);
                signal.Set();
            }
            catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
            {
                // Die erste Instanz startet gerade erst oder beendet sich – dann gibt es nichts nach vorn zu holen
            }
        }

        public override void Listen(Action onShow)
        {
            _wait?.Unregister(null);
            _wait = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => onShow(), null, Timeout.Infinite, executeOnlyOnce: false);
        }

        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _wait?.Unregister(null);
            show.Dispose();
            try { mutex.ReleaseMutex(); }
            catch (ApplicationException) { } // auf einem anderen Thread freigegeben – das System räumt beim Beenden auf
            mutex.Dispose();
        }
    }

    private sealed class UnixInstance(FileStream lockFile) : SingleInstance
    {
        private readonly CancellationTokenSource _stop = new();
        private bool _disposed;

        /// <summary>Je Benutzer: XDG_RUNTIME_DIR (Linux), sonst der Temp-Ordner (macOS: je Benutzer ohnehin eigener).</summary>
        private static string Folder
        {
            get
            {
                var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
                return !string.IsNullOrEmpty(runtime) && Directory.Exists(runtime) ? runtime : Path.GetTempPath();
            }
        }

        private static string PipeName => $"ict-baden.lupe.{Environment.UserName}";

        public static SingleInstance? Acquire()
        {
            try
            {
                // FileShare.None: .NET sperrt die Datei unter Unix mit flock – frei, sobald der Prozess endet (auch nach einem Absturz)
                var stream = new FileStream(Path.Combine(Folder, $"lupe-{Environment.UserName}.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new UnixInstance(stream);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                // Sperrdatei nicht anlegbar: lieber zwei Lupen als keine
                return new UnixInstance(new FileStream(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose));
            }
        }

        public static void Signal()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(1000);
                client.WriteByte(1);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                // Die erste Instanz startet gerade erst oder beendet sich
            }
        }

        public override void Listen(Action onShow)
        {
            var token = _stop.Token;
            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                        await server.WaitForConnectionAsync(token);
                        var buffer = new byte[1];
                        _ = await server.ReadAsync(buffer, token);
                        onShow();
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (IOException)
                    {
                        // Eine alte Socket-Datei (nach einem Absturz) oder ein abgebrochener zweiter Start – kurz warten, weiter
                        await Task.Delay(500, CancellationToken.None);
                    }
                }
            }, CancellationToken.None);
        }

        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stop.Cancel();
            lockFile.Dispose();
        }
    }
}
