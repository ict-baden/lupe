// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Text;
using Lupe.Core.Model;

namespace Lupe.Tests.App;

/// <summary>
/// Startet die gebaute Lupe.exe (gleiche Konfiguration wie die Tests) als eigenen Prozess. Das Testprojekt
/// verweist nur auf Lupe.Core – die App wird deshalb über ihre Befehlszeile geprüft, wie ein Skript sie nutzt.
/// Gebaut wird sie mit <c>dotnet build Lupe.sln</c>; <c>dotnet test tests/Lupe.Tests</c> allein baut sie nicht.
/// </summary>
internal static class AppExe
{
    private static readonly Lazy<string?> PathLazy = new(Find);

    public static string? Path => PathLazy.Value;

    public sealed record Result(int ExitCode, string Out, string Err)
    {
        public string[] Lines => Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
    }

    public static Result Run(TimeSpan timeout, params string[] args)
    {
        var exe = Path ?? throw new InvalidOperationException("Lupe.exe nicht gefunden.");
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            // Inhalte der Tests sind ASCII – gleich in UTF-8 und jeder OEM-Codepage, in die Lupe für Pipes schreibt
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"Lupe.exe {string.Join(' ', args)} lief länger als {timeout.TotalSeconds:0} s.");
        }
        process.WaitForExit();
        return new Result(process.ExitCode, stdout.Result.TrimStart('\uFEFF'), stderr.Result.TrimStart('\uFEFF'));
    }

    public static Result Run(params string[] args) => Run(TimeSpan.FromSeconds(60), args);

    /// <summary>Lupe.exe neben den Tests: src/Lupe/bin/&lt;Konfiguration&gt;/&lt;Zielframework&gt;/Lupe.exe.</summary>
    private static string? Find()
    {
        var baseDir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        string tfm = baseDir.Name, configuration = baseDir.Parent?.Name ?? "Debug";
        for (var dir = baseDir; dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(System.IO.Path.Combine(dir.FullName, "Lupe.sln"))) continue;
            var exe = System.IO.Path.Combine(dir.FullName, "src", "Lupe", "bin", configuration, tfm, "Lupe.exe");
            return File.Exists(exe) ? exe : null;
        }
        return null;
    }

    // ================= Bilddateien für die Tests =================

    /// <summary>Schreibt ein Bild als 24-Bit-BMP (liest jeder Windows-Decoder, ohne Bibliothek zu schreiben).</summary>
    public static void WriteBmp(PixelImage image, string path)
    {
        int rowSize = (image.Width * 3 + 3) & ~3;
        int dataSize = rowSize * image.Height;
        using var w = new BinaryWriter(File.Create(path));
        w.Write((byte)'B');
        w.Write((byte)'M');
        w.Write(54 + dataSize);
        w.Write(0);
        w.Write(54);
        w.Write(40);
        w.Write(image.Width);
        w.Write(image.Height); // positiv = von unten nach oben
        w.Write((short)1);
        w.Write((short)24);
        w.Write(0);
        w.Write(dataSize);
        w.Write(2835);
        w.Write(2835);
        w.Write(0);
        w.Write(0);
        var row = new byte[rowSize];
        for (int y = image.Height - 1; y >= 0; y--)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int i = y * image.Stride + x * 4;
                row[x * 3] = image.Bgra[i];
                row[x * 3 + 1] = image.Bgra[i + 1];
                row[x * 3 + 2] = image.Bgra[i + 2];
            }
            w.Write(row);
        }
    }

    /// <summary>Breite und Höhe aus dem Kopf einer PNG-Datei.</summary>
    public static (int Width, int Height) PngSize(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 24 || bytes[1] != 'P' || bytes[2] != 'N' || bytes[3] != 'G') throw new InvalidDataException($"{path} ist kein PNG.");
        static int BigEndian(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
        return (BigEndian(bytes, 16), BigEndian(bytes, 20));
    }
}

/// <summary>Braucht die gebaute Lupe.exe – fehlt sie, wird der Test übersprungen (mit Hinweis, wie man sie baut).</summary>
public sealed class AppFactAttribute : FactAttribute
{
    public AppFactAttribute()
    {
        if (AppExe.Path is null) Skip = "Lupe.exe ist nicht gebaut – vorher dotnet build Lupe.sln.";
    }
}

/// <summary>Wie <see cref="AppFactAttribute"/>, für Theorien.</summary>
public sealed class AppTheoryAttribute : TheoryAttribute
{
    public AppTheoryAttribute()
    {
        if (AppExe.Path is null) Skip = "Lupe.exe ist nicht gebaut – vorher dotnet build Lupe.sln.";
    }
}

/// <summary>Ein eigener, leerer Ordner je Test unter %TEMP%, wird danach gelöscht.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Lupe-Test-{Guid.NewGuid():N}");

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
