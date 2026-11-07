using System.Windows.Input;
using ClaudeHotkey.Core.Claude;
using ClaudeHotkey.Core.Config;
using ClaudeHotkey.Core.Localization;

namespace ClaudeHotkey.ViewModels;

public enum PopupState
{
    /// <summary>Text captured, waiting for the user to pick an action.</summary>
    Choosing,
    /// <summary>An action with askInput was picked; waiting for the user's instruction.</summary>
    AskingInput,
    Running,
    Result,
    Error,
}

public sealed record ActionItem(ActionDefinition Definition, string Shortcut)
{
    public string Label => Definition.Label;
    public string ToolTip => Shortcut.Length > 0 ? Lang.T($"Taste {Shortcut}", $"Key {Shortcut}") : Definition.Label;
}

/// <summary>One result in the popup: the action's own (first) or one per follow-up.</summary>
/// <param name="Label">Action label for the first version, the user's change request for the others.</param>
/// <param name="Prompt">The action prompt it was made with (with {input} filled in).</param>
public sealed record ResultVersion(string Label, string Prompt, string Text, string? Feedback);

/// <summary>A chip in the version row; clicking it shows that version.</summary>
public sealed record StepItem(int Index, string Label, bool IsCurrent);

/// <summary>State of one popup: the captured text, the action buttons and the Claude call.</summary>
public sealed class PopupViewModel : ObservableObject
{
    public delegate Task<ClaudeResult> RunClaude(ClaudeRequest request, CancellationToken cancellationToken);

    /// <summary>Like <see cref="RunClaude"/>, plus the answer so far while it streams in (on any thread).</summary>
    public delegate Task<ClaudeResult> RunClaudeStreaming(ClaudeRequest request, Action<string> onPartialText, CancellationToken cancellationToken);

    private readonly string? _sourceText;
    private readonly AppSettings _settings;
    private readonly RunClaudeStreaming _run;
    private readonly Action<string> _copy;
    private CancellationTokenSource? _running;
    private PopupState _state;
    private ActionItem? _current;
    private string _inputText = "";
    private string _resultText = "";
    private string _errorMessage = "";
    private string? _errorDetails;
    private bool _showDetails;
    private readonly List<ResultVersion> _versions = [];
    private int _versionIndex = -1;
    private string _refineText = "";
    // What "Erneut versuchen" repeats: the action or the follow-up that failed.
    private Func<Task>? _lastAttempt;

    /// <param name="sourceText">The captured text; null when capturing failed.</param>
    /// <param name="captureMessage">Why capturing failed, shown instead of the actions.</param>
    public PopupViewModel(string? sourceText, string? captureMessage, ActionConfig config, RunClaudeStreaming run, Action<string> copy)
    {
        _sourceText = sourceText;
        _settings = config.Settings;
        _run = run;
        _copy = copy;

        Actions = config.EnabledActions
            .Select((action, index) => new ActionItem(action, index < 9 ? (index + 1).ToString() : ""))
            .ToList();

        RunActionCommand = new RelayCommand(p => { if (p is ActionItem item) _ = StartAsync(item); }, _ => CanRunActions);
        SubmitInputCommand = new RelayCommand(() => { if (_current is { } item) _ = RunAsync(item, InputText); },
                                              () => IsAskingInput && InputText.Trim().Length > 0);
        CopyCommand = new RelayCommand(() => { _copy(ResultText); CloseRequested?.Invoke(this, EventArgs.Empty); }, () => HasResult);
        ReplaceCommand = new RelayCommand(() =>
        {
            ReplaceError = null;
            ReplaceRequested?.Invoke(this, ResultText);
        }, () => CanReplace);
        RetryCommand = new RelayCommand(() => { if (_lastAttempt is { } attempt) _ = attempt(); }, () => CanRetry);
        ToggleDetailsCommand = new RelayCommand(() => ShowDetails = !ShowDetails);
        RefineCommand = new RelayCommand(() => { if (CanRefine) _ = RefineAsync(RefineText.Trim()); }, () => CanRefine);
        PreviousVersionCommand = new RelayCommand(() => ShowVersion(_versionIndex - 1), () => HasResult && _versionIndex > 0);
        NextVersionCommand = new RelayCommand(() => ShowVersion(_versionIndex + 1), () => HasResult && _versionIndex < _versions.Count - 1);
        SelectVersionCommand = new RelayCommand(p => { if (p is StepItem step) ShowVersion(step.Index); }, _ => HasResult);
        BackToResultCommand = new RelayCommand(() =>
        {
            if (!CanReturnToResult)
                return;
            ShowVersion(_versionIndex);
            RefineFocusRequested?.Invoke(this, EventArgs.Empty);
        }, () => CanReturnToResult);

        if (sourceText is null)
        {
            _errorMessage = captureMessage ?? Lang.T("Kein Text übernommen.", "No text taken over.");
            _state = PopupState.Error;
        }
    }

    /// <summary>Asks the window to close (after "Kopieren").</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Asks the window to focus the input field.</summary>
    public event EventHandler? InputRequested;

