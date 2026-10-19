// SPDX-License-Identifier: MIT
namespace Lupe.Tests.TestSupport;

/// <summary>
/// Gleichzeitige Arbeit auf eigenen Threads statt im Thread-Pool. Tests, die Threads lange belegen (Barriere, Schleife bis
/// zum Testende), ließen sonst den Pool des Testprozesses verhungern: Auf dem macOS-Runner von GitHub (3 Kerne, also
/// anfangs 3 Pool-Threads) kamen Timer und Fortsetzungen parallel laufender Tests – Task.Delay, der Takt der Demo-Kamera,
/// die Erkennung der Scan-Schleife – dann über 10 s nicht dran.
/// </summary>
internal static class OwnThreads
{
    /// <summary>Führt <paramref name="body"/> (mit 0 … count−1) auf je einem eigenen Thread aus und wartet auf alle.</summary>
    public static void Run(int count, Action<int> body)
    {
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var threads = Enumerable.Range(0, count).Select(i => new Thread(() =>
        {
            try
            {
                body(i);
            }
            catch (Exception ex)
            {
                errors.Enqueue(ex);
            }
        }) { IsBackground = true, Name = $"Testthread {i}" }).ToArray();
        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join();
        if (!errors.IsEmpty) throw new AggregateException(errors);
    }

    /// <summary>Wie <c>Task.Run</c>, aber auf einem eigenen Thread – für Schleifen, die bis zum Testende laufen.</summary>
    public static Task Start(Action body) =>
        Task.Factory.StartNew(body, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
}
