using System.Windows.Input;
using ClaudeHotkey.Core.Claude;
using ClaudeHotkey.Core.Config;
using ClaudeHotkey.Core.Localization;

namespace ClaudeHotkey.ViewModels;

/// <summary>One action in the settings dialog: the form fields plus the "Testen" area.</summary>
public sealed class ActionEditorViewModel : ObservableObject
{
    private readonly Func<AppSettings> _currentSettings;
    private readonly PopupViewModel.RunClaude _run;
    private readonly Func<ActionEditorViewModel, IEnumerable<string>> _otherIds;
    private string _id;
    private string _label;
    private string _prompt;
    private string _model;
    private bool _replace;
    private bool _askInput;
    private string _hotkey;
    private string _feedback;
    private string _testFeedback = "";
    private bool _enabled;
    private bool _isNew;
    private string _testText = Lang.T("Das ist ein kurtzer Beispieltext, mit dem sich die Aktion ausprobieren lässt.",
        "This is a short sample text with a mistak, to try the action.");
    private string _testInput = "";
    private string _testResult = "";
    private string _testError = "";
    private bool _isTesting;
    private CancellationTokenSource? _testing;

    /// <param name="isNew">New actions take their id from the label until they are saved once.</param>
    /// <param name="otherIds">Ids of all other actions, for making a generated id unique.</param>
    public ActionEditorViewModel(
        ActionDefinition source, bool isNew, Func<AppSettings> currentSettings, PopupViewModel.RunClaude run,
        Func<ActionEditorViewModel, IEnumerable<string>> otherIds)
    {
        _currentSettings = currentSettings;
        _run = run;
        _otherIds = otherIds;
        _id = source.Id;
        _label = source.Label;
        _prompt = source.Prompt;
        _model = source.Model ?? "";
        _replace = source.Replace;
        _askInput = source.AskInput;
        _hotkey = source.Hotkey ?? "";
        _feedback = source.Feedback ?? "";
        _enabled = source.Enabled;
        _isNew = isNew;

        TestCommand = new RelayCommand(() => _ = TestAsync(), () => !IsTesting && Prompt.Trim().Length > 0 && TestText.Length > 0);
        CancelTestCommand = new RelayCommand(() => _testing?.Cancel(), () => IsTesting);
    }

    public FieldErrors Errors { get; } = new();

    public string Id
    {
        get => _id;
        private set => Set(ref _id, value);
    }

    public string Label
    {
        get => _label;
        set
        {
            if (!Set(ref _label, value))
                return;
            if (_isNew)
                Id = ActionIds.FromLabel(value, _otherIds(this));
            OnPropertyChanged(nameof(DisplayLabel));
        }
    }

    public string Prompt
    {
        get => _prompt;
        set
        {
            if (Set(ref _prompt, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Empty means the default model from "Allgemein".</summary>
    public string Model
    {
        get => _model;
        set => Set(ref _model, value);
    }

    public bool Replace
    {
        get => _replace;
        set => Set(ref _replace, value);
    }

    public bool AskInput
    {
        get => _askInput;
        set => Set(ref _askInput, value);
    }

    public string Hotkey
    {
        get => _hotkey;
        set => Set(ref _hotkey, value);
    }

    /// <summary>Optional instruction for feedback besides the result; empty = none.</summary>
    public string Feedback
    {
        get => _feedback;
        set => Set(ref _feedback, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (Set(ref _enabled, value))
                OnPropertyChanged(nameof(DisplayLabel));
        }
    }

    /// <summary>Text in the list: label, marked when inactive.</summary>
    public string DisplayLabel => (string.IsNullOrWhiteSpace(Label) ? Lang.T("(ohne Namen)", "(no name)") : Label)
        + (Enabled ? "" : Lang.T("  (inaktiv)", "  (inactive)"));

    /// <summary>Called after saving: from now on the id stays as it is.</summary>
    public void MarkSaved() => _isNew = false;

    public ActionDefinition ToDefinition() => new()
    {
        Id = Id,
        Label = Label.Trim(),
        Prompt = Prompt,
        Model = string.IsNullOrWhiteSpace(Model) ? null : Model.Trim(),
        Replace = Replace,
        AskInput = AskInput,
        Hotkey = string.IsNullOrWhiteSpace(Hotkey) ? null : Hotkey.Trim(),
        Feedback = string.IsNullOrWhiteSpace(Feedback) ? null : Feedback.Trim(),
        Enabled = Enabled,
    };

    // "Testen": runs the action as currently edited (not yet saved) on a sample text.

    public string TestText
    {
        get => _testText;
        set
        {
            if (Set(ref _testText, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Stands in for the popup's input field when askInput is on.</summary>
    public string TestInput
    {
        get => _testInput;
        set => Set(ref _testInput, value);
    }

    public string TestResult
    {
        get => _testResult;
        private set => Set(ref _testResult, value);
    }

    public string TestFeedback
    {
        get => _testFeedback;
        private set => Set(ref _testFeedback, value);
    }

    public string TestError
    {
        get => _testError;
        private set => Set(ref _testError, value);
    }

    public bool IsTesting
    {
        get => _isTesting;
        private set
        {
            if (Set(ref _isTesting, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public ICommand TestCommand { get; }
    public ICommand CancelTestCommand { get; }

    public void CancelTest() => _testing?.Cancel();

    private async Task TestAsync()
    {
        var settings = _currentSettings();
        var definition = ToDefinition();
        var request = new ClaudeRequest(
            definition.BuildPrompt(TestInput),
            TestText,
            definition.EffectiveModel(settings),
            settings.SystemPrompt,
            TimeSpan.FromSeconds(settings.TimeoutSeconds),
            definition.Feedback);

        TestResult = "";
        TestFeedback = "";
        TestError = "";
        IsTesting = true;
        using var testing = new CancellationTokenSource();
        _testing = testing;
        try
        {
            var result = await _run(request, testing.Token);
            if (result.Success)
            {
                TestResult = result.Text!;
                TestFeedback = result.Feedback ?? "";
            }
            else
                TestError = result.Message ?? ClaudeCli.MessageFor(result.Error);
        }
        catch (Exception ex)
        {
            TestError = ex.Message;
        }
        finally
        {
            _testing = null;
            IsTesting = false;
        }
    }
}
