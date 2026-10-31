using System.Windows;

namespace ClaudeHotkey.Views;

/// <summary>
/// Temporary window for the phase 2 acceptance test: lists every capture. The popup (phase 3)
/// and the tray (phase 6) replace it.
/// </summary>
public partial class CaptureLogWindow : Window
{
    public sealed record Entry(string Header, string Body, string Clipboard);

    public CaptureLogWindow() => InitializeComponent();

    public void SetPreflight(string text) => PreflightText.Text = text;

    public void SetHotkeyStatus(string text) => HotkeyText.Text = text;

    public void Add(Entry entry)
    {
        Log.Items.Insert(0, entry);
        Log.SelectedIndex = 0;
    }
}
