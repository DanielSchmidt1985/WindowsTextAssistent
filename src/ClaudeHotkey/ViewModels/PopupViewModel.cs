using System.Windows.Input;
using ClaudeHotkey.Core.Claude;
using ClaudeHotkey.Core.Config;

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
    public string ToolTip => Shortcut.Length > 0 ? $"Taste {Shortcut}" : Definition.Label;
}

/// <summary>State of one popup: the captured text, the action buttons and the Claude call.</summary>
public sealed class PopupViewModel : ObservableObject
{
    public delegate Task<ClaudeResult> RunClaude(ClaudeRequest request, CancellationToken cancellationToken);

    private readonly string? _sourceText;
    private readonly AppSettings _settings;
    private readonly RunClaude _run;
    private readonly Action<string> _copy;
    private CancellationTokenSource? _running;
    private PopupState _state;
    private ActionItem? _current;
    private string _inputText = "";
    private string _resultText = "";
    private string _errorMessage = "";
    private string? _errorDetails;
    private bool _showDetails;

    /// <param name="sourceText">The captured text; null when capturing failed.</param>
    /// <param name="captureMessage">Why capturing failed, shown instead of the actions.</param>
    public PopupViewModel(string? sourceText, string? captureMessage, ActionConfig config, RunClaude run, Action<string> copy)
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
        RetryCommand = new RelayCommand(() => { if (_current is { } item) _ = RunAsync(item, InputText); }, () => CanRetry);
        ToggleDetailsCommand = new RelayCommand(() => ShowDetails = !ShowDetails);

        if (sourceText is null)
        {
            _errorMessage = captureMessage ?? "Kein Text übernommen.";
            _state = PopupState.Error;
        }
    }

    /// <summary>Asks the window to close (after "Kopieren").</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Asks the window to focus the input field.</summary>
    public event EventHandler? InputRequested;

    public IReadOnlyList<ActionItem> Actions { get; }

    public bool HasSource => _sourceText is not null;

    public string SourceInfo => _sourceText is null ? "" : $"{_sourceText.Length} Zeichen";

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
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(CanReplace));
            OnPropertyChanged(nameof(CanRetry));
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

    public bool CanRetry => HasError && _current is not null;

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
    public ICommand RetryCommand { get; }
    public ICommand ToggleDetailsCommand { get; }

    /// <summary>Runs the action with the given shortcut (1–9). False when there is none.</summary>
    public bool TryRunShortcut(string key)
    {
        var item = Actions.FirstOrDefault(a => a.Shortcut == key);
        if (item is null || !CanRunActions)
            return false;
        _ = StartAsync(item);
        return true;
    }

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

    private async Task RunAsync(ActionItem item, string? input)
    {
        if (_sourceText is null || IsRunning)
            return;

        CurrentAction = item;
        ShowDetails = false;
        State = PopupState.Running;
        using var running = new CancellationTokenSource();
        _running = running;

        var definition = item.Definition;
        var request = new ClaudeRequest(
            definition.BuildPrompt(input),
            _sourceText,
            definition.EffectiveModel(_settings),
            _settings.SystemPrompt,
            TimeSpan.FromSeconds(_settings.TimeoutSeconds));

        ClaudeResult result;
        try
        {
            result = await _run(request, running.Token);
        }
        catch (Exception ex)
        {
            result = new ClaudeResult(ClaudeErrorKind.Failed, null, ClaudeCli.MessageFor(ClaudeErrorKind.Failed), ex.ToString());
        }
        finally
        {
            _running = null;
        }

        if (result.Success)
        {
            ResultText = result.Text!;
            ErrorDetails = null;
            State = PopupState.Result;
        }
        else
        {
            ErrorMessage = result.Message ?? ClaudeCli.MessageFor(result.Error);
            ErrorDetails = result.Details;
            State = PopupState.Error;
        }
    }
}
