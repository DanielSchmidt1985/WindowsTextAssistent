using System.Windows;

namespace ClaudeHotkey.Views;

/// <summary>
/// Temporary status window: preflight, hotkeys, captures and config errors. The tray icon
/// (phase 6) replaces it.
/// </summary>
public partial class CaptureLogWindow : Window
{
    public sealed record Entry(string Header, string Body, string Clipboard);

    public CaptureLogWindow() => InitializeComponent();

    public event EventHandler? SettingsRequested;

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    public void SetPreflight(string text) => PreflightText.Text = text;

    public void SetHotkeyStatus(string text) => HotkeyText.Text = text;

    public void Add(Entry entry)
    {
        Log.Items.Insert(0, entry);
        Log.SelectedIndex = 0;
    }
}
