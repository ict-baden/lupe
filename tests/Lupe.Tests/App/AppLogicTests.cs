using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Lupe.Core.Model;
using Lupe.Core.Screen;

namespace Lupe.Tests.App;

/// <summary>Reine Logik der App (ohne Fenster), direkt aus der gebauten Lupe.dll – siehe <see cref="AppInternals"/>.</summary>
public sealed class AppLogicTests
{
    // ================= Fensterlage (Auswahl mehrerer Codes) =================

    private static (int X, int Y) CenterOn(int x, int y, int width, int height, PixelRect workArea) =>
        ((int, int))AppInternals.Call("Lupe.Services.Placement", "CenterOn", x, y, width, height, workArea)!;

    [AppFact]
    public void Fenster_mittig_auf_der_Auswahl()
    {
        Assert.Equal((800, 350), CenterOn(1000, 500, 400, 300, new PixelRect(0, 0, 1920, 1040)));
    }

    [AppFact]
    public void Monitor_ueber_dem_Hauptmonitor_bleibt_bei_negativem_Y()
    {
        // Auswahl oben am Rand eines Monitors über dem Hauptmonitor: Das Fenster darf nicht auf Y = 0 (anderer Monitor) rutschen
        var top = new PixelRect(0, -1080, 1920, 1040);
        var (x, y) = CenterOn(960, -1000, 780, 630, top);
        Assert.Equal(570, x);
        Assert.Equal(-1080, y);
    }

    [AppFact]
    public void Monitor_links_vom_Hauptmonitor_bleibt_ganz_darauf()
    {
        var left = new PixelRect(-1920, 0, 1920, 1040);
        var (x, y) = CenterOn(-100, 500, 780, 630, left);
        Assert.Equal(-780, x); // rechter Rand genau an der Monitorgrenze, nicht darüber hinaus
        Assert.Equal(185, y);
    }

    [AppFact]
    public void Zu_grosses_Fenster_zeigt_die_linke_obere_Ecke()
    {
        Assert.Equal((0, 0), CenterOn(960, 520, 3000, 2000, new PixelRect(0, 0, 1920, 1040)));
        Assert.Equal((-2560, -300), CenterOn(-1280, 400, 3000, 2000, new PixelRect(-2560, -300, 2560, 1400)));
    }

    // ================= Formate aus den Einstellungen =================

    private static IReadOnlyList<string>? Usable(string[] wanted, string[]? known) =>
        (IReadOnlyList<string>?)AppInternals.Call("Lupe.Services.FormatFilter", "Usable", wanted, known);

    [AppFact]
    public void Leere_Formatliste_heisst_alle()
    {
        Assert.Null(Usable([], ["QR_CODE", "EAN_13"]));
    }

    [AppFact]
    public void Unbekannte_Formate_aus_der_Datei_fallen_weg()
    {
        Assert.Equal(["QR_CODE"], Usable(["qr_code", "GIBT_ES_NICHT"], ["QR_CODE", "EAN_13"]));
        Assert.Equal(["QR_CODE", "EAN_13"], Usable([" EAN_13 ", "QR_CODE"], ["QR_CODE", "EAN_13"]));
    }

    [AppFact]
    public void Nur_unbekannte_Formate_heisst_alle_statt_keins()
    {
        Assert.Null(Usable(["GIBT_ES_NICHT"], ["QR_CODE", "EAN_13"]));
    }

    [AppFact]
    public void Ohne_bekannte_Liste_bleiben_die_Formate_unveraendert()
    {
        Assert.Equal(["IRGENDWAS"], Usable(["IRGENDWAS"], null));
    }

    // ================= Ausgabezeile der Befehlszeile =================

    private static string Escape(string text) => (string)AppInternals.Call("Lupe.Cli.CodeLine", "Escape", text)!;