    /// <summary>Asks the window to focus the follow-up field (after a follow-up, so the next one can be typed right away).</summary>
    public event EventHandler? RefineFocusRequested;

    /// <summary>After each Claude call: action id, duration, outcome and whether it was a follow-up, for the log file.</summary>
    public event EventHandler<(string ActionId, TimeSpan Duration, ClaudeErrorKind Error, bool FollowUp)>? RunCompleted;

    /// <summary>"Ersetzen" was chosen; the argument is the text to paste into the source window.</summary>
    public event EventHandler<string>? ReplaceRequested;

    private string? _replaceError;

    /// <summary>Why pasting failed; the result stays in the popup so it can still be copied.</summary>
    public string? ReplaceError
    {
        get => _replaceError;
        set
        {
            if (Set(ref _replaceError, value))
                OnPropertyChanged(nameof(HasReplaceError));
        }
    }

    public bool HasReplaceError => !string.IsNullOrEmpty(ReplaceError);

    public IReadOnlyList<ActionItem> Actions { get; }

    public bool HasSource => _sourceText is not null;

    public string SourceInfo => _sourceText is null ? "" : Lang.T($"{_sourceText.Length} Zeichen", $"{_sourceText.Length} characters");

    /// <summary>Selected text on one line, for the header.</summary>
    public string SourcePreview => _sourceText is null ? "" : string.Join(' ', _sourceText.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    public PopupState State
    {
        get => _state;
        private set
        {
            if (!Set(ref _state, value))
                return;
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsAskingInput));
            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(HasFeedback));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(CanReplace));
            OnPropertyChanged(nameof(CanRetry));
            OnPropertyChanged(nameof(CanReturnToResult));
            OnPropertyChanged(nameof(HasVersions));
            OnPropertyChanged(nameof(CanRunActions));
            OnPropertyChanged(nameof(RunningLabel));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsRunning => State == PopupState.Running;
    public bool IsAskingInput => State == PopupState.AskingInput;
    public bool HasResult => State == PopupState.Result;
    public bool HasError => State == PopupState.Error;
    public bool CanRunActions => HasSource && !IsRunning;

    /// <summary>The "Ersetzen" button is shown for actions with replace=true.</summary>
    public bool CanReplace => HasResult && _current?.Definition.Replace == true;

    public bool CanRetry => HasError && _lastAttempt is not null;

    /// <summary>After a failed follow-up the previous result is still there.</summary>
    public bool CanReturnToResult => HasError && _versionIndex >= 0;

    public bool CanRefine => HasResult && RefineText.Trim().Length > 0;

    /// <summary>The user's change request for the shown result ("in Du-Form").</summary>
    public string RefineText
    {
        get => _refineText;
        set
        {
            if (Set(ref _refineText, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>The version row is shown once there is more than one result.</summary>
    public bool HasVersions => HasResult && _versions.Count > 1;

    public IReadOnlyList<StepItem> Steps =>
        _versions.Select((version, index) => new StepItem(index, version.Label, index == _versionIndex)).ToList();

    public string VersionInfo => _versions.Count == 0 ? "" : $"{_versionIndex + 1} / {_versions.Count}";

    public string RunningLabel => _current is null ? "" : $"{_current.Label} …";

    public ActionItem? CurrentAction
    {
        get => _current;
        private set => Set(ref _current, value);
    }

    public string InputText
    {
        get => _inputText;
        set
        {
            if (Set(ref _inputText, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string ResultText
    {
        get => _resultText;
        private set => Set(ref _resultText, value);
    }

    private string _streamingText = "";

    /// <summary>The answer so far while the call runs; only shown, the final result replaces it.</summary>
    public string StreamingText
    {
        get => _streamingText;
        private set
        {
            if (Set(ref _streamingText, value))
                OnPropertyChanged(nameof(HasStreamingText));
        }
    }

    public bool HasStreamingText => StreamingText.Length > 0;

    private string? _feedbackText;

    /// <summary>Feedback of the action (e.g. language coaching); shown, never copied or pasted.</summary>
    public string? FeedbackText
    {
        get => _feedbackText;
        private set
        {
            if (Set(ref _feedbackText, value))
                OnPropertyChanged(nameof(HasFeedback));
        }
    }

    public bool HasFeedback => HasResult && !string.IsNullOrEmpty(FeedbackText);

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => Set(ref _errorMessage, value);
    }

    public string? ErrorDetails
    {
        get => _errorDetails;
        private set
        {
            if (Set(ref _errorDetails, value))
                OnPropertyChanged(nameof(HasErrorDetails));
        }
    }

    public bool HasErrorDetails => !string.IsNullOrEmpty(ErrorDetails);

    public bool ShowDetails
    {
        get => _showDetails;
        set => Set(ref _showDetails, value);
    }

    public ICommand RunActionCommand { get; }
    public ICommand SubmitInputCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand ReplaceCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand ToggleDetailsCommand { get; }
    public ICommand RefineCommand { get; }
    public ICommand PreviousVersionCommand { get; }
    public ICommand NextVersionCommand { get; }
    public ICommand SelectVersionCommand { get; }
    public ICommand BackToResultCommand { get; }

    /// <summary>Runs the action with the given shortcut (1–9). False when there is none.</summary>
    public bool TryRunShortcut(string key)
    {
        var item = Actions.FirstOrDefault(a => a.Shortcut == key);
        if (item is null || !CanRunActions)
            return false;
        _ = StartAsync(item);
        return true;
    }

    /// <summary>Starts the action with this id right away (own hotkey of an action).</summary>
    public void StartAction(string id)
    {
        if (Actions.FirstOrDefault(a => a.Definition.Id == id) is { } item && CanRunActions)
            _ = StartAsync(item);
    }

    /// <summary>The gear in the header asks for the settings dialog.</summary>
    public event EventHandler? SettingsRequested;

    public void RequestSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Stops a running call; the CLI process tree is killed.</summary>
    public void Cancel() => _running?.Cancel();

    private Task StartAsync(ActionItem item)
    {
        CurrentAction = item;
        if (item.Definition.AskInput)
        {
            State = PopupState.AskingInput;
            InputRequested?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
        return RunAsync(item, null);
    }

    /// <summary>Runs the action on the selected text; its result starts a new version history.</summary>
    private Task RunAsync(ActionItem item, string? input)
    {
        var prompt = item.Definition.BuildPrompt(input);
        _lastAttempt = () => RunAsync(item, input);
        return ExecuteAsync(item, prompt, null, result =>
        {
            _versions.Clear();
            _versions.Add(new ResultVersion(item.Label, prompt, result.Text!, result.Feedback));
        });
    }

    /// <summary>Reworks the shown version; versions after it are dropped, like undo in an editor.</summary>
    private Task RefineAsync(string instruction)
    {
        if (_current is not { } item || _versionIndex < 0)
            return Task.CompletedTask;
        var baseIndex = _versionIndex;
        var start = _versions[baseIndex];
        // The wishes on the way to the shown version, plus the new one; the action's own version has none.
        var instructions = _versions.Skip(1).Take(baseIndex).Select(v => v.Label).Append(instruction).ToList();
        _lastAttempt = () => RefineAsync(instruction);
        return ExecuteAsync(item, start.Prompt, new Refinement(start.Text, instructions), result =>
        {
            _versions.RemoveRange(baseIndex + 1, _versions.Count - baseIndex - 1);
            _versions.Add(new ResultVersion(instruction, start.Prompt, result.Text!, result.Feedback));
            RefineText = "";
        });
    }

    private void ShowVersion(int index)
    {
        if (index < 0 || index >= _versions.Count)
            return;
        _versionIndex = index;
        var version = _versions[index];
        ResultText = version.Text;
        FeedbackText = version.Feedback;
        ErrorDetails = null;
        ReplaceError = null;
        OnPropertyChanged(nameof(Steps));
        OnPropertyChanged(nameof(VersionInfo));
        OnPropertyChanged(nameof(HasVersions));
        State = PopupState.Result;
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task ExecuteAsync(ActionItem item, string prompt, Refinement? refinement, Action<ClaudeResult> addVersion)
    {
        if (_sourceText is null || IsRunning)
            return;

        CurrentAction = item;
        ShowDetails = false;
        ReplaceError = null;
        StreamingText = "";
        State = PopupState.Running;
        using var running = new CancellationTokenSource();
        _running = running;

        // Partial text arrives on a worker thread; updates that come in after the call ended are dropped.
        var context = SynchronizationContext.Current;
        void OnPartialText(string text)
        {
            void Apply() { if (_running == running) StreamingText = text; }
            if (context is null) Apply();
            else context.Post(_ => Apply(), null);
        }

        var definition = item.Definition;
        var request = new ClaudeRequest(
            prompt,
            _sourceText,
            definition.EffectiveModel(_settings),
            _settings.SystemPrompt,
            TimeSpan.FromSeconds(_settings.TimeoutSeconds),
            definition.Feedback,
            refinement);

        ClaudeResult result;
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            result = await _run(request, OnPartialText, running.Token);
        }
        catch (Exception ex)
        {
            result = new ClaudeResult(ClaudeErrorKind.Failed, null, ClaudeCli.MessageFor(ClaudeErrorKind.Failed), ex.ToString());
        }
        finally
        {
            _running = null;
            StreamingText = "";
        }
        RunCompleted?.Invoke(this, (definition.Id, started.Elapsed, result.Error, refinement is not null));

        if (result.Success)
        {
            addVersion(result);
            ShowVersion(_versions.Count - 1);
            if (refinement is not null)
                RefineFocusRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // A failed new action leaves nothing to go back to; a failed follow-up keeps the versions.
            if (refinement is null)
            {
                _versions.Clear();
                _versionIndex = -1;
            }
            ErrorMessage = result.Message ?? ClaudeCli.MessageFor(result.Error);
            ErrorDetails = result.Details;
            State = PopupState.Error;
        }
    }
}
