using System.Diagnostics;
using System.IO;
using System.Windows;
using ClaudeHotkey.Core.Claude;
using ClaudeHotkey.Core.Clipboard;
using ClaudeHotkey.Core.Config;
using ClaudeHotkey.Core.Hotkeys;
using ClaudeHotkey.Core.Input;
using ClaudeHotkey.Core.Logging;
using ClaudeHotkey.Core.Startup;
using ClaudeHotkey.Core.Windows;
using ClaudeHotkey.Themes;
using ClaudeHotkey.ViewModels;
using ClaudeHotkey.Views;

namespace ClaudeHotkey;

public partial class App : Application
{
    private readonly ClipboardService _clipboard = new();
    private readonly ActionConfigStore _config = new(ActionConfigStore.DefaultDirectory, DefaultConfig.Json);
    private readonly AppLog _log = new(Path.Combine(ActionConfigStore.DefaultDirectory, AppLog.FileName));
    private UiStateStore _ui = new(ActionConfigStore.DefaultDirectory);
    private SettingsViewModel? _settingsViewModel;
    private SingleInstance? _instance;
    private TrayIcon? _tray;
    private KeyboardHook? _hook;
    private GlobalHotkeys? _hotkeys;
    private TextCapture? _capture;
    private PopupWindow? _popup;
    private SettingsWindow? _settings;
    private bool _capturing;

    // Pieces of the tray status; see UpdateTrayStatus.
    private string _claudeStatus = "Claude wird geprüft …";
    private bool _claudeWarning;
    private string _hotkeyStatus = "";
    private bool _hotkeyWarning;
    private bool _configWarning;

