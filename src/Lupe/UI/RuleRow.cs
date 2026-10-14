using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lupe.Core.Model;
using Lupe.Core.Settings;

namespace Lupe.UI;

/// <summary>Eine Link-Regel in der Tabelle der Einstellungen, mit Prüfergebnis.</summary>
public sealed class RuleRow : INotifyPropertyChanged
{
    private string _pattern = "";
    private string _prefix = "";
    private bool _enabled = true;
    private string? _error;

    public string Pattern { get => _pattern; set => Set(ref _pattern, value); }
    public string Prefix { get => _prefix; set => Set(ref _prefix, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    /// <summary>Fehlermeldung aus <see cref="LinkRules.Validate"/>, sonst null.</summary>
    public string? Error { get => _error; set => Set(ref _error, value); }

    public LinkRule ToRule() => new(Pattern.Trim(), Prefix.Trim(), Enabled);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
