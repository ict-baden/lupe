using System.Runtime.InteropServices;
using Lupe.Core.Camera;
using Windows.Media.Capture.Frames;

using Lupe.Platform.Windows.Camera;

namespace Lupe.Tests.Camera;

public sealed class CameraProblemsTests
{
    [Theory]
    [InlineData(0x80070005u, "AccessDenied")]
    [InlineData(0xC00DB798u, "AccessDenied")]
    [InlineData(0x80070490u, "NotFound")]
    [InlineData(0xC00D36D5u, "NotFound")]
    [InlineData(0xC00DABE0u, "NoCamera")]
    [InlineData(0xC00D3EA2u, "Disconnected")]
    [InlineData(0x80070651u, "Disconnected")]
    [InlineData(0x8007048Fu, "Disconnected")]
    [InlineData(0xC00D3EA3u, "Preempted")]
    [InlineData(0xC00D3704u, "InUse")]
    [InlineData(0xC00D4E24u, "InUse")]
    [InlineData(0x80070020u, "InUse")]
    [InlineData(0x800705AAu, "InUse")]
    [InlineData(0xC00DABE7u, "NoColorStream")]
    [InlineData(0xC00D3E98u, "FormatNotSupported")]
    [InlineData(0x80040154u, "MediaFeaturesMissing")]
    [InlineData(0x8007007Eu, "MediaFeaturesMissing")]
    [InlineData(0x80004005u, "Unknown")]
    [InlineData(0u, "Unknown")]
    public void HRESULT_wird_eingeordnet(uint hresult, string expected)
    {
        Assert.Equal(Enum.Parse<CameraProblemKind>(expected), CameraProblems.Classify(unchecked((int)hresult)));
    }

    [Fact]
    public void Ausnahmen_werden_eingeordnet()
    {
        Assert.Equal(CameraProblemKind.AccessDenied, CameraProblems.FromException(new UnauthorizedAccessException()).Kind);
        Assert.Equal(CameraProblemKind.MediaFeaturesMissing, CameraProblems.FromException(new DllNotFoundException("mf.dll")).Kind);
        Assert.Equal(CameraProblemKind.Preempted, CameraProblems.FromException(new COMException("x", unchecked((int)0xC00D3EA3))).Kind);
        var unknown = CameraProblems.FromException(new InvalidOperationException("seltsam"));
        Assert.Equal(CameraProblemKind.Unknown, unknown.Kind);
        Assert.Equal("seltsam", unknown.Detail);
    }

    [Fact]
    public void Startstatus_des_Readers_wird_eingeordnet()
    {
        Assert.Equal(CameraProblemKind.Disconnected, CameraProblems.FromStartStatus(MediaFrameReaderStartStatus.DeviceNotAvailable).Kind);
        Assert.Equal(CameraProblemKind.InUse, CameraProblems.FromStartStatus(MediaFrameReaderStartStatus.ExclusiveControlNotAvailable).Kind);
        Assert.Equal(CameraProblemKind.FormatNotSupported, CameraProblems.FromStartStatus(MediaFrameReaderStartStatus.OutputFormatNotSupported).Kind);
        Assert.Equal(CameraProblemKind.Unknown, CameraProblems.FromStartStatus(MediaFrameReaderStartStatus.UnknownFailure).Kind);
    }

    [Fact]
    public void Nur_bei_Belegung_und_Unklarem_lohnt_der_geteilte_Modus()
    {
        Assert.True(new CameraProblem(CameraProblemKind.InUse).RetryShared);
        Assert.True(new CameraProblem(CameraProblemKind.Preempted).RetryShared);
        Assert.True(new CameraProblem(CameraProblemKind.Unknown).RetryShared);
        Assert.False(new CameraProblem(CameraProblemKind.AccessDenied).RetryShared);
        Assert.False(new CameraProblem(CameraProblemKind.NotFound).RetryShared);
        Assert.False(new CameraProblem(CameraProblemKind.NoCamera).RetryShared);
        Assert.False(new CameraProblem(CameraProblemKind.MediaFeaturesMissing).RetryShared);
    }

    [Fact]
    public void Konkreter_Fehler_schlägt_unbekannten()
    {
        var unknown = new CameraProblem(CameraProblemKind.Unknown, 1);
        var inUse = new CameraProblem(CameraProblemKind.InUse, 2);
        var notFound = new CameraProblem(CameraProblemKind.NotFound, 3);
        Assert.Same(inUse, CameraProblems.MoreSpecific(unknown, inUse));
        Assert.Same(inUse, CameraProblems.MoreSpecific(inUse, notFound));
        Assert.Same(inUse, CameraProblems.MoreSpecific(inUse, unknown));
        Assert.Same(unknown, CameraProblems.MoreSpecific(unknown, null));
    }

    public static TheoryData<string, string> Meldungen => new()
    {
        { nameof(CameraProblemKind.NoCamera), "Keine Kamera gefunden" },
        { nameof(CameraProblemKind.NotFound), "wurde nicht gefunden" },
        { nameof(CameraProblemKind.Disconnected), "abgezogen" },
        { nameof(CameraProblemKind.InUse), "anderen Programm" },
        { nameof(CameraProblemKind.Preempted), "übernommen" },
        { nameof(CameraProblemKind.StreamShutdown), "erneut" },
        { nameof(CameraProblemKind.NoColorStream), "kein Farbbild" },
        { nameof(CameraProblemKind.FormatNotSupported), "Bildformat" },
        { nameof(CameraProblemKind.MediaFeaturesMissing), "Media Feature Pack" },
        { nameof(CameraProblemKind.Unknown), "Fehler 0x80004005" },
    };

