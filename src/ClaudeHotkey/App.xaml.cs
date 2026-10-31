using System.Windows;
using ClaudeHotkey.Core.Claude;
using ClaudeHotkey.Core.Clipboard;
using ClaudeHotkey.Core.Hotkeys;
using ClaudeHotkey.Core.Input;
using ClaudeHotkey.Views;

namespace ClaudeHotkey;

public partial class App : Application
{
    // Defaults from the plan; phase 3 reads them from actions.json.
    private const int DoubleCopyMilliseconds = 400;
    private const string SecondHotkey = "ctrl+alt+space";

    private readonly ClipboardService _clipboard = new();
    private KeyboardHook? _hook;
    private GlobalHotkeys? _hotkeys;
    private TextCapture? _capture;
    private CaptureLogWindow? _window;
    private bool _busy;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _window = new CaptureLogWindow();
        MainWindow = _window;
        _window.Show();

        _capture = new TextCapture(new SystemClipboard(_clipboard), new KeyboardInput());

        _hook = new KeyboardHook(DoubleCopyMilliseconds);
        _hook.DoubleCopy += (_, args) => Capture("Strg+C+C", () => _capture.AfterUserCopyAsync(args.ClipboardSequence));

        _hotkeys = new GlobalHotkeys();
        _hotkeys.Pressed += (_, gesture) => Capture(gesture.ToString(), () => _capture.BySendingCopyAsync(gesture.VirtualKey));
        if (!HotkeyGesture.TryParse(SecondHotkey, out var gesture))
            _window.SetHotkeyStatus($"Zweiter Hotkey „{SecondHotkey}“ ist ungültig.");
        else if (_hotkeys.TryRegister(gesture))
            _window.SetHotkeyStatus($"Strg+C+C ({DoubleCopyMilliseconds} ms) und {gesture} sind aktiv.");
        else
            _window.SetHotkeyStatus($"Strg+C+C ist aktiv. {gesture} ist schon von einem anderen Programm belegt.");

        _ = RunPreflightAsync();
    }

    private async Task RunPreflightAsync()
    {
        var preflight = await new ClaudeCli().PreflightAsync("haiku");
        _window?.SetPreflight($"Claude: {preflight.Message}");
    }

    private async void Capture(string trigger, Func<Task<CaptureResult>> capture)
    {
        if (_busy || _window is null)
            return;
        _busy = true;
        try
        {
            var result = await capture();
            var body = result.Text is { } text
                ? $"{text.Length} Zeichen: {Preview(text)}"
                : result.Message ?? "";
            _window.Add(new CaptureLogWindow.Entry(
                $"{DateTime.Now:HH:mm:ss}  {trigger}  {(result.Success ? "✓" : "✗")}",
                body,
                $"Zwischenablage jetzt: {Preview(_clipboard.GetText() ?? "(kein Text)")}"));
        }
        catch (Exception ex)
        {
            _window.Add(new CaptureLogWindow.Entry($"{DateTime.Now:HH:mm:ss}  {trigger}  Fehler", ex.Message, ""));
        }
        finally
        {
            _busy = false;
        }
    }

    private static string Preview(string text)
    {
        var flat = text.Replace("\r\n", " ⏎ ").Replace('\n', ' ');
        return flat.Length <= 200 ? flat : flat[..200] + " …";
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        _hotkeys?.Dispose();
        base.OnExit(e);
    }
}
