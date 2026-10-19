// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Lupe.UI.Theme;

/// <summary>
/// Hell/Dunkel nach dem System (Avalonia meldet Wechsel unter Windows, macOS und Linux), Akzentfarbe des Systems,
/// Schriften je System und – unter Windows 11 – Mica als Fensterhintergrund.
/// </summary>
public static class ThemeManager
{
    // Windows-11-Standardblau – für Screenshots fest (die Bilder sollen nicht von der Akzentfarbe des Rechners abhängen)
    private static readonly Dictionary<string, Color> FixedAccent = new()
    {
        ["SystemAccentColor"] = Color.Parse("#0078D4"),
        ["SystemAccentColorDark1"] = Color.Parse("#0067C0"),
        ["SystemAccentColorDark2"] = Color.Parse("#003E92"),
        ["SystemAccentColorDark3"] = Color.Parse("#001A68"),
        ["SystemAccentColorLight1"] = Color.Parse("#0093F9"),
        ["SystemAccentColorLight2"] = Color.Parse("#60CDFF"),
        ["SystemAccentColorLight3"] = Color.Parse("#99EBFF"),
    };

    private static Application? _app;
    private static bool _fixedAccent;

    public static bool IsDark => _app?.ActualThemeVariant == ThemeVariant.Dark;

    /// <summary>Schriften setzen und dem System folgen (Hell/Dunkel, Akzentfarbe).</summary>
    public static void Initialize(Application app)
    {
        if (_app is not null) return;
        _app = app;
        SetFonts(app.Resources);
        app.ActualThemeVariantChanged += (_, _) => UpdateDerived();
        if (app.PlatformSettings is { } settings) settings.ColorValuesChanged += (_, _) => UpdateDerived();
        UpdateDerived();
    }

    /// <summary>Für Screenshots: festes Theme und feste Akzentfarbe.</summary>
    public static void Force(bool dark)
    {
        if (_app is null) return;
        _fixedAccent = true;
        foreach (var (key, color) in FixedAccent) _app.Resources[key] = color;
        _app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        UpdateDerived();
    }

    private static void SetFonts(IResourceDictionary r)
    {
        const string inter = "fonts:Inter#Inter";
        if (OperatingSystem.IsWindows())
        {
            r["UiFont"] = new FontFamily($"Segoe UI Variable Text, Segoe UI, {inter}");
            r["DisplayFont"] = new FontFamily($"Segoe UI Variable Display, Segoe UI, {inter}");
            r["MonoFont"] = new FontFamily("Cascadia Mono, Consolas, Courier New");
            // Windows bringt die Symbolschrift mit (Windows 10: MDL2 mit denselben Zeichen); sonst Lupes eigene
            r["IconFont"] = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, avares://Lupe/Assets/LupeIcons.ttf#Lupe Icons");
        }
        else
        {
            // Inter ist eingebettet – sieht auf jedem Linux und macOS gleich aus, auch ohne installierte Schriften
            r["UiFont"] = new FontFamily(inter);
            r["DisplayFont"] = new FontFamily(inter);
            r["MonoFont"] = new FontFamily(OperatingSystem.IsMacOS() ? "Menlo, Monaco, monospace" : "DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, monospace");
            // Fluent-Symbole (MIT, Microsoft) auf denselben Codepunkten wie Segoe Fluent Icons
            r["IconFont"] = new FontFamily("avares://Lupe/Assets/LupeIcons.ttf#Lupe Icons");
        }
    }

    /// <summary>Akzentfarben wie WinUI (hell: Dark1/Dark2, dunkel: Light2/Light3) und die daraus getönten Flächen.</summary>
    private static void UpdateDerived()
    {
        if (_app is null) return;
        bool dark = IsDark;
        var r = _app.Resources;
        Color fill = Accent(dark ? "SystemAccentColorLight2" : "SystemAccentColorDark1");
        Color text = Accent(dark ? "SystemAccentColorLight3" : "SystemAccentColorDark2");
        r["AccentFillColorDefaultBrush"] = new SolidColorBrush(fill);
        r["AccentTextFillColorPrimaryBrush"] = new SolidColorBrush(text);
        r["TextOnAccentFillColorPrimaryBrush"] = new SolidColorBrush(dark ? Colors.Black : Colors.White);
        r["AccentSubtle"] = new SolidColorBrush(Color.FromArgb(0x33, fill.R, fill.G, fill.B));
        // Auswahl in Listen dezent getönt statt voll mit Akzent – sonst stünde im dunklen Theme heller Text auf hellem Akzent
        r["SelectedRow"] = new SolidColorBrush(Color.FromArgb(0x3D, fill.R, fill.G, fill.B));
        r["SelectedRowHover"] = new SolidColorBrush(Color.FromArgb(0x52, fill.R, fill.G, fill.B));

        // Avalonias Akzentknöpfe und Häkchen wie WinUI: gedämpfter Akzent (hell dunkler, dunkel heller mit schwarzer Schrift)
        var onAccent = new SolidColorBrush(dark ? Colors.Black : Colors.White);
        var hover = new SolidColorBrush(Color.FromArgb(0xE6, fill.R, fill.G, fill.B));
        var pressed = new SolidColorBrush(Color.FromArgb(0xCC, fill.R, fill.G, fill.B));
        var fillBrush = new SolidColorBrush(fill);
        r["AccentButtonBackground"] = fillBrush;
        r["AccentButtonBackgroundPointerOver"] = hover;
        r["AccentButtonBackgroundPressed"] = pressed;
        r["AccentButtonBorderBrush"] = fillBrush;
        r["AccentButtonBorderBrushPointerOver"] = hover;
        r["AccentButtonBorderBrushPressed"] = pressed;
        r["AccentButtonForeground"] = onAccent;
        r["AccentButtonForegroundPointerOver"] = onAccent;
        r["AccentButtonForegroundPressed"] = onAccent;
        r["CheckBoxCheckBackgroundFillChecked"] = fillBrush;
        r["CheckBoxCheckBackgroundFillCheckedPointerOver"] = hover;
        r["CheckBoxCheckBackgroundFillCheckedPressed"] = pressed;
        r["CheckBoxCheckBackgroundStrokeChecked"] = fillBrush;
        r["CheckBoxCheckBackgroundStrokeCheckedPointerOver"] = hover;
        r["CheckBoxCheckBackgroundStrokeCheckedPressed"] = pressed;
        r["CheckBoxCheckGlyphForegroundChecked"] = onAccent;
        r["CheckBoxCheckGlyphForegroundCheckedPointerOver"] = onAccent;
        r["CheckBoxCheckGlyphForegroundCheckedPressed"] = onAccent;
    }

    private static Color Accent(string key)
    {
        if (_fixedAccent && FixedAccent.TryGetValue(key, out var fixedColor)) return fixedColor;
        if (_app!.TryGetResource(key, _app.ActualThemeVariant, out var value) && value is Color color) return color;
        return FixedAccent[key];
    }

    public static bool IsWindows11 => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    /// <summary>
    /// Hintergrund des Hauptfensters: unter Windows 11 Mica (gibt es in Screenshots nicht), sonst die Fläche aus dem Theme.
    /// </summary>
    public static void ApplyWindowChrome(Window w, bool mica = true)
    {
        if (mica && IsWindows11)
        {
            w.TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None];
            if (w.ActualTransparencyLevel == WindowTransparencyLevel.Mica)
            {
                w.Background = Brushes.Transparent;
                return;
            }
        }
        w.SetResource(TemplatedControl.BackgroundProperty, "ApplicationBackgroundBrush");
    }
}
