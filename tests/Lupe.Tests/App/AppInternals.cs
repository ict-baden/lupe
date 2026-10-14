using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Lupe.Tests.App;

/// <summary>
/// Ruft interne, WPF-freie Hilfen der App über Reflexion aus der gebauten Lupe.dll auf. Das Testprojekt verweist nur
/// auf Lupe.Core (ein Verweis auf die App ist ein offener Vertragswunsch) – so lässt sich reine Logik trotzdem direkt
/// prüfen. Nur Typen ohne WPF-Abhängigkeit: Der Testprozess hat das WindowsDesktop-Framework nicht geladen.
/// </summary>
internal static class AppInternals
{
    private static readonly Lazy<Assembly> App = new(() =>
    {
        var exe = AppExe.Path ?? throw new InvalidOperationException("Lupe.exe nicht gefunden.");
        return Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(exe)!, "Lupe.dll"));
    });

    public static object? Call(string typeName, string method, params object?[] args)
    {
        var type = App.Value.GetType(typeName, throwOnError: true)!;
        var info = type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(typeName, method);
        try
        {
            return info.Invoke(null, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(ex.InnerException);
            throw;
        }
    }
}