    [Theory]
    [MemberData(nameof(Meldungen))]
    public void Meldungen_sind_deutsch_und_sagen_was_zu_tun_ist(string kindName, string expected)
    {
        var kind = Enum.Parse<CameraProblemKind>(kindName);
        string text = CameraProblems.Describe(new CameraProblem(kind, unchecked((int)0x80004005)), "Logitech C920", CameraPrivacyState.Allowed);
        Assert.Contains(expected, text);
        if (kind is not (CameraProblemKind.NoCamera or CameraProblemKind.MediaFeaturesMissing))
            Assert.Contains("„Logitech C920“", text);
        Assert.DoesNotContain("Exception", text);
        Assert.EndsWith(".", text);
    }

    [Fact]
    public void Unbekannter_Fehler_nennt_Code_und_gekürzte_Systemmeldung()
    {
        string text = CameraProblems.Describe(new CameraProblem(CameraProblemKind.Unknown, unchecked((int)0xC00D1234), new string('x', 500)), "Cam");
        Assert.Contains("0xC00D1234", text);
        Assert.Contains("…", text);
        Assert.True(text.Length < 450, text);
        Assert.Contains("unbekannter Fehler", CameraProblems.Describe(new CameraProblem(CameraProblemKind.Unknown), "Cam"));
    }

    public static TheoryData<string, string> Datenschutz => new()
    {
        { nameof(CameraPrivacyState.PolicyBlocked), "Richtlinie" },
        { nameof(CameraPrivacyState.DeviceOff), "für dieses Gerät ausgeschaltet" },
        { nameof(CameraPrivacyState.AppsOff), "für Apps ist ausgeschaltet" },
        { nameof(CameraPrivacyState.DesktopAppsOff), "Desktop-Apps den Zugriff auf die Kamera erlauben" },
        { nameof(CameraPrivacyState.ThisAppOff), "Lupe den Kamerazugriff entzogen" },
        { nameof(CameraPrivacyState.Allowed), "verweigert den Zugriff auf die Kamera „Cam“" },
    };

    [Theory]
    [MemberData(nameof(Datenschutz))]
    public void Zugriff_verweigert_nennt_den_richtigen_Schalter(string stateName, string expected)
    {
        var state = Enum.Parse<CameraPrivacyState>(stateName);
        string text = CameraProblems.Describe(new CameraProblem(CameraProblemKind.AccessDenied), "Cam", state);
        Assert.Contains(expected, text);
        if (state != CameraPrivacyState.PolicyBlocked) Assert.Contains("Datenschutz und Sicherheit → Kamera", text);
    }

    [Fact]
    public void Datenschutz_strengste_Sperre_gewinnt()
    {
        Assert.Equal(CameraPrivacyState.Allowed, CameraPrivacy.Evaluate(null, null, null, null, null, null));
        Assert.Equal(CameraPrivacyState.Allowed, CameraPrivacy.Evaluate(1, 0, "Allow", "Allow", "Allow", "Allow"));
        Assert.Equal(CameraPrivacyState.ThisAppOff, CameraPrivacy.Evaluate(null, null, "Allow", "Allow", "Allow", "Deny"));
        Assert.Equal(CameraPrivacyState.DesktopAppsOff, CameraPrivacy.Evaluate(null, null, "Allow", "Allow", "deny", "Deny"));
        Assert.Equal(CameraPrivacyState.AppsOff, CameraPrivacy.Evaluate(null, null, "Allow", " Deny ", "Deny", null));
        Assert.Equal(CameraPrivacyState.DeviceOff, CameraPrivacy.Evaluate(null, 1, "Deny", "Deny", "Deny", null));
        Assert.Equal(CameraPrivacyState.PolicyBlocked, CameraPrivacy.Evaluate(0, null, "Allow", "Allow", "Allow", null));
        Assert.Equal(CameraPrivacyState.PolicyBlocked, CameraPrivacy.Evaluate(null, 2, "Allow", "Allow", "Allow", null));
    }

    [Fact]
    public void Registry_Schlüssel_der_eigenen_App_wie_Windows_ihn_ablegt()
    {
        Assert.Equal(@"C:#Program Files#Lupe#Lupe.exe", CameraPrivacy.AppConsentKeyName(@"C:\Program Files\Lupe\Lupe.exe"));
        Assert.Null(CameraPrivacy.AppConsentKeyName(null));
        Assert.Null(CameraPrivacy.AppConsentKeyName(" "));
    }

    [Fact]
    public void Datenschutz_lesen_aus_der_echten_Registry_wirft_nicht()
    {
        var state = CameraPrivacy.Read();
        Assert.True(Enum.IsDefined(state));
        // Meldung ohne vorgegebenen Zustand liest selbst aus der Registry
        Assert.False(string.IsNullOrWhiteSpace(CameraProblems.Describe(new CameraProblem(CameraProblemKind.AccessDenied), "Cam")));
    }
}
