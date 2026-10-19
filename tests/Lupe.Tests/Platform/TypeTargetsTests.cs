// SPDX-License-Identifier: MIT
using Lupe.Core.Platform;

namespace Lupe.Tests.Platform;

/// <summary>Wohin „Eintippen“ schreibt, je nachdem was das System über das Vordergrundfenster weiß.</summary>
public sealed class TypeTargetsTests
{
    private sealed class FakeActive(bool supported, nint own) : IActiveWindow
    {
        public bool IsSupported => supported;
        public nint Foreground() => 0;
        public bool IsOwnWindow(nint window) => window != 0 && window == own;
        public Task<string?> ActivateAsync(nint window, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    [Fact]
    public void Bekanntes_fremdes_Fenster_ist_das_Ziel()
    {
        Assert.Equal((nint)42, TypeTargets.From(new FakeActive(true, own: 7), 42, lupeActive: false));
    }

    [Fact]
    public void Lupe_selbst_ist_kein_Ziel()
    {
        Assert.Equal(0, TypeTargets.From(new FakeActive(true, own: 7), 7, lupeActive: true));
    }

    [Fact]
    public void Kein_Fenster_bei_unterstuetztem_System_heisst_nicht_tippen()
    {
        // X11 oder Windows ohne Vordergrundfenster: lieber nichts tippen als irgendwohin
        Assert.Equal(0, TypeTargets.From(new FakeActive(true, own: 7), 0, lupeActive: false));
    }

    [Fact]
    public void Wayland_tippt_in_das_Fenster_mit_dem_Fokus()
    {
        var wayland = new FakeActive(false, own: 0);
        Assert.Equal(TypeTargets.FocusedWindow, TypeTargets.From(wayland, 0, lupeActive: false));
        Assert.Equal(0, TypeTargets.From(wayland, 0, lupeActive: true)); // Lupe hat den Fokus: nicht in Lupe selbst tippen
    }
}
