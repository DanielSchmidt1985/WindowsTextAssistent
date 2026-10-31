using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using ClaudeHotkey.Core.Claude;
using ClaudeHotkey.Core.Config;

namespace ClaudeHotkey.ViewModels;

/// <summary>
/// The settings dialog. Edits a copy of the configuration; "Speichern" validates, writes
/// actions.json atomically and raises <see cref="Saved"/> so the app applies it right away.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> ModelAliases = ["haiku", "sonnet", "opus", "fable"];

    private readonly ActionConfigStore _store;
    private readonly PopupViewModel.RunClaude _run;
    private string _savedJson;
    private ActionEditorViewModel? _selected;
    private string _doubleCopyMs = "";
    private string _secondHotkey = "";
    private string _model = "";
    private string _timeoutSeconds = "";
    private string _pasteDelayMs = "";
    private string _systemPrompt = "";
    private bool _autostart;
    private string _claudePath = "";
    private string _status = "";
    private bool _statusIsError;

    public SettingsViewModel(ActionConfigStore store, PopupViewModel.RunClaude run)
    {
        _store = store;
        _run = run;
        Load(store.Current);
        _savedJson = BuildJsonOrEmpty();

        NewCommand = new RelayCommand(AddNew);
        DuplicateCommand = new RelayCommand(Duplicate, () => Selected is not null);
        DeleteCommand = new RelayCommand(Delete, () => Selected is not null);
        MoveUpCommand = new RelayCommand(() => MoveSelected(-1), () => Selected is not null && Actions.IndexOf(Selected) > 0);
        MoveDownCommand = new RelayCommand(() => MoveSelected(+1), () => Selected is not null && Actions.IndexOf(Selected) < Actions.Count - 1);
        SaveCommand = new RelayCommand(() => Save());

        if (store.LoadErrors.Count > 0)
            SetStatus("actions.json ist fehlerhaft. Angezeigt wird die letzte gültige Konfiguration; Speichern überschreibt die Datei.", isError: true);
    }

    /// <summary>Raised after a successful save with the configuration now in effect.</summary>
    public event EventHandler<ActionConfig>? Saved;

    public ObservableCollection<ActionEditorViewModel> Actions { get; } = [];

    public IReadOnlyList<string> Models => ModelAliases;

    public ActionEditorViewModel? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public FieldErrors GeneralErrors { get; } = new();

    public string DoubleCopyMs { get => _doubleCopyMs; set => Set(ref _doubleCopyMs, value); }
    public string SecondHotkey { get => _secondHotkey; set => Set(ref _secondHotkey, value); }
    public string Model { get => _model; set => Set(ref _model, value); }
    public string TimeoutSeconds { get => _timeoutSeconds; set => Set(ref _timeoutSeconds, value); }
    public string PasteDelayMs { get => _pasteDelayMs; set => Set(ref _pasteDelayMs, value); }
    public string SystemPrompt { get => _systemPrompt; set => Set(ref _systemPrompt, value); }
    public bool Autostart { get => _autostart; set => Set(ref _autostart, value); }
    public string ClaudePath { get => _claudePath; set => Set(ref _claudePath, value); }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => Set(ref _statusIsError, value);
    }

    public ICommand NewCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand SaveCommand { get; }

    public bool IsDirty => BuildJsonOrEmpty() != _savedJson;

    /// <summary>Moves an action (drag and drop). The order is the order of the popup buttons.</summary>
    public void Move(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= Actions.Count || to >= Actions.Count)
            return;
        Actions.Move(from, to);
        CommandManager.InvalidateRequerySuggested();
    }

    public void CancelTests()
    {
        foreach (var action in Actions)
            action.CancelTest();
    }

    /// <summary>Validates and writes actions.json. False when there were errors (shown at the fields).</summary>
    public bool Save()
    {
        var (config, parseErrors) = Build();
        if (config is null)
        {
            ShowErrors(parseErrors);
            return false;
        }
        try
        {
            _store.Save(config);
        }
        catch (ActionConfigException ex)
        {
            ShowErrors(ex.Details);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Speichern fehlgeschlagen: {ex.Message}", isError: true);
            return false;
        }

        ShowErrors([]);
        foreach (var action in Actions)
            action.MarkSaved();
        _savedJson = BuildJsonOrEmpty();
        SetStatus($"Gespeichert um {DateTime.Now:HH:mm:ss}. Die Änderungen gelten sofort.", isError: false);
        Saved?.Invoke(this, _store.Current);
        return true;
    }

    private void Load(ActionConfig config)
    {
        var s = config.Settings;
        DoubleCopyMs = s.DoubleCopyMs.ToString(CultureInfo.InvariantCulture);
        SecondHotkey = s.SecondHotkey;
        Model = s.Model;
        TimeoutSeconds = s.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        PasteDelayMs = s.PasteDelayMs.ToString(CultureInfo.InvariantCulture);
        SystemPrompt = s.SystemPrompt;
        Autostart = s.Autostart;
        ClaudePath = s.ClaudePath;

        Actions.Clear();
        foreach (var action in config.Actions)
            Actions.Add(CreateEditor(action, isNew: false));
        Selected = Actions.FirstOrDefault();
    }

    private ActionEditorViewModel CreateEditor(ActionDefinition definition, bool isNew) =>
        new(definition, isNew, CurrentSettingsForTests, _run, self => Actions.Where(a => a != self).Select(a => a.Id));

    /// <summary>The general settings as currently typed, so "Testen" uses the edited system prompt.</summary>
    private AppSettings CurrentSettingsForTests() => new()
    {
        Model = string.IsNullOrWhiteSpace(Model) ? "haiku" : Model.Trim(),
        SystemPrompt = SystemPrompt,
        TimeoutSeconds = int.TryParse(TimeoutSeconds, out var t) && t is >= 5 and <= 600 ? t : 60,
        ClaudePath = ClaudePath,
    };

    private void AddNew()
    {
        var editor = CreateEditor(new ActionDefinition { Label = "", Prompt = "" }, isNew: true);
        editor.Label = "Neue Aktion";
        Actions.Add(editor);
        Selected = editor;
    }

    private void Duplicate()
    {
        if (Selected is null)
            return;
        var copy = Selected.ToDefinition();
        copy.Hotkey = null; // the same hotkey twice would not validate
        var editor = CreateEditor(copy, isNew: true);
        editor.Label = $"{Selected.Label} (Kopie)";
        Actions.Insert(Actions.IndexOf(Selected) + 1, editor);
        Selected = editor;
    }

    private void Delete()
    {
        if (Selected is null)
            return;
        var index = Actions.IndexOf(Selected);
        Selected.CancelTest();
        Actions.RemoveAt(index);
        Selected = Actions.Count == 0 ? null : Actions[Math.Min(index, Actions.Count - 1)];
    }

    private void MoveSelected(int delta)
    {
        if (Selected is null)
            return;
        var index = Actions.IndexOf(Selected);
        Move(index, index + delta);
    }

    /// <summary>Builds the configuration from the form. Number fields that do not parse become field errors.</summary>
    private (ActionConfig? Config, IReadOnlyList<ConfigError> Errors) Build()
    {
        var errors = new List<ConfigError>();
        int Number(string text, string field, string label)
        {
            if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return value;
            errors.Add(new ConfigError(null, field, "Bitte eine ganze Zahl eingeben.", $"{label}: keine ganze Zahl."));
            return 0;
        }

        var settings = new AppSettings
        {
            DoubleCopyMs = Number(DoubleCopyMs, "doubleCopyMs", "Zeitfenster"),
            SecondHotkey = SecondHotkey.Trim(),
            Model = Model.Trim(),
            TimeoutSeconds = Number(TimeoutSeconds, "timeoutSeconds", "Timeout"),
            PasteDelayMs = Number(PasteDelayMs, "pasteDelayMs", "Wartezeit beim Einfügen"),
            SystemPrompt = SystemPrompt,
            Autostart = Autostart,
            ClaudePath = ClaudePath.Trim(),
        };
        var config = new ActionConfig { Settings = settings, Actions = Actions.Select(a => a.ToDefinition()).ToList() };
        return errors.Count > 0 ? (null, errors) : (config, []);
    }

    private string BuildJsonOrEmpty() => Build().Config?.ToJson() ?? "";

    private void ShowErrors(IReadOnlyList<ConfigError> errors)
    {
        GeneralErrors.Set(errors.Where(e => e.ActionIndex is null).Select(e => (e.Field, e.FieldMessage)));
        for (var i = 0; i < Actions.Count; i++)
        {
            var index = i;
            Actions[i].Errors.Set(errors.Where(e => e.ActionIndex == index).Select(e => (e.Field, Capitalize(e.FieldMessage))));
        }

        if (errors.Count == 0)
            return;
        var firstBroken = errors.FirstOrDefault(e => e.ActionIndex is not null)?.ActionIndex;
        if (firstBroken is { } brokenIndex && Selected?.Errors.Any != true)
            Selected = Actions[brokenIndex];
        SetStatus(errors.Count == 1 ? $"Nicht gespeichert: {errors[0].Message}" : $"Nicht gespeichert: {errors.Count} Fehler, siehe markierte Felder.", isError: true);
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private void SetStatus(string text, bool isError)
    {
        StatusIsError = isError;
        Status = text;
    }
}
