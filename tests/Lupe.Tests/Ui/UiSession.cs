// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Headless;

namespace Lupe.Tests.Ui;

/// <summary>
/// Die Avalonia-Oberfläche ohne Bildschirm (Avalonia.Headless mit Skia): ein UI-Thread für alle Tests dieser Assembly,
/// Fenster und Seiten werden wie in der App angelegt, Tasten und Klicks über die Headless-Eingabe geschickt. Läuft
/// unter Windows, Linux (ohne X-Server) und macOS.
/// </summary>
internal static class UiSession
{
    private static readonly Lazy<HeadlessUnitTestSession> Session =
        new(() => HeadlessUnitTestSession.StartNew(typeof(UiTestApp), AvaloniaTestIsolationLevel.PerAssembly));

    /// <summary>Führt <paramref name="test"/> auf dem UI-Thread aus (Ausnahmen kommen beim Aufrufer an).</summary>
    public static void Run(Action test) => Session.Value.Dispatch(test, CancellationToken.None).GetAwaiter().GetResult();

    public static void Run(Func<Task> test) =>
        Session.Value.Dispatch(async () => { await test(); return true; }, CancellationToken.None).GetAwaiter().GetResult();
}

/// <summary>Einstieg für <see cref="HeadlessUnitTestSession"/>: Lupes App mit Theme, ohne Fenster beim Start.</summary>
public static class UiTestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Lupe.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
}