    [AppFact]
    public void Steuerzeichen_werden_maskiert_der_Schraegstrich_nicht()
    {
        Assert.Equal(@"a\r\nb\tc\x1Dd\x7F\x00e\", Escape("a\r\nb\tc\u001Dd\u007F\0e\\"));
    }

    [AppFact]
    public void Gewoehnlicher_Inhalt_bleibt_wie_er_ist()
    {
        Assert.Equal("https://example.org/a?b=1", Escape("https://example.org/a?b=1"));
        Assert.Equal(@"WIFI:T:WPA;S:Netz\;1;P:geheim;;", Escape(@"WIFI:T:WPA;S:Netz\;1;P:geheim;;"));
        Assert.Equal("Grüße 😀", Escape("Grüße 😀"));
    }

    // ================= Umzug aus oberflaeche.json =================

    private static LupeSettings Migrate(LupeSettings settings, string path) =>
        (LupeSettings)AppInternals.Call("Lupe.Services.LegacyPreferences", "Migrate", settings, path)!;

    [AppFact]
    public void Alte_oberflaeche_json_wird_in_die_Einstellungen_uebernommen()
    {
        using var temp = new TempFolder();
        var path = temp.File("oberflaeche.json");
        File.WriteAllText(path, """
            {
              "KeepRunningInTray": false,
              "TrayHintShown": true
            }
            """);

        var migrated = Migrate(new LupeSettings { Sound = false }, path);

        Assert.False(migrated.KeepRunningInTray);
        Assert.True(migrated.TrayHintShown);
        Assert.False(migrated.Sound); // der Rest bleibt
    }

    [AppFact]
    public void Ohne_oder_mit_kaputter_alter_Datei_bleibt_alles_wie_es_ist()
    {
        using var temp = new TempFolder();
        var settings = new LupeSettings();
        Assert.Same(settings, Migrate(settings, temp.File("gibt-es-nicht.json")));

        var broken = temp.File("oberflaeche.json");
        File.WriteAllText(broken, "{ kaputt");
        Assert.Same(settings, Migrate(settings, broken));
    }

    [AppFact]
    public void Umzug_ueberschreibt_keine_neueren_Werte()
    {
        using var temp = new TempFolder();
        var path = temp.File("oberflaeche.json");
        File.WriteAllText(path, """{ "KeepRunningInTray": true, "TrayHintShown": false }""");

        var migrated = Migrate(new LupeSettings { KeepRunningInTray = false, TrayHintShown = true }, path);

        Assert.False(migrated.KeepRunningInTray);
        Assert.True(migrated.TrayHintShown);
    }

    // ================= Kamera: Live-Decoder =================

    [AppFact]
    public void Kamera_Seite_erkennt_mit_dem_schonenden_Live_Decoder()
    {
        // Die Seite selbst braucht WPF (nicht im Testprozess) – deshalb die Metadaten der gebauten Lupe.dll lesen
        var fields = FieldTypes("Lupe.UI.Pages", "CameraPage");

        Assert.Contains("Lupe.Core.Decoding.LiveBarcodeDecoder", fields);
        Assert.DoesNotContain("Lupe.Core.Decoding.BarcodeDecoder", fields);
    }

    /// <summary>Die Typen aller Felder eines Typs der App, als „Namensraum.Name“.</summary>
    private static List<string> FieldTypes(string ns, string name)
    {
        var dll = Path.Combine(Path.GetDirectoryName(AppExe.Path)!, "Lupe.dll");
        using var stream = File.OpenRead(dll);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var type = md.TypeDefinitions.Select(md.GetTypeDefinition)
            .Single(t => md.GetString(t.Namespace) == ns && md.GetString(t.Name) == name);
        var names = new TypeNames(md);
        return type.GetFields().Select(h => md.GetFieldDefinition(h).DecodeSignature(names, null)).ToList();
    }

    /// <summary>Gibt Typen in Signaturen als Namen zurück (Generika, Arrays usw. nur grob – hier geht es um einfache Felder).</summary>
    private sealed class TypeNames(MetadataReader md) : ISignatureTypeProvider<string, object?>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var t = md.GetTypeDefinition(handle);
            return $"{md.GetString(t.Namespace)}.{md.GetString(t.Name)}";
        }
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var t = md.GetTypeReference(handle);
            return $"{md.GetString(t.Namespace)}.{md.GetString(t.Name)}";
        }
        public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) => "spec";
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[,]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPinnedType(string elementType) => elementType;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => $"{genericType}<{string.Join(",", typeArguments)}>";
        public string GetGenericMethodParameter(object? context, int index) => $"!!{index}";
        public string GetGenericTypeParameter(object? context, int index) => $"!{index}";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
    }

    // ================= Einzeilige Anzeige in Listen =================

    private static string OneLine(string text, int max) => (string)AppInternals.Call("Lupe.Labels", "OneLine", text, max)!;

    [AppFact]
    public void Einzeilig_mit_Zeilenumbruch_als_Zeichen_und_gekuerzt()
    {
        Assert.Equal("BEGIN⏎N:Muster", OneLine("BEGIN\r\nN:Muster", 400));
        Assert.Equal("a b·c", OneLine("a\tb\u001Dc", 400));
        Assert.Equal("abcde …", OneLine("abcdefgh", 5));
    }

    // ================= Lizenztexte und Formatnamen =================

    [AppFact]
    public void Lizenztexte_eingebettet_und_neben_der_Exe()
    {
        var text = (string?)AppInternals.Call("Lupe.Services.ThirdPartyNotices", "Text");
        Assert.NotNull(text);
        foreach (var part in new[] { "zxing-cpp 3.1.1", "Apache License", "libzint", "libzueci", "BSD 3-Clause", "Hoehrmann", "MIT" })
            Assert.Contains(part, text);
        // Dieselbe Fassung liegt neben Lupe.exe (ZIP und MSI liefern sie so aus)
        var file = Path.Combine(Path.GetDirectoryName(AppExe.Path!)!, "THIRD-PARTY-NOTICES.txt");
        Assert.Equal(text.ReplaceLineEndings(), File.ReadAllText(file).ReplaceLineEndings());
    }

    private static string FormatLabel(string name) => (string)AppInternals.Call("Lupe.Labels", "Format", name)!;

    [AppFact]
    public void Jedes_Format_hat_einen_Anzeigenamen()
    {
        // Ohne Eintrag stünde in Liste und Einstellungen „MICRO QR CODE“ statt „Micro-QR-Code“
        foreach (var name in Lupe.Core.Decoding.BarcodeDecoder.AllFormats.Where(n => n != "ITF"))
            Assert.NotEqual(name.Replace('_', ' '), FormatLabel(name));
        // Frühere Formate im Verlauf bleiben lesbar
        Assert.Equal("Intelligent Mail", FormatLabel("IMB"));
    }

    // ================= Linux ohne X-Server =================

    private static string NoDisplay(string? display, string? wayland) =>
        (string)AppInternals.Call("Lupe.Program", "NoDisplayMessage", display, wayland)!;

    [AppFact]
    public void Ohne_X_Server_unter_Wayland_Hinweis_auf_XWayland()
    {
        var text = NoDisplay(null, "wayland-1");
        Assert.Contains("DISPLAY ist nicht gesetzt", text);
        Assert.Contains("XWayland", text);
        Assert.Contains("lupe lesen", text);
        Assert.Contains("DISPLAY=:5 ist nicht erreichbar", NoDisplay(":5", null));
        Assert.DoesNotContain("XWayland", NoDisplay(":5", null));
    }
}
