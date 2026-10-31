using System.Windows;
using ClaudeHotkey.Core.Claude;

namespace ClaudeHotkey;

public partial class App : Application
{
    // Phase 1: no tray or popup yet. The app only runs the preflight and reports the result.
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var preflight = await new ClaudeCli().PreflightAsync("haiku");
        MessageBox.Show(
            preflight.Details is null ? preflight.Message : $"{preflight.Message}\n\n{preflight.Details}",
            "ClaudeHotkey",
            MessageBoxButton.OK,
            preflight.Success && preflight.UsesSubscription ? MessageBoxImage.Information : MessageBoxImage.Warning);
        Shutdown();
    }
}
