using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace ClaudeHotkey.Views;

/// <summary>
/// Tray icon with status (tooltip, warning icon) and the menu: Einstellungen, Logdatei, Beenden.
/// Double click opens the settings.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private static readonly BitmapFrame NormalIcon = LoadIcon("app.ico");
    private static readonly BitmapFrame WarningIcon = LoadIcon("app-warning.ico");

    private readonly TaskbarIcon _icon;
    private readonly MenuItem _statusItem;

    public TrayIcon()
    {
        _statusItem = new MenuItem { IsEnabled = false };
        var settings = new MenuItem { Header = "Einstellungen …", FontWeight = FontWeights.SemiBold };
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        var log = new MenuItem { Header = "Logdatei öffnen" };
        log.Click += (_, _) => OpenLogRequested?.Invoke(this, EventArgs.Empty);
        var exit = new MenuItem { Header = "Beenden" };
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _icon = new TaskbarIcon
        {
            IconSource = NormalIcon,
            ToolTipText = "ClaudeHotkey",
            ContextMenu = new ContextMenu { Items = { _statusItem, new Separator(), settings, log, new Separator(), exit } },
            DoubleClickCommand = new ViewModels.RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty)),
        };
        // false: no Windows efficiency mode. It throttles the process, and a throttled low-level
        // keyboard hook answers too slowly and gets dropped by Windows.
        _icon.ForceCreate(false);
    }

    public event EventHandler? SettingsRequested;
    public event EventHandler? OpenLogRequested;
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
