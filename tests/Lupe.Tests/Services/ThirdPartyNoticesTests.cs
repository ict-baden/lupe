// SPDX-License-Identifier: MIT
using Lupe.Services;

namespace Lupe.Tests.Services;

/// <summary>Lizenztexte ohne Datei neben dem Programm: nie unter einem festen Namen im gemeinsamen Temp-Ordner.</summary>
public sealed class ThirdPartyNoticesTests
{
    [Fact]
    public void Eingebettete_Texte_gehen_in_einen_eigenen_neuen_Ordner()
    {
        var folder = ThirdPartyNotices.TempFolder;

        Assert.True(Directory.Exists(folder));
        Assert.Equal(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetDirectoryName(folder));
        Assert.StartsWith("lupe-", Path.GetFileName(folder));
        Assert.NotEqual(Path.Combine(Path.GetTempPath(), "Lupe"), folder, StringComparer.OrdinalIgnoreCase);
        // Einer je Prozess: Jeder weitere Klick auf „Lizenz“ legt keinen neuen Ordner an
        Assert.Equal(folder, ThirdPartyNotices.TempFolder);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(folder));
    }
}
