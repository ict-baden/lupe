// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

using Lupe.Platform.Windows.Screen;

namespace Lupe.Tests.Screen;

/// <summary>
/// Eigener STA-Thread mit Nachrichtenschleife für Testfenster: Win32-Fenster gehören dem Thread, der sie erzeugt, und
/// brauchen dort jemanden, der ihre Nachrichten (z. B. WM_PAINT) verarbeitet. Fenster, die über
/// <see cref="CreateWindow"/> entstehen, werden beim <see cref="Dispose"/> zerstört.
/// </summary>
internal sealed class UiThread : IDisposable
{
    private const uint WM_RUN = TestNative.WM_APP + 0x55;
    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly ManualResetEventSlim _ready = new();
    private readonly List<nint> _windows = [];
    private uint _threadId;

    /// <param name="perMonitorDpi">Thread auf Per-Monitor-DPI stellen, damit Fensterkoordinaten physische Pixel sind.</param>
    public UiThread(bool perMonitorDpi = true)
    {
        _thread = new Thread(() => Run(perMonitorDpi)) { IsBackground = true, Name = "Lupe-Testfenster" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    public uint ThreadId => _threadId;

    public T Invoke<T>(Func<T> func)
    {
        T result = default!;
        Exception? error = null;
        var done = new ManualResetEventSlim(); // bewusst ohne using: bei Zeitüberschreitung setzt der Thread es später noch
        _work.Enqueue(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        });
        if (!TestNative.PostThreadMessageW(_threadId, WM_RUN, 0, 0))
            throw new InvalidOperationException("Der Testfenster-Thread läuft nicht mehr.");
        if (!done.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Der Testfenster-Thread antwortet nicht.");
        if (error is not null) ExceptionDispatchInfo.Throw(error);
        return result;
    }

    public void Invoke(Action action) => Invoke(() => { action(); return 0; });

    /// <summary>Erzeugt ein Fenster auf diesem Thread (Fehler → Ausnahme).</summary>
    public nint CreateWindow(uint exStyle, string className, uint style, int x, int y, int width, int height) => Invoke(() =>
    {
        nint hwnd = TestNative.CreateWindowExW(exStyle, className, null, style, x, y, width, height, 0, 0, 0, 0);
        if (hwnd == 0) throw new System.ComponentModel.Win32Exception();
        _windows.Add(hwnd);
        return hwnd;
    });

    private void Run(bool perMonitorDpi)
    {
        if (perMonitorDpi) TestNative.SetThreadDpiAwarenessContext(TestNative.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        _threadId = TestNative.GetCurrentThreadId();
        TestNative.PeekMessageW(out _, 0, 0, 0, 0); // Warteschlange anlegen, bevor jemand etwas schickt
        _ready.Set();
        while (TestNative.GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            if (msg.hwnd == 0 && msg.message == WM_RUN)
            {
                while (_work.TryDequeue(out var work)) work();
                continue;
            }
            TestNative.TranslateMessage(ref msg);
            TestNative.DispatchMessageW(ref msg);
        }
        while (_work.TryDequeue(out var rest)) rest();
    }

    public void Dispose()
    {
        if (!_thread.IsAlive) return;
        try
        {
            Invoke(() =>
            {
                foreach (nint hwnd in _windows) TestNative.DestroyWindow(hwnd);
                _windows.Clear();
            });
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
        {
            // Thread hängt oder ist schon weg – die Fenster verschwinden spätestens mit dem Testprozess.
        }
        TestNative.PostThreadMessageW(_threadId, TestNative.WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(5));
    }
}
