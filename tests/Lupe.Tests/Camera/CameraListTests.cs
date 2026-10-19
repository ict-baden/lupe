// SPDX-License-Identifier: MIT
using Lupe.Core.Camera;

namespace Lupe.Tests.Camera;

public sealed class CameraListTests
{
    private static CameraSourceCandidate Color(string? device = null) => new(IsColor: true, IsVideo: true, device);
    private static CameraSourceCandidate Infrared(string? device = null) => new(IsColor: false, IsVideo: true, device);
    private static CameraSourceCandidate Photo(string? device = null) => new(IsColor: true, IsVideo: false, device);

    [Fact]
    public void Leer_ergibt_leere_Liste()
    {
        Assert.Empty(CameraList.Build([]));
    }

    [Fact]
    public void Nur_Gruppen_mit_Farbvideo()
    {
        var list = CameraList.Build(
        [
            new("g1", "Integrierte Kamera", [Color("d1")]),
            new("g2", "Windows Hello IR", [Infrared("d2")]),
            new("g3", "Nur Foto", [Photo("d3")]),
            new("g4", "Ohne Ströme", []),
            new("  ", "Ohne Kennung", [Color("d5")]),
        ]);
        var only = Assert.Single(list);
        Assert.Equal(new CameraInfo("g1", "Integrierte Kamera"), only);
    }

    [Fact]
    public void Dieselbe_Kamera_in_mehreren_Gruppen_nur_einmal_die_schlichteste_gewinnt()
    {
        var list = CameraList.Build(
        [
            new("hello", "Surface Kamera", [Color("dev-front"), Infrared("dev-ir")]),
            new("front", "Surface Kamera", [Color("DEV-FRONT")]),
            new("usb", "USB-Kamera", [Color("dev-usb")]),
        ]);
        Assert.Equal(["front", "usb"], list.Select(c => c.Id));
        Assert.Equal(["Surface Kamera", "USB-Kamera"], list.Select(c => c.Name));
    }

    [Fact]
    public void Gruppen_ohne_Gerätekennung_werden_nicht_zusammengelegt()
    {
        var list = CameraList.Build(
        [
            new("a", "Kamera", [Color()]),
            new("b", "Kamera", [Color()]),
        ]);
        Assert.Equal(["Kamera (1)", "Kamera (2)"], list.Select(c => c.Name));
    }

    [Theory]
    [InlineData(null, "Kamera")]
    [InlineData("", "Kamera")]
    [InlineData("   ", "Kamera")]
    [InlineData("\t\r\n", "Kamera")]
    [InlineData("  Logitech   C920\t ", "Logitech C920")]
    [InlineData("Kamera\u0000mit\u0007Steuerzeichen", "Kamera mit Steuerzeichen")]
    [InlineData("Größenwahn-Cam „Ü“", "Größenwahn-Cam „Ü“")]
    public void Namen_bereinigt(string? raw, string expected)
    {
        Assert.Equal(expected, CameraList.CleanName(raw));
    }

    [Fact]
    public void Gleiche_Namen_werden_durchnummeriert_Groß_Klein_egal()
    {
        var names = CameraList.MakeUnique(["USB Camera", "Integrated Webcam", "usb camera", "USB Camera"]);
        Assert.Equal(["USB Camera (1)", "Integrated Webcam", "usb camera (2)", "USB Camera (3)"], names);
    }

    [Fact]
    public void Nummerierung_kollidiert_nicht_mit_vorhandenem_Namen()
    {
        var names = CameraList.MakeUnique(["Cam", "Cam", "Cam (1)"]);
        Assert.Equal(3, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("Cam (1)", names[2]);
        Assert.All(names.Take(2), n => Assert.StartsWith("Cam (", n));
    }

    [Fact]
    public void Namenlose_Kameras_heißen_Kamera_und_sind_unterscheidbar()
    {
        var names = CameraList.MakeUnique([null, "", "  "]);
        Assert.Equal(["Kamera (1)", "Kamera (2)", "Kamera (3)"], names);
    }
}
