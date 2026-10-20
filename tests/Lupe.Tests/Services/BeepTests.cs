// SPDX-License-Identifier: MIT
using Lupe.Services;

namespace Lupe.Tests.Services;

/// <summary>Der Treffer-Ton als Datei (Linux, macOS): nie unter einem festen Namen im gemeinsamen Temp-Ordner.</summary>
public sealed class BeepTests
{
    [Fact]
    public void Tondatei_liegt_in_einem_eigenen_neuen_Ordner()
    {
        var first = Beep.WriteTempWave();
        var second = Beep.WriteTempWave();
        try
        {
            Assert.True(File.Exists(first));
            Assert.Equal(Beep.CreateWave(), File.ReadAllBytes(first));
            // Jedes Mal ein neuer Ordner – einen vorher angelegten Symlink kann es dort nicht geben
            Assert.NotEqual(Path.GetDirectoryName(first), Path.GetDirectoryName(second));
            Assert.NotEqual(Path.Combine(Path.GetTempPath(), $"lupe-{Environment.UserName}"), Path.GetDirectoryName(first));
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.GetDirectoryName(first)!));
        }
        finally
        {
            foreach (var file in new[] { first, second }) Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
        }
    }
}
