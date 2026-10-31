using System.IO;
using System.Windows;
using ClaudeHotkey.Core.Claude;
using ClaudeHotkey.Core.Clipboard;
using ClaudeHotkey.Core.Config;
using ClaudeHotkey.Core.Hotkeys;
using ClaudeHotkey.Core.Input;
using ClaudeHotkey.Core.Windows;
using ClaudeHotkey.Themes;
using ClaudeHotkey.ViewModels;
using ClaudeHotkey.Views;

namespace ClaudeHotkey;

public partial class App : Application
{
    private readonly ClipboardService _clipboard = new();
    private readonly ActionConfigStore _config = new(ActionConfigStore.DefaultDirectory, DefaultConfig.Json);
    private KeyboardHook? _hook;
    private GlobalHotkeys? _hotkeys;
    private TextCapture? _capture;
    private CaptureLogWindow? _log;
    private PopupWindow? _popup;
    private SettingsWindow? _settings;
    private bool _capturing;

    /// <summary>Registered gestures: null for the second hotkey, otherwise the id of the action it runs.</summary>
    private readonly Dictionary<HotkeyGesture, string?> _hotkeyTargets = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args is ["--demo", var demoState, ..])
        {
            ShowDemo(demoState);
            return;
        }

        // Until the tray icon (phase 6) exists, this window shows status; closing it ends the tool.
        _log = new CaptureLogWindow();
        _log.SettingsRequested += (_, _) => OpenSettings();
        MainWindow = _log;
        _log.Show();

        foreach (var problem in ThemeOverrides.Apply(Resources, _config.Directory))
            _log.Add(new CaptureLogWindow.Entry("Theme", problem, ""));

        ReloadConfig();
        var settings = _config.Current.Settings;
        _capture = new TextCapture(new SystemClipboard(_clipboard), new KeyboardInput());

        _hook = new KeyboardHook(settings.DoubleCopyMs);
        _hook.DoubleCopy += (_, args) => Capture("Strg+C+C", () => _capture.AfterUserCopyAsync(args.ClipboardSequence), null);

        _hotkeys = new GlobalHotkeys();
        _hotkeys.Pressed += (_, gesture) =>
        {
            if (_hotkeyTargets.TryGetValue(gesture, out var actionId))
                Capture(gesture.ToString(), () => _capture.BySendingCopyAsync(gesture.VirtualKey), actionId);
        };
        RegisterHotkeys();

        // While a hotkey field in the settings has focus, the combination must reach the field.
        HotkeyTextBox.CaptureStarted += (_, _) => _hotkeys.UnregisterAll();
        HotkeyTextBox.CaptureEnded += (_, _) => RegisterHotkeys();

        _ = RunPreflightAsync(settings);
    }

    /// <summary>(Re)registers the second hotkey and the hotkeys of all active actions from the current config.</summary>
    private void RegisterHotkeys()
    {
        if (_hotkeys is null || _log is null)
            return;
        _hotkeys.UnregisterAll();
        _hotkeyTargets.Clear();

        var config = _config.Current;
        var wanted = new List<(string Text, string? ActionId, string Name)> { (config.Settings.SecondHotkey, null, "zweiter Hotkey") };
        wanted.AddRange(config.EnabledActions
            .Where(a => !string.IsNullOrWhiteSpace(a.Hotkey))
            .Select(a => (a.Hotkey!, (string?)a.Id, $"„{a.Label}“")));

        var active = new List<string>();
        var taken = new List<string>();
        foreach (var (text, actionId, name) in wanted)
        {
            if (!HotkeyGesture.TryParse(text, out var gesture))
                continue; // validation already rejected it; nothing to register
            if (_hotkeys.TryRegister(gesture))
            {
                _hotkeyTargets[gesture] = actionId;
                active.Add($"{gesture} ({name})");
            }
            else
            {
                taken.Add($"{gesture} ({name})");
            }
        }

        var status = $"Strg+C+C ({config.Settings.DoubleCopyMs} ms) aktiv. Hotkeys: {(active.Count > 0 ? string.Join(", ", active) : "keine")}.";
        if (taken.Count > 0)
            status += $" Von einem anderen Programm belegt: {string.Join(", ", taken)}.";
        _log.SetHotkeyStatus(status);
    }

    /// <summary>Puts a newly saved or reloaded configuration into effect without a restart.</summary>
    private void ApplyConfig()
    {
        if (_hook is not null)
            _hook.DoubleCopyMilliseconds = _config.Current.Settings.DoubleCopyMs;
        RegisterHotkeys();
    }

    private void OpenSettings()
    {
        if (_settings is not null)
        {
            _settings.Activate();
            return;
        }
        var viewModel = new SettingsViewModel(_config,
            (request, token) => new ClaudeCli(_config.Current.Settings.ClaudePath).RunAsync(request, token));
        viewModel.Saved += (_, _) => ApplyConfig();
        _settings = new SettingsWindow(viewModel);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
        _settings.Activate();
    }

    private async Task RunPreflightAsync(AppSettings settings)
    {
        var preflight = await new ClaudeCli(settings.ClaudePath).PreflightAsync(settings.Model);
        _log?.SetPreflight($"Claude: {preflight.Message}");
    }

    /// <summary>Reads actions.json again if it changed; a broken file keeps the last valid one.</summary>
    private void ReloadConfig()
    {
        var before = _config.Current;
        var ok = _config.Reload();
        if (!ReferenceEquals(before, _config.Current) && _hotkeys is not null)
            ApplyConfig(); // edited by hand: hotkeys and timing follow without a restart
        if (ok)
            return;
        _log?.Add(new CaptureLogWindow.Entry(
            "actions.json fehlerhaft, die letzte gültige Konfiguration bleibt aktiv",
            string.Join(Environment.NewLine, _config.LoadErrors),
            _config.FilePath));
    }

    /// <param name="actionId">Set for an action's own hotkey: the popup runs that action right away.</param>
    private async void Capture(string trigger, Func<Task<CaptureResult>> capture, string? actionId)
    {
        if (_capturing || _log is null)
            return;
        var source = ForegroundWindow.Current; // "Ersetzen" pastes back into this window
        if (ForegroundWindow.BelongsToThisProcess(source))
            return; // copying inside the popup or the settings must not open another popup
        _capturing = true;
        try
        {
            var anchor = ForegroundWindow.CursorPosition();
            var result = await capture();
            ReloadConfig();

            _log.Add(new CaptureLogWindow.Entry(
                $"{DateTime.Now:HH:mm:ss}  {trigger}  {(result.Success ? "✓" : "✗")}",
                result.Text is { } text ? $"{text.Length} Zeichen" : result.Message ?? "",
                ""));
            ShowPopup(result, anchor, source, actionId);
        }
        catch (Exception ex)
        {
            _log.Add(new CaptureLogWindow.Entry($"{DateTime.Now:HH:mm:ss}  {trigger}  Fehler", ex.Message, ""));
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

        var popup = new PopupWindow(viewModel, anchor);
        var closed = false;
        popup.Closed += (_, _) => { closed = true; if (_popup == popup) _popup = null; };
        viewModel.ReplaceRequested += async (_, text) =>
        {
            // Out of the way first, so the source window can take the focus back.
            popup.Hide();
            var replacer = new TextReplacer(new SystemClipboard(_clipboard), new KeyboardInput(), new SystemWindowActivator());
            var result = await replacer.ReplaceAsync(source, text, TimeSpan.FromMilliseconds(config.Settings.PasteDelayMs));
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
            var settingsViewModel = new SettingsViewModel(demoStore, fake);
            var settingsWindow = new SettingsWindow(settingsViewModel);
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
                    settingsViewModel.Selected!.Hotkey = "ctrl+alt+space";
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
        var popup = new PopupWindow(viewModel, ((int)(area.Width / 2 - 240), (int)(area.Height / 2 - 200)));
        MainWindow = popup;
        // Start the action only after the popup is on screen, as in real use. Starting it before
        // Loaded hid the busy-bar crash: the bar was visible from the start there.
        popup.ContentRendered += (_, _) =>
        {
            switch (state)
            {
                case "input": viewModel.TryRunShortcut("8"); break;
                case "running" or "result" or "error": viewModel.TryRunShortcut("2"); break;
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
        base.OnExit(e);
    }
}
