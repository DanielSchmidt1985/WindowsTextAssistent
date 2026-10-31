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
    private bool _capturing;

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
        MainWindow = _log;
        _log.Show();

        foreach (var problem in ThemeOverrides.Apply(Resources, _config.Directory))
            _log.Add(new CaptureLogWindow.Entry("Theme", problem, ""));

        ReloadConfig();
        var settings = _config.Current.Settings;
        _capture = new TextCapture(new SystemClipboard(_clipboard), new KeyboardInput());

        _hook = new KeyboardHook(settings.DoubleCopyMs);
        _hook.DoubleCopy += (_, args) => Capture("Strg+C+C", () => _capture.AfterUserCopyAsync(args.ClipboardSequence));

        _hotkeys = new GlobalHotkeys();
        _hotkeys.Pressed += (_, gesture) => Capture(gesture.ToString(), () => _capture.BySendingCopyAsync(gesture.VirtualKey));
        if (HotkeyGesture.TryParse(settings.SecondHotkey, out var gesture) && _hotkeys.TryRegister(gesture))
            _log.SetHotkeyStatus($"Strg+C+C ({settings.DoubleCopyMs} ms) und {gesture} sind aktiv.");
        else
            _log.SetHotkeyStatus($"Strg+C+C ist aktiv. {settings.SecondHotkey} ist schon von einem anderen Programm belegt.");

        _ = RunPreflightAsync(settings);
    }

    private async Task RunPreflightAsync(AppSettings settings)
    {
        var preflight = await new ClaudeCli(settings.ClaudePath).PreflightAsync(settings.Model);
        _log?.SetPreflight($"Claude: {preflight.Message}");
    }

    /// <summary>Reads actions.json again if it changed; a broken file keeps the last valid one.</summary>
    private void ReloadConfig()
    {
        if (_config.Reload())
            return;
        _log?.Add(new CaptureLogWindow.Entry(
            "actions.json fehlerhaft, die letzte gültige Konfiguration bleibt aktiv",
            string.Join(Environment.NewLine, _config.LoadErrors),
            _config.FilePath));
    }

    private async void Capture(string trigger, Func<Task<CaptureResult>> capture)
    {
        if (_capturing || _log is null)
            return;
        _capturing = true;
        try
        {
            var anchor = ForegroundWindow.CursorPosition();
            var source = ForegroundWindow.Current; // needed for "Ersetzen" in phase 4
            var result = await capture();
            ReloadConfig();
            if (_hook is not null)
                _hook.DoubleCopyMilliseconds = _config.Current.Settings.DoubleCopyMs;

            _log.Add(new CaptureLogWindow.Entry(
                $"{DateTime.Now:HH:mm:ss}  {trigger}  {(result.Success ? "✓" : "✗")}",
                result.Text is { } text ? $"{text.Length} Zeichen" : result.Message ?? "",
                ""));
            ShowPopup(result, anchor);
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

    private void ShowPopup(CaptureResult capture, (int X, int Y) anchor)
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
        popup.Closed += (_, _) => { if (_popup == popup) _popup = null; };
        _popup = popup;
        popup.Show();
    }

    /// <summary>
    /// Opens the popup in one state with sample text and a simulated Claude answer, without hooks
    /// or CLI calls: <c>ClaudeHotkey.exe --demo choose|input|running|result|error|nocapture</c>.
    /// For checking the design against the reference screenshot.
    /// </summary>
    private void ShowDemo(string state)
    {
        ThemeOverrides.Apply(Resources, _config.Directory);
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
            }
        };
        popup.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _popup?.Close();
        _hook?.Dispose();
        _hotkeys?.Dispose();
        base.OnExit(e);
    }
}
