// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Text;

namespace Lupe.Tests.Localization;

/// <summary>
/// Ohne Kulturdaten (DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1, etwa in Containern ohne ICU) gibt es keine Kultur außer der
/// neutralen – Lupe startet trotzdem, mit englischen Texten. Der Modus gilt für den ganzen Prozess, deshalb läuft Lupe
/// hier als eigener Prozess (<c>dotnet Lupe.dll</c> aus der Ausgabe der Tests). Läuft auf jedem System.
/// </summary>
public sealed class InvariantGlobalizationTests
{
    private static readonly string LupeDll = Path.Combine(AppContext.BaseDirectory, "Lupe.dll");
    private static readonly string QrImage = Path.Combine(AppContext.BaseDirectory, "TestData", "smoke-qr.png");

    /// <summary>Die neutrale Fassung (net10.0) liest unter Windows keine Bilddateien – dort prüft das das Windows-Ziel.</summary>
#if WINDOWS
    private static readonly bool ReadsImageFiles = true;
#else
    private static readonly bool ReadsImageFiles = !OperatingSystem.IsWindows();
#endif

    [Fact]
    public void Hilfe_ohne_Kulturdaten()
    {
        foreach (var help in new[] { "--hilfe", "--help" })
        {
            var (exit, output, error) = Run(TimeSpan.FromSeconds(60), help);
            Assert.True(exit == 0, $"Exitcode {exit}: {error}");
            Assert.Contains("lesen", output);
            Assert.Equal("", error);
        }
    }

    [Fact]
    public void Lesen_Version_und_Info_ohne_Kulturdaten()
    {
        int exit;
        string output, error;
        if (ReadsImageFiles)
        {
            (exit, output, error) = Run(TimeSpan.FromSeconds(60), "lesen", QrImage);
            Assert.True(exit == 0, $"Exitcode {exit}: {error}");
            Assert.Equal("QR_CODE\thttps://github.com/ict-baden/lupe", output.Trim());
        }

        foreach (var command in new[] { "--version", "--info" })
        {
            (exit, output, error) = Run(TimeSpan.FromSeconds(60), command);
            Assert.True(exit == 0, $"{command}: Exitcode {exit}: {error}");
            Assert.StartsWith("Lupe ", output);
        }
    }

    [Fact]
    public void Hauptfenster_ohne_Kulturdaten()
    {
        // --screenshots baut das Hauptfenster mit allen Seiten wie beim normalen Start, nur ohne Bildschirm
        var folder = Directory.CreateTempSubdirectory("lupe-invariant-");
        try
        {
            var (exit, _, error) = Run(TimeSpan.FromMinutes(4), "--screenshots", folder.FullName, "--theme", "light");
            Assert.True(exit == 0, $"Exitcode {exit}: {error}");
            Assert.Contains(folder.GetFiles("*.png"), f => f.Name.StartsWith("einstellungen", StringComparison.Ordinal));
        }
        finally
        {
            try { folder.Delete(recursive: true); }
            catch (IOException) { }
        }
    }

    private static (int Exit, string Output, string Error) Run(TimeSpan timeout, params string[] args)
    {
        Assert.True(File.Exists(LupeDll), $"{LupeDll} fehlt (Projektverweis auf src/Lupe).");
        var info = new ProcessStartInfo(DotnetHost())
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        info.ArgumentList.Add(LupeDll);
        foreach (var a in args) info.ArgumentList.Add(a);
        info.Environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1";
        info.Environment.Remove("LUPE_LANGUAGE");
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"Lupe {string.Join(' ', args)} lief länger als {timeout.TotalSeconds:0} s.");
        }
        process.WaitForExit();
        return (process.ExitCode, output.Result.TrimStart('﻿'), error.Result.TrimStart('﻿'));
    }

    /// <summary>Der dotnet-Host, der die Tests ausführt (dotnet test setzt DOTNET_HOST_PATH), sonst der aus dem PATH.</summary>
    private static string DotnetHost()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host)) return host;
        if (Environment.ProcessPath is { } self && Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) return self;
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root)
        {
            var candidate = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate)) return candidate;
        }
        return "dotnet";
    }
}
