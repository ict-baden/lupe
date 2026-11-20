// SPDX-License-Identifier: MIT
using System.Reflection;
using System.Runtime.InteropServices;
using Lupe.Core.Resources;

namespace Lupe.Core.Decoding;

/// <summary>
/// Die Erkennung ist auf diesem System nicht verfügbar: Die native zxing-cpp-Bibliothek (ZXing.dll, libZXing.so,
/// libZXing.dylib) lässt sich nicht laden oder passt nicht zum .NET-Teil (fehlende Funktionen). Die Meldung beginnt mit
/// dem Fehlercode <see cref="ErrorCodes.DecoderUnavailable"/> und nennt den Grund des Systems (z. B. „GLIBC_2.38 not found“).
/// </summary>
public sealed class DecoderUnavailableException(string message) : Exception(message)
{
    /// <summary>Fehlercode für den Support.</summary>
    public string Code => ErrorCodes.DecoderUnavailable;
}

/// <summary>Fehlercodes für Meldungen, die der Support unabhängig von der Sprache zuordnen können muss (docs/error-codes.md).</summary>
public static class ErrorCodes
{
    /// <summary>Die native Erkennungsbibliothek (zxing-cpp) lässt sich nicht laden.</summary>
    public const string DecoderUnavailable = "LUP-0001";

    /// <summary>„LUP-0001: Text“ – der Code steht vor dem übersetzten Text, in jeder Sprache gleich.</summary>
    public static string Format(string code, string text) => $"{code}: {text}";
}

/// <summary>
/// Prüft einmal, ob sich die native zxing-cpp-Bibliothek laden lässt und alle Funktionen hat, die der .NET-Teil
/// (NuGet-Paket ZXingCpp) aufruft – bevor Lupe das erste Objekt von ZXingCpp anlegt.
/// <para>
/// Ohne diese Prüfung meldete jeder Scan „Kein Code gefunden“ (der Ladefehler fiel unter die Fehler eines einzelnen
/// Lesevorgangs), und der Prozess brach danach im Finalizer ab: Der Konstruktor von <c>ReaderOptions</c> wirft
/// <see cref="DllNotFoundException"/>, das halb angelegte Objekt bleibt für den Finalizer, und der ruft wieder die
/// fehlende Bibliothek auf. Deshalb hier vorab über <see cref="NativeLibrary"/> – dieselbe Suche wie bei DllImport.
/// </para>
/// </summary>
internal static class ZXingLibrary
{
    /// <summary>Name in den DllImport-Angaben von ZXingCpp (ZXing.dll, libZXing.so, libZXing.dylib).</summary>
    internal const string Name = "ZXing";

    private static readonly Lazy<string?> s_problem = new(() => Probe(Name, typeof(ZXingCpp.BarcodeReader).Assembly), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Null, wenn die Bibliothek lädt und vollständig ist; sonst die Meldung mit Fehlercode für Benutzer und Support.</summary>
    public static string? Problem => s_problem.Value;

    /// <summary>Wirft <see cref="DecoderUnavailableException"/>, wenn die Erkennung nicht verfügbar ist.</summary>
    public static void ThrowIfUnavailable()
    {
        if (Problem is { } problem) throw new DecoderUnavailableException(problem);
    }

    /// <summary>Meldung für einen Ladefehler, mit Fehlercode.</summary>
    internal static string Message(string reason) =>
        ErrorCodes.Format(ErrorCodes.DecoderUnavailable, string.Format(Strings.Core_Decoder_Unavailable, reason.Trim()));

    /// <summary>
    /// Lädt <paramref name="library"/> wie ein DllImport aus <paramref name="binding"/> und prüft, ob alle Funktionen
    /// da sind, die <paramref name="binding"/> per DllImport aus <paramref name="importName"/> (Standard: dieselbe)
    /// aufruft. Null = in Ordnung, sonst die Meldung.
    /// </summary>
    internal static string? Probe(string library, Assembly binding, string? importName = null)
    {
        IntPtr handle;
        try
        {
            handle = NativeLibrary.Load(library, binding, null);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or ArgumentException or EntryPointNotFoundException)
        {
            return Message(ex.Message);
        }
        // Die Bibliothek bleibt geladen: Die DllImport-Aufrufe von ZXingCpp verwenden dieselbe
        var missing = ImportsOf(binding, importName ?? library).Where(entry => !NativeLibrary.TryGetExport(handle, entry, out _)).Order(StringComparer.Ordinal).ToList();
        return missing.Count == 0 ? null : Message(string.Format(Strings.Core_Decoder_MissingFunctions, library, string.Join(", ", missing.Take(5))));
    }

    /// <summary>Lädt eine Datei direkt (für Tests mit einer bestimmten, z. B. kaputten Bibliothek).</summary>
    internal static string? ProbeFile(string path)
    {
        try
        {
            NativeLibrary.Free(NativeLibrary.Load(path));
            return null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or ArgumentException)
        {
            return Message(ex.Message);
        }
    }

    /// <summary>Die Einsprungpunkte aller DllImport-Methoden in <paramref name="binding"/>, die <paramref name="library"/> aufrufen.</summary>
    internal static IReadOnlyList<string> ImportsOf(Assembly binding, string library)
    {
        Type?[] types;
        try { types = binding.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types; }
        const BindingFlags all = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        return [.. types.Where(t => t is not null)
            .SelectMany(t => t!.GetMethods(all))
            .Select(m => (Method: m, Import: m.GetCustomAttribute<DllImportAttribute>()))
            .Where(x => x.Import is not null && x.Import.Value == library)
            .Select(x => x.Import!.EntryPoint ?? x.Method.Name)
            .Distinct(StringComparer.Ordinal)];
    }
}
