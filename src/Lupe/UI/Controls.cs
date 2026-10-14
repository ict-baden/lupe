using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Lupe.UI;

/// <summary>Eine Einstellungszeile im Windows-11-Stil: Symbol, Titel, Beschreibung, Steuerelement rechts (wie Vitrine).</summary>
public class SettingCard : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingCard), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingCard), new PropertyMetadata(null));
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingCard), new PropertyMetadata(null));

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string? Description { get => (string?)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
}

/// <summary>Abzeichen auf der Navigation (z. B. Zahl der Verlaufseinträge). Leer = keins.</summary>
public static class Nav
{
    public static readonly DependencyProperty BadgeProperty =
        DependencyProperty.RegisterAttached("Badge", typeof(string), typeof(Nav), new PropertyMetadata(null));
    /// <summary>Farbe des Abzeichens: "Warnung" oder leer (neutral).</summary>
    public static readonly DependencyProperty BadgeToneProperty =
        DependencyProperty.RegisterAttached("BadgeTone", typeof(string), typeof(Nav), new PropertyMetadata(""));

    public static string? GetBadge(DependencyObject o) => (string?)o.GetValue(BadgeProperty);
    public static void SetBadge(DependencyObject o, string? value) => o.SetValue(BadgeProperty, value);
    public static string GetBadgeTone(DependencyObject o) => (string)o.GetValue(BadgeToneProperty);
    public static void SetBadgeTone(DependencyObject o, string value) => o.SetValue(BadgeToneProperty, value);
}

/// <summary>
/// Leerer Text → sichtbar (für Platzhalter über Eingabefeldern), sonst eingeklappt; mit ConverterParameter
/// "invert" umgekehrt.
/// </summary>
public sealed class EmptyToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ^ (parameter as string == "invert") ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>true → sichtbar, false → eingeklappt; mit ConverterParameter "invert" umgekehrt.</summary>
public sealed class BoolToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is true) ^ (parameter as string == "invert") ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// Befehle der Ergebniszeilen (Vorlage „CodeRow“ in Controls.xaml). Parameter ist das <see cref="Services.CodeItem"/>;
/// jede Seite und jedes Fenster bindet sie selbst (CommandBindings).
/// </summary>
public static class CodeCommands
{
    public static readonly RoutedUICommand Copy = new("Kopieren", nameof(Copy), typeof(CodeCommands));
    public static readonly RoutedUICommand OpenLink = new("Link öffnen", nameof(OpenLink), typeof(CodeCommands));
}
