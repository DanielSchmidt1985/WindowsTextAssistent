using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using ClaudeHotkey.Core.Localization;

namespace ClaudeHotkey.Views;

/// <summary>
/// Tray icon with status (tooltip, warning icon) and the menu: settings, log file, exit.
/// Double click opens the settings.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private static readonly BitmapFrame NormalIcon = LoadIcon("app.ico");
    private static readonly BitmapFrame WarningIcon = LoadIcon("app-warning.ico");

    private readonly TaskbarIcon _icon;
    private readonly MenuItem _statusItem;
    private readonly MenuItem _settingsItem;
    private readonly MenuItem _logItem;
    private readonly MenuItem _updateItem;
    private readonly MenuItem _exitItem;

    public TrayIcon()
    {
        _statusItem = new MenuItem { IsEnabled = false };
        var settings = _settingsItem = new MenuItem { FontWeight = FontWeights.SemiBold };
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        var log = _logItem = new MenuItem();
        log.Click += (_, _) => OpenLogRequested?.Invoke(this, EventArgs.Empty);
        var update = _updateItem = new MenuItem();
        update.Click += (_, _) => UpdateCheckRequested?.Invoke(this, EventArgs.Empty);
        var exit = _exitItem = new MenuItem();
        UpdateTexts();
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _icon = new TaskbarIcon
        {
            IconSource = NormalIcon,
            ToolTipText = "ClaudeHotkey",
            ContextMenu = new ContextMenu { Items = { _statusItem, new Separator(), settings, log, update, new Separator(), exit } },
            DoubleClickCommand = new ViewModels.RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty)),
        };
        // false: no Windows efficiency mode. It throttles the process, and a throttled low-level
        // keyboard hook answers too slowly and gets dropped by Windows.
        _icon.ForceCreate(false);
    }

    /// <summary>Menu texts in the current UI language; called again after the language changed.</summary>
    public void UpdateTexts()
    {
        _settingsItem.Header = Lang.T("Einstellungen …", "Settings …");
        _logItem.Header = Lang.T("Logdatei öffnen", "Open log file");
        _updateItem.Header = Lang.T("Nach Updates suchen", "Check for updates");
        _exitItem.Header = Lang.T("Beenden", "Exit");
    }

    public event EventHandler? SettingsRequested;
    public event EventHandler? OpenLogRequested;
    public event EventHandler? UpdateCheckRequested;
    public event EventHandler? ExitRequested;

    /// <param name="summary">One line, shown in the tooltip and at the top of the menu.</param>
    /// <param name="warning">Switches to the icon with the red dot.</param>
    public void SetStatus(string summary, bool warning)
    {
        _icon.IconSource = warning ? WarningIcon : NormalIcon;
        // The tray tooltip is limited to 127 characters.
        var tip = $"ClaudeHotkey – {summary}";
        _icon.ToolTipText = tip.Length <= 127 ? tip : tip[..126] + "…";
        _statusItem.Header = summary;
    }

    public void Notify(string title, string message, bool warning) =>
        _icon.ShowNotification(title, message, warning ? NotificationIcon.Warning : NotificationIcon.Info);

    public void Dispose() => _icon.Dispose();

    private static BitmapFrame LoadIcon(string name) =>
        BitmapFrame.Create(new Uri($"pack://application:,,,/Assets/{name}", UriKind.Absolute));
}
