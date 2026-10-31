using System.ComponentModel;

namespace ClaudeHotkey.ViewModels;

/// <summary>
/// Error text per field, bindable as <c>{Binding Errors[prompt]}</c>. Missing keys return null
/// (a Dictionary would throw and break the binding).
/// </summary>
public sealed class FieldErrors : INotifyPropertyChanged
{
    private readonly Dictionary<string, string> _errors = new(StringComparer.OrdinalIgnoreCase);

    public event PropertyChangedEventHandler? PropertyChanged;

    public string? this[string field] => _errors.TryGetValue(field, out var message) ? message : null;

    public bool Any => _errors.Count > 0;

    public void Set(IEnumerable<(string Field, string Message)> errors)
    {
        _errors.Clear();
        foreach (var (field, message) in errors)
            _errors[field] = _errors.TryGetValue(field, out var existing) ? $"{existing} {message}" : message;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Any)));
    }

    public void Clear() => Set([]);
}
