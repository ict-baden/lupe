using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Lupe.Services;

namespace Lupe.UI;

/// <summary>Eine Einstellungszeile im Windows-11-Stil: Symbol, Titel, Beschreibung, Steuerelement rechts (wie Vitrine).</summary>
public class SettingCard : ContentControl
{
    public static readonly StyledProperty<string> HeaderProperty = AvaloniaProperty.Register<SettingCard, string>(nameof(Header), "");
    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<SettingCard, string?>(nameof(Description));
    public static readonly StyledProperty<string?> GlyphProperty = AvaloniaProperty.Register<SettingCard, string?>(nameof(Glyph));

    public string Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string? Glyph { get => GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }

    protected override Type StyleKeyOverride => typeof(SettingCard);
}

/// <summary>Eintrag der Navigation links: Symbol, Text, Abzeichen (z. B. Zahl der Verlaufseinträge; leer = keins).</summary>
public class NavButton : RadioButton
{
    public static readonly StyledProperty<string?> GlyphProperty = AvaloniaProperty.Register<NavButton, string?>(nameof(Glyph));
    public static readonly StyledProperty<string?> BadgeProperty = AvaloniaProperty.Register<NavButton, string?>(nameof(Badge));

    public string? Glyph { get => GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string? Badge { get => GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }

    private bool _warning;

    /// <summary>Abzeichen als Warnung (gelb) statt neutral.</summary>
    public bool BadgeWarning
    {
        get => _warning;
        set
        {
            _warning = value;
            PseudoClasses.Set(":warning", value);
        }
    }

    protected override Type StyleKeyOverride => typeof(NavButton);
}

public static class Converters
{
    /// <summary>Inhalt eines Knopfs als Text (für Link-Knöpfe).</summary>
    public static readonly IValueConverter ToText = new FuncValueConverter<object?, string>(o => o?.ToString() ?? "");
}

/// <summary>
/// Befehle der Ergebniszeilen (Vorlage „CodeRow“ in App.axaml). Parameter ist das <see cref="CodeItem"/>. Was aus einer
/// Ergebniszeile kopiert oder geöffnet wird, vermerkt der Verlaufseintrag des Treffers.
/// </summary>
public static class CodeCommands
{
    /// <summary>Der Zustand der laufenden Oberfläche (je Prozess einer; Screenshots legen ihren eigenen an).</summary>
    internal static AppState? State { get; set; }

    public static ICommand Copy { get; } = new Command(p => p is CodeItem, p =>
    {
        if (State is { } state && p is CodeItem item) _ = CodeActions.CopyAsync(state, item);
    });

    public static ICommand OpenLink { get; } = new Command(p => p is CodeItem { HasLink: true }, p =>
    {
        if (State is { } state && p is CodeItem { Link: not null } item) CodeActions.Open(state, item);
    });

    private sealed class Command(Func<object?, bool> canExecute, Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => canExecute(parameter);
        public void Execute(object? parameter) => execute(parameter);
    }
}