    /// <summary>Registered gestures: null for the second hotkey, otherwise the id of the action it runs.</summary>
    private readonly Dictionary<HotkeyGesture, string?> _hotkeyTargets = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args is ["--demo", var demoState, ..])
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            // Own ui.json in %TEMP%, so trying Ctrl+wheel in a demo leaves the real one alone.
            var demoUi = Path.Combine(Path.GetTempPath(), "ClaudeHotkey-demo-ui");
            if (Directory.Exists(demoUi))
                Directory.Delete(demoUi, recursive: true);
            _ui = new UiStateStore(demoUi);
            if (e.Args is [_, _, var scaleArg, ..] && int.TryParse(scaleArg, out var demoScale))
                _ui.Update(s => s.Scale = demoScale);
            UiScale.Apply(_ui.Current.Scale);
            ShowDemo(demoState);
            return;
        }

        // A second start opens the settings of the running instance instead; "--exit" quits it.
        var exitRequested = e.Args.Contains("--exit");
        _instance = SingleInstance.Acquire(
            () => Dispatcher.BeginInvoke(OpenSettings),
            () => Dispatcher.BeginInvoke(() => Shutdown()),
            exitRequested ? SingleInstance.Request.Exit : SingleInstance.Request.Activate);
        if (!_instance.IsFirst || exitRequested)
        {
            Shutdown();
            return;
        }
        _log.Write("start");
        UiScale.Apply(_ui.Load().Scale);

        _tray = new TrayIcon();
        _tray.SettingsRequested += (_, _) => OpenSettings();
        _tray.OpenLogRequested += (_, _) => OpenLogFile();
        _tray.ExitRequested += (_, _) => Shutdown();

        var themeProblems = ThemeOverrides.Apply(Resources, _config.Directory);
        if (themeProblems.Count > 0)
            _tray.Notify("theme.json fehlerhaft", string.Join(Environment.NewLine, themeProblems), warning: true);

        ReloadConfig();
        var settings = _config.Current.Settings;
        _capture = new TextCapture(new SystemClipboard(_clipboard), new KeyboardInput());

        _hook = new KeyboardHook(settings.DoubleCopyMs);
        _hook.DoublePress += (_, args) => OnHotkey(args.Gesture, args.ClipboardSequence);
        _hotkeys = new GlobalHotkeys();
        _hotkeys.Pressed += (_, gesture) => OnHotkey(gesture, ClipboardService.SequenceNumber);
        RegisterHotkeys(notifyWhenTaken: true);

        // While a hotkey field in the settings has focus, the combination must reach the field.
        HotkeyTextBox.CaptureStarted += (_, _) => _hotkeys.UnregisterAll();
        HotkeyTextBox.CaptureEnded += (_, _) => RegisterHotkeys(notifyWhenTaken: false);

        UpdateTrayStatus();
        _ = RunPreflightAsync(settings);
    }

    private async Task RunPreflightAsync(AppSettings settings)
    {
        var preflight = await new ClaudeCli(settings.ClaudePath).PreflightAsync(settings.Model);
        _log.Write("preflight", code: preflight.Success ? (preflight.UsesSubscription ? "Subscription" : "ApiKey") : preflight.Error.ToString());
        _claudeStatus = preflight.Message;
        _claudeWarning = !preflight.Success || !preflight.UsesSubscription;
        UpdateTrayStatus();
        if (_claudeWarning)
            _tray?.Notify("Claude", preflight.Message, warning: true);
    }

    /// <summary>Tooltip and menu header: Claude state first, then hotkeys; red dot on any warning.</summary>
    private void UpdateTrayStatus()
    {
        var parts = new List<string> { _claudeStatus };
        if (_configWarning)
            parts.Add("actions.json fehlerhaft");
        if (_hotkeyStatus.Length > 0)
            parts.Add(_hotkeyStatus);
        _tray?.SetStatus(string.Join(" · ", parts), _claudeWarning || _hotkeyWarning || _configWarning);
    }

    /// <summary>
    /// Main hotkey or an action's hotkey was pressed. With Ctrl+C+C and Ctrl+X+X the app has
    /// already copied (cut) the text; for any other combination the tool copies itself.
    /// </summary>
    /// <param name="clipboardSequence">Clipboard sequence number at the (second) press.</param>
    private void OnHotkey(HotkeyGesture gesture, uint clipboardSequence)
    {
        if (_capture is null || !_hotkeyTargets.TryGetValue(gesture, out var actionId))
            return;
        Capture(actionId is null ? "main-hotkey" : "action-hotkey",
            gesture.AppCopiesItself
                ? () => _capture.AfterUserCopyAsync(clipboardSequence)
                : () => _capture.BySendingCopyAsync(gesture.VirtualKey),
            actionId);
    }

    /// <summary>
    /// (Re)registers the main hotkey and the hotkeys of all active actions from the current
    /// config: single presses with RegisterHotKey, double presses in the keyboard hook.
    /// </summary>
    private void RegisterHotkeys(bool notifyWhenTaken)
    {
        if (_hotkeys is null)
            return;
        _hotkeys.UnregisterAll();
        _hotkeyTargets.Clear();

        var config = _config.Current;
        var wanted = new List<(string Text, string? ActionId, string Name)> { (config.Settings.MainHotkey, null, "Haupt-Hotkey") };
        wanted.AddRange(config.EnabledActions
            .Where(a => !string.IsNullOrWhiteSpace(a.Hotkey))
            .Select(a => (a.Hotkey!, (string?)a.Id, $"„{a.Label}“")));

        var taken = new List<string>();
        var doublePresses = new List<HotkeyGesture>();
        foreach (var (text, actionId, name) in wanted)
        {
            if (!HotkeyGesture.TryParse(text, out var gesture))
                continue; // validation already rejected it; nothing to register
            if (gesture.IsDoublePress)
            {
                // Only our hook sees these, so they cannot be taken by another program.
                doublePresses.Add(gesture);
                _hotkeyTargets[gesture] = actionId;
            }
            else if (_hotkeys.TryRegister(gesture))
                _hotkeyTargets[gesture] = actionId;
            else
                taken.Add($"{gesture} ({name})");
        }
        if (_hook is not null)
            _hook.Gestures = doublePresses;

        _hotkeyWarning = taken.Count > 0;
        _hotkeyStatus = _hotkeyWarning ? $"belegt: {string.Join(", ", taken)}" : "";
        UpdateTrayStatus();
        if (_hotkeyWarning && notifyWhenTaken)
        {
            _log.Write("hotkey", code: "Taken");
            _tray?.Notify("Hotkey belegt",
                $"Von einem anderen Programm belegt: {string.Join(", ", taken)}. In den Einstellungen eine andere Kombination wählen.",
                warning: true);
        }
    }

    /// <summary>Puts a newly saved or reloaded configuration into effect without a restart.</summary>
    private void ApplyConfig(bool notify)
    {
        if (_hook is not null)
            _hook.DoublePressMilliseconds = _config.Current.Settings.DoubleCopyMs;
        RegisterHotkeys(notify);
    }

    /// <summary>Reads actions.json again if it changed; a broken file keeps the last valid one.</summary>
    private void ReloadConfig()
    {
        var before = _config.Current;
        var ok = _config.Reload();
        if (!ReferenceEquals(before, _config.Current) && _hotkeys is not null)
            ApplyConfig(notify: true); // edited by hand: hotkeys and timing follow without a restart

        var wasBroken = _configWarning;
        _configWarning = !ok;
        UpdateTrayStatus();
        if (!ok && !wasBroken)
        {
            _log.Write("config", code: "Invalid");
            _tray?.Notify("actions.json fehlerhaft",
                $"Die letzte gültige Konfiguration bleibt aktiv.{Environment.NewLine}{string.Join(Environment.NewLine, _config.LoadErrors.Take(3))}",
                warning: true);
        }
    }

    private void OpenSettings()
    {
        if (_settings is not null)
        {
            _settings.Activate();
            return;
        }
        var viewModel = new SettingsViewModel(_config,
            (request, token) => new ClaudeCli(_config.Current.Settings.ClaudePath).RunAsync(request, token), _ui);
        viewModel.Saved += (_, config) =>
        {
            _log.Write("settings", code: "Saved");
            UiScale.Apply(_ui.Current.Scale);
            ApplyAutostart(config.Settings.Autostart);
            ApplyConfig(notify: true);
            ReloadConfig(); // clears a "fehlerhaft" state: the file is valid again
        };
        var window = new SettingsWindow(viewModel, _ui.Current.SettingsWindow, StepScale, _ui.Current.Scale);
        window.Closing += (_, e) => { if (!e.Cancel) _ui.Update(s => s.SettingsWindow = window.CurrentBounds); };
        window.Closed += (_, _) => { _settings = null; _settingsViewModel = null; };
        _settings = window;
        _settingsViewModel = viewModel;
        window.Show();
        window.Activate();
    }

    /// <summary>Ctrl+Plus/Minus/0 or Ctrl+wheel in any window: +1, -1 or 0 (= 100 %).</summary>
    private void StepScale(int direction)
    {
        var state = _ui.Update(s => s.Scale = UiState.StepScale(s.Scale, direction));
        UiScale.Apply(state.Scale);
        _settingsViewModel?.ReloadUi(state);
    }

    private void PopupWidthChanged(int width)
    {
        var state = _ui.Update(s => s.PopupWidth = width);
        _settingsViewModel?.ReloadUi(state);
    }

    private PopupWindow CreatePopup(PopupViewModel viewModel, (int X, int Y) anchor) =>
        new(viewModel, anchor, _ui.Current.PopupWidth, () => _ui.Current.ScaleFactor, StepScale, PopupWidthChanged);

    /// <summary>
    /// Only on "Speichern", not at every start: a test build started from bin\Debug must not
    /// redirect the autostart entry of the installed copy.
    /// </summary>
    private void ApplyAutostart(bool enabled)
    {
        if (Environment.ProcessPath is not { } exe)
            return;
        try
        {
            if (new Autostart(new CurrentUserRunKey()).Apply(enabled, exe))
                _log.Write("autostart", code: enabled ? "On" : "Off");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _tray?.Notify("Autostart", $"Der Autostart ließ sich nicht ändern: {ex.Message}", warning: true);
        }
    }

    private void OpenLogFile()
    {
        if (!File.Exists(_log.FilePath))
            _log.Write("log", code: "Opened");
        Process.Start(new ProcessStartInfo(_log.FilePath) { UseShellExecute = true });
    }

    /// <param name="actionId">Set for an action's own hotkey: the popup runs that action right away.</param>
    private async void Capture(string trigger, Func<Task<CaptureResult>> capture, string? actionId)
    {
        if (_capturing)
            return;
        var source = ForegroundWindow.Current; // "Ersetzen" pastes back into this window
        if (ForegroundWindow.BelongsToThisProcess(source))
            return; // copying inside the popup or the settings must not open another popup
        _capturing = true;
        try
        {
            var anchor = ForegroundWindow.CursorPosition();
            var result = await capture();
            _log.Write("capture", actionId ?? trigger, code: result.Success ? "Ok" : "NoText");
            ReloadConfig();
            ShowPopup(result, anchor, source, actionId);
        }
        catch (Exception ex)
        {
            _log.Write("capture", actionId ?? trigger, code: ex.GetType().Name);
        }
        finally
        {
            _capturing = false;
        }
    }

    private void ShowPopup(CaptureResult capture, (int X, int Y) anchor, IntPtr source, string? autoRunActionId = null)
    {
        _popup?.Close(); // a new trigger replaces the open popup and cancels its call

        var config = _config.Current;
        var cli = new ClaudeCli(config.Settings.ClaudePath);
        var viewModel = new PopupViewModel(
            capture.Text,
            capture.Message,
            config,
            (request, token) => cli.RunAsync(request, token),
            text => _clipboard.SetText(text));
        viewModel.RunCompleted += (_, run) => _log.Write("claude", run.ActionId, run.Duration, run.Error.ToString());

        var popup = CreatePopup(viewModel, anchor);
        var closed = false;
        popup.Closed += (_, _) => { closed = true; if (_popup == popup) _popup = null; };
        viewModel.ReplaceRequested += async (_, text) =>
        {
            // Out of the way first, so the source window can take the focus back.
            popup.Hide();
            var replacer = new TextReplacer(new SystemClipboard(_clipboard), new KeyboardInput(), new SystemWindowActivator());
            var result = await replacer.ReplaceAsync(source, text, TimeSpan.FromMilliseconds(config.Settings.PasteDelayMs));
            _log.Write("replace", viewModel.CurrentAction?.Definition.Id, code: result.Success ? "Ok" : ReplaceCode(result.Message));
            if (closed)
                return; // replaced by a newer popup meanwhile
            if (result.Success)
            {
                popup.Close();
                return;
            }
            viewModel.ReplaceError = result.Message;
            popup.Show();
        };
        viewModel.SettingsRequested += (_, _) => OpenSettings();
        if (autoRunActionId is not null)
            popup.ContentRendered += (_, _) => viewModel.StartAction(autoRunActionId);
        _popup = popup;
        popup.Show();
    }

    private static string ReplaceCode(string? message) => message switch
    {
        TextReplacer.WindowGoneMessage => "WindowGone",
        TextReplacer.ActivateFailedMessage => "ActivateFailed",
        TextReplacer.KeysHeldMessage => "KeysHeld",
        _ => "Failed",
    };

    /// <summary>
    /// Opens the popup in one state with sample text and a simulated Claude answer, without hooks
    /// or CLI calls: <c>ClaudeHotkey.exe --demo choose|input|running|result|error|nocapture|replacefail</c>.
    /// For checking the design against the reference screenshot.
    /// </summary>
    private void ShowDemo(string state)
    {
        ThemeOverrides.Apply(Resources, _config.Directory);
        if (state.StartsWith("settings", StringComparison.Ordinal))
        {
            // Works on a fresh copy in %TEMP%, never on the real actions.json.
            var demoDirectory = Path.Combine(Path.GetTempPath(), "ClaudeHotkey-demo");
            if (Directory.Exists(demoDirectory))
                Directory.Delete(demoDirectory, recursive: true);
            var demoStore = new ActionConfigStore(demoDirectory, DefaultConfig.Json);
            demoStore.Reload();
            PopupViewModel.RunClaude fake = async (request, token) =>
            {
                await Task.Delay(400, token);
                return new ClaudeResult(ClaudeErrorKind.None, "Das ist ein kurzer Beispieltext, mit dem sich die Aktion ausprobieren lässt.", null, null);
            };
            var settingsViewModel = new SettingsViewModel(demoStore, fake, _ui);
            var settingsWindow = new SettingsWindow(settingsViewModel, null, StepScale, _ui.Current.Scale);
            MainWindow = settingsWindow;
            settingsWindow.Show();
            switch (state)
            {
                case "settings-general":
                    settingsWindow.SelectTab(1);
                    break;
                case "settings-errors":
                    // A broken new action plus a duplicate hotkey, then "Speichern".
                    settingsViewModel.NewCommand.Execute(null);
                    settingsViewModel.Selected!.Hotkey = "ctrl+c+c";
                    settingsViewModel.Save();
                    break;
                case "settings-test":
                    settingsViewModel.Selected = settingsViewModel.Actions[2];
                    settingsViewModel.Selected.TestCommand.Execute(null);
                    break;
            }
            return;
        }

        const string sample = "Hallo zusammen, anbei die Unterlagen für das Meeting morgen. Bitte schaut sie euch vorher kurtz an und gebt mir bescheid, falls etwas fehlt.";
        var config = ActionConfig.Parse(DefaultConfig.Json());

        PopupViewModel.RunClaude run = state switch
        {
            "running" => async (_, token) => { await Task.Delay(Timeout.Infinite, token); return null!; },
            "feedback" => async (_, _) =>
            {
                await Task.Delay(300);
                return new ClaudeResult(ClaudeErrorKind.None,
                    "I would like to schedule a meeting with you tomorrow.", null, null,
                    "• „make a meeting“ → „schedule a meeting“: Termine werden „scheduled“ oder „arranged“.\n" +
                    "• „I want make“ → „I would like to“: nach „want“ steht „to“, „would like“ klingt höflicher.\n" +
                    "• „tomorow“ → „tomorrow“: zwei r, ein w.");
            },
            "error" => (_, _) => Task.FromResult(new ClaudeResult(ClaudeErrorKind.NotLoggedIn, null,
                ClaudeCli.MessageFor(ClaudeErrorKind.NotLoggedIn),
                "Exit-Code: 1\n\nstdout:\n{\"is_error\":true,\"result\":\"Failed to authenticate: OAuth session expired\"}")),
            _ => async (_, _) =>
            {
                await Task.Delay(300);
                return new ClaudeResult(ClaudeErrorKind.None,
                    "Hallo zusammen,\n\nanbei die Unterlagen für das Meeting morgen. Bitte schaut sie euch vorher kurz an und gebt mir Bescheid, falls etwas fehlt.",
                    null, null);
            },
        };

        var viewModel = state == "nocapture"
            ? new PopupViewModel(null, TextCapture.NoTextMessage, config, run, _ => { })
            : new PopupViewModel(sample, null, config, run, _ => { });
        var area = SystemParameters.WorkArea;
        var popup = CreatePopup(viewModel, ((int)(area.Width / 2 - 240), (int)(area.Height / 2 - 200)));
        MainWindow = popup;
        // Start the action only after the popup is on screen, as in real use. Starting it before
        // Loaded hid the busy-bar crash: the bar was visible from the start there.
        popup.ContentRendered += (_, _) =>
        {
            switch (state)
            {
                case "input": viewModel.TryRunShortcut("8"); break;
                case "running" or "result" or "error": viewModel.TryRunShortcut("2"); break;
                case "feedback": viewModel.TryRunShortcut("3"); break;
                case "replacefail":
                    viewModel.TryRunShortcut("2");
                    viewModel.ReplaceError = TextReplacer.ActivateFailedMessage;
                    break;
            }
        };
        popup.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _popup?.Close();
        _settings?.Close();
        _hook?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        if (_instance?.IsFirst == true)
            _log.Write("exit");
        _instance?.Dispose();
        base.OnExit(e);
    }
}
