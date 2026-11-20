// SPDX-License-Identifier: MIT
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
    public static SingleInstance? TryAcquire() => TryAcquire("");

    /// <summary>Zweite Instanz: die erste bitten, ihr Fenster zu zeigen – oder (<paramref name="scan"/>) einen Ausschnitt zu scannen.</summary>
    public static void SignalFirst(bool scan) => SignalFirst(scan, "");

    /// <summary>
    /// Wie <see cref="TryAcquire()"/>, mit einem Zusatz an allen Namen (Sperre, Ereignisse, Socket). Tests nehmen einen
    /// eigenen Zusatz und kommen so weder einer laufenden Lupe noch den Prozess-Tests in die Quere; Lupe selbst nutzt "".
    /// </summary>
    internal static SingleInstance? TryAcquire(string nameSuffix) =>
        OperatingSystem.IsWindows() ? WindowsInstance.Acquire(nameSuffix) : UnixInstance.Acquire(nameSuffix);

    /// <summary>Wie <see cref="SignalFirst(bool)"/>, an die Instanz mit diesem Namenszusatz.</summary>
    internal static void SignalFirst(bool scan, string nameSuffix)
    {
        if (OperatingSystem.IsWindows()) WindowsInstance.Signal(scan, nameSuffix);
        else UnixInstance.Signal(scan, nameSuffix);
    }

    /// <summary>
    /// Ruft bei jedem weiteren Start (auf einem Hintergrund-Thread) <paramref name="onShow"/> auf, bei
    /// <c>--bildschirm</c> stattdessen <paramref name="onScan"/>.
    /// </summary>
    public abstract void Listen(Action onShow, Action onScan);

    public abstract void Dispose();

    /// <summary>Linux und macOS: Pfad des Sockets für das Signal (Tests).</summary>
    internal static string UnixSocketPath => UnixInstance.PipePath("");

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private sealed class WindowsInstance(Mutex mutex, EventWaitHandle show, EventWaitHandle scan) : SingleInstance
    {
        // „Local\“ = je Sitzung: mehrere angemeldete Benutzer (Terminalserver) haben jeder ihre eigene Lupe
        private const string MutexName = @"Local\ICT-Baden.Lupe";
        private const string ShowEventName = @"Local\ICT-Baden.Lupe.Fenster";
        private const string ScanEventName = @"Local\ICT-Baden.Lupe.Scan";

        private RegisteredWaitHandle? _wait;
        private RegisteredWaitHandle? _waitScan;
        private bool _disposed;

        public static SingleInstance? Acquire(string suffix)
        {
            var mutex = new Mutex(initiallyOwned: true, MutexName + suffix, out bool created);
            if (!created)
            {
                mutex.Dispose();
                return null;
            }
            return new WindowsInstance(mutex,
                new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName + suffix),
                new EventWaitHandle(false, EventResetMode.AutoReset, ScanEventName + suffix));
        }

        public static void Signal(bool scan, string suffix)
        {
            // Diese Instanz wurde gerade vom Benutzer gestartet und darf deshalb den Vordergrund vergeben – die erste
            // Instanz bekommt dieses Recht, sonst blinkte ihr Fenster nur in der Taskleiste
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);
            try
            {
                using var signal = EventWaitHandle.OpenExisting((scan ? ScanEventName : ShowEventName) + suffix);
                signal.Set();
            }
            catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
            {
                // Die erste Instanz startet gerade erst oder beendet sich – dann gibt es nichts nach vorn zu holen
            }
        }

        public override void Listen(Action onShow, Action onScan)
        {
            _wait?.Unregister(null);
            _waitScan?.Unregister(null);
            _wait = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => Run(onShow), null, Timeout.Infinite, executeOnlyOnce: false);
            _waitScan = ThreadPool.RegisterWaitForSingleObject(scan, (_, _) => Run(onScan), null, Timeout.Infinite, executeOnlyOnce: false);
        }

        // Ein Fehler beim Reagieren auf ein Signal beendet weder das Lauschen noch (Thread-Pool) den Prozess – wie unter Unix
        private static void Run(Action action)
        {
            try { action(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Nur eine Lupe: Signal nicht angenommen: {ex}"); }
        }

        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _wait?.Unregister(null);
            _waitScan?.Unregister(null);
            show.Dispose();
            scan.Dispose();
            try { mutex.ReleaseMutex(); }
            catch (ApplicationException) { } // auf einem anderen Thread freigegeben – das System räumt beim Beenden auf
            mutex.Dispose();
        }
    }

    private sealed class UnixInstance(FileStream lockFile, string pipePath) : SingleInstance
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

        /// <summary>
        /// Der Unix-Socket im selben Ordner wie die Sperrdatei (ein absoluter Pfad ist für .NET der Ort des Sockets) – nicht
        /// unter festem Namen im gemeinsamen /tmp, wo ein anderer Benutzer ihn vorher anlegen könnte. Dazu
        /// <see cref="PipeOptions.CurrentUserOnly"/>: Socket nur für den Benutzer, und der Gegenüber muss derselbe Benutzer sein.
        /// </summary>
        internal static string PipePath(string suffix) => Path.Combine(Folder, $"lupe-{Environment.UserName}{suffix}.sock");

        public static SingleInstance? Acquire(string suffix)
        {
            try
            {
                // FileShare.None: .NET sperrt die Datei unter Unix mit flock – frei, sobald der Prozess endet (auch nach einem Absturz)
                var stream = new FileStream(Path.Combine(Folder, $"lupe-{Environment.UserName}{suffix}.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new UnixInstance(stream, PipePath(suffix));
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                // Sperrdatei nicht anlegbar: lieber zwei Lupen als keine
                return new UnixInstance(new FileStream(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose), PipePath(suffix));
            }
        }

        // Ein Byte je Signal
        private const byte ShowByte = 1, ScanByte = 2;

        public static void Signal(bool scan, string suffix)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipePath(suffix), PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(1000);
                client.WriteByte(scan ? ScanByte : ShowByte);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                // Die erste Instanz startet gerade erst oder beendet sich
            }
        }

        public override void Listen(Action onShow, Action onScan)
        {
            var token = _stop.Token;
            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await using var server = new NamedPipeServerStream(pipePath, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                        await server.WaitForConnectionAsync(token);
                        var buffer = new byte[1];
                        int read = await server.ReadAsync(buffer, token);
                        if (read == 1 && buffer[0] == ScanByte) onScan();
                        else onShow();
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Eine alte Socket-Datei (nach einem Absturz, SocketException), ein abgebrochener zweiter Start
                        // (IOException), ein fremder Benutzer am Socket (UnauthorizedAccessException) oder ein Fehler beim
                        // Reagieren auf das Signal: kurz warten und weiter lauschen – sonst kämen spätere Signale nie mehr an
                        System.Diagnostics.Debug.WriteLine($"Nur eine Lupe: Signal nicht angenommen: {ex}");
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
