using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using ClaudeHotkey.Core.Claude;
using ClaudeHotkey.Core.Config;
using ClaudeHotkey.Core.Localization;
using ClaudeHotkey.Localization;

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
    private string _mainHotkey = "";
    private string _model = "";
    private string _timeoutSeconds = "";
    private string _pasteDelayMs = "";
    private string _systemPrompt = "";
    private bool _autostart;
    private bool _autoUpdate = true;
    private string _claudePath = "";
    private string _theme = ThemeSetting.System;
    private string _language = Lang.System;
    private string _status = "";
    private bool _statusIsError;
    private readonly UiStateStore _ui;
    private string _scale = "";
    private string _popupWidth = "";
    private (string Scale, string Width) _savedUi;

    /// <param name="ui">Look and window sizes (ui.json); defaults to the file next to actions.json.</param>
    public SettingsViewModel(ActionConfigStore store, PopupViewModel.RunClaude run, UiStateStore? ui = null)
    {
        _store = store;
        _run = run;
        _ui = ui ?? new UiStateStore(store.Directory);
        if (ui is null)
            _ui.Load();
        Load(store.Current);
        LoadUi(_ui.Current);
        _savedJson = BuildJsonOrEmpty();

        NewCommand = new RelayCommand(AddNew);
        DuplicateCommand = new RelayCommand(Duplicate, () => Selected is not null);
        DeleteCommand = new RelayCommand(Delete, () => Selected is not null);
        MoveUpCommand = new RelayCommand(() => MoveSelected(-1), () => Selected is not null && Actions.IndexOf(Selected) > 0);
        MoveDownCommand = new RelayCommand(() => MoveSelected(+1), () => Selected is not null && Actions.IndexOf(Selected) < Actions.Count - 1);
        SaveCommand = new RelayCommand(() => Save());

        if (store.LoadErrors.Count > 0)
            SetStatus(Lang.T("actions.json ist fehlerhaft. Angezeigt wird die letzte gültige Konfiguration; Speichern überschreibt die Datei.",
                "actions.json is broken. Shown is the last valid configuration; saving overwrites the file."), isError: true);
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
    public string MainHotkey { get => _mainHotkey; set => Set(ref _mainHotkey, value); }
    public string Model { get => _model; set => Set(ref _model, value); }
    public string TimeoutSeconds { get => _timeoutSeconds; set => Set(ref _timeoutSeconds, value); }
    public string PasteDelayMs { get => _pasteDelayMs; set => Set(ref _pasteDelayMs, value); }
    public string SystemPrompt { get => _systemPrompt; set => Set(ref _systemPrompt, value); }
    public bool Autostart { get => _autostart; set => Set(ref _autostart, value); }
    public bool AutoUpdate { get => _autoUpdate; set => Set(ref _autoUpdate, value); }
    public string ClaudePath { get => _claudePath; set => Set(ref _claudePath, value); }

    /// <summary>settings.theme: "system", "dark" or "light".</summary>
    public string Theme { get => _theme; set => Set(ref _theme, value); }

    /// <summary>settings.language: "system", "de" or "en".</summary>
    public string Language { get => _language; set => Set(ref _language, value); }

    public IReadOnlyList<Choice> ThemeChoices { get; } =
        [new(ThemeSetting.System, Ui.LikeWindows), new(ThemeSetting.Dark, Ui.Dark), new(ThemeSetting.Light, Ui.Light)];

    // Language names stay in their own language, so they can be found in either UI language.
    public IReadOnlyList<Choice> LanguageChoices { get; } =
        [new(Lang.System, Ui.LikeWindows), new(Lang.German, "Deutsch"), new(Lang.English, "English")];

    /// <summary>Scale of text and controls in percent (ui.json).</summary>
    public string Scale { get => _scale; set => Set(ref _scale, value); }

    /// <summary>Popup width in pixels (ui.json); also changed by dragging the popup's right edge.</summary>
    public string PopupWidth { get => _popupWidth; set => Set(ref _popupWidth, value); }

    /// <summary>
    /// Ctrl+wheel or dragging the popup changed ui.json while the dialog is open: show the new
    /// values, so "Speichern" does not put the old ones back.
    /// </summary>
    public void ReloadUi(UiState state) => LoadUi(state);

    private void LoadUi(UiState state)
    {
        Scale = state.Scale.ToString(CultureInfo.InvariantCulture);
        PopupWidth = state.PopupWidth.ToString(CultureInfo.InvariantCulture);
        _savedUi = (Scale, PopupWidth);
    }

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

    public bool IsDirty => BuildJsonOrEmpty() != _savedJson || (Scale, PopupWidth) != _savedUi;

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
        var uiErrors = ValidateUi(out var scale, out var popupWidth);
        if (config is null || uiErrors.Count > 0)
        {
            ShowErrors([.. parseErrors, .. uiErrors]);
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
            SetStatus(Lang.T($"Speichern fehlgeschlagen: {ex.Message}", $"Saving failed: {ex.Message}"), isError: true);
            return false;
        }
        LoadUi(_ui.Update(s =>
        {
            s.Scale = scale;
            s.PopupWidth = popupWidth;
        }));

        ShowErrors([]);
        foreach (var action in Actions)
            action.MarkSaved();
        _savedJson = BuildJsonOrEmpty();
        SetStatus(Lang.T($"Gespeichert um {DateTime.Now:HH:mm:ss}. Die Änderungen gelten sofort.",
            $"Saved at {DateTime.Now:HH:mm:ss}. The changes apply right away."), isError: false);
        Saved?.Invoke(this, _store.Current);
        return true;
    }

    private void Load(ActionConfig config)
    {
        var s = config.Settings;
        DoubleCopyMs = s.DoubleCopyMs.ToString(CultureInfo.InvariantCulture);
        MainHotkey = s.MainHotkey;
        Model = s.Model;
        TimeoutSeconds = s.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        PasteDelayMs = s.PasteDelayMs.ToString(CultureInfo.InvariantCulture);
        SystemPrompt = s.SystemPrompt;
        Autostart = s.Autostart;
        AutoUpdate = s.AutoUpdate;
        ClaudePath = s.ClaudePath;
        Theme = NormalizeChoice(s.Theme, ThemeChoices);
        Language = NormalizeChoice(s.Language, LanguageChoices);

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
        editor.Label = Lang.T("Neue Aktion", "New action");
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
        editor.Label = Lang.T($"{Selected.Label} (Kopie)", $"{Selected.Label} (copy)");
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
            errors.Add(new ConfigError(null, field, Lang.T("Bitte eine ganze Zahl eingeben.", "Please enter a whole number."),
                Lang.T($"{label}: keine ganze Zahl.", $"{label}: not a whole number.")));
            return 0;
        }

        var settings = new AppSettings
        {
            DoubleCopyMs = Number(DoubleCopyMs, "doubleCopyMs", Lang.T("Zeitfenster", "Time window")),
            MainHotkey = MainHotkey.Trim(),
            Model = Model.Trim(),
            TimeoutSeconds = Number(TimeoutSeconds, "timeoutSeconds", "Timeout"),
            PasteDelayMs = Number(PasteDelayMs, "pasteDelayMs", Lang.T("Wartezeit beim Einfügen", "Paste delay")),
            SystemPrompt = SystemPrompt,
            Autostart = Autostart,
            AutoUpdate = AutoUpdate,
            ClaudePath = ClaudePath.Trim(),
            Theme = Theme,
            Language = Language,
        };
        var config = new ActionConfig { Settings = settings, Actions = Actions.Select(a => a.ToDefinition()).ToList() };
        return errors.Count > 0 ? (null, errors) : (config, []);
    }

    private List<ConfigError> ValidateUi(out int scale, out int popupWidth)
    {
        var errors = new List<ConfigError>();
        int Range(string text, string field, int min, int max, string label)
        {
            if (!int.TryParse(text.Trim().TrimEnd('%', ' '), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                errors.Add(new ConfigError(null, field, Lang.T("Bitte eine ganze Zahl eingeben.", "Please enter a whole number."),
                Lang.T($"{label}: keine ganze Zahl.", $"{label}: not a whole number.")));
            else if (value < min || value > max)
                errors.Add(new ConfigError(null, field, Lang.T($"Muss zwischen {min} und {max} liegen.", $"Must be between {min} and {max}."),
                    Lang.T($"{label}: zwischen {min} und {max}.", $"{label}: between {min} and {max}.")));
            return value;
        }
        scale = Range(Scale, "scale", UiState.MinScale, UiState.MaxScale, Lang.T("Skalierung", "Scale"));
        popupWidth = Range(PopupWidth, "popupWidth", UiState.MinPopupWidth, UiState.MaxPopupWidth, Lang.T("Popup-Breite", "Popup width"));
        return errors;
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
        SetStatus(errors.Count == 1
            ? Lang.T($"Nicht gespeichert: {errors[0].Message}", $"Not saved: {errors[0].Message}")
            : Lang.T($"Nicht gespeichert: {errors.Count} Fehler, siehe markierte Felder.", $"Not saved: {errors.Count} errors, see the marked fields."), isError: true);
    }

    /// <summary>The setting in lower case if it is one of the choices, otherwise "system" (also for empty).</summary>
    private static string NormalizeChoice(string? value, IReadOnlyList<Choice> choices) =>
        choices.FirstOrDefault(c => c.Value == value?.Trim().ToLowerInvariant())?.Value ?? choices[0].Value;

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private void SetStatus(string text, bool isError)
    {
        StatusIsError = isError;
        Status = text;
    }
}

/// <summary>One entry of a settings ComboBox: the value stored in actions.json and its label.</summary>
public sealed record Choice(string Value, string Label)
{
    // The themed ComboBox shows the selected item as text, without DisplayMemberPath.
    public override string ToString() => Label;
}
