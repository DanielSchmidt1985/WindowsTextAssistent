using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ClaudeHotkey.Core.Windows;
using ClaudeHotkey.ViewModels;

namespace ClaudeHotkey.Views;

/// <summary>Borderless popup next to the mouse. Esc cancels a running call and closes.</summary>
public partial class PopupWindow : Window
{
    private readonly PopupViewModel _viewModel;
    private readonly (int X, int Y) _anchor;

    /// <param name="anchor">Mouse position in physical pixels at the moment of the trigger.</param>
    public PopupWindow(PopupViewModel viewModel, (int X, int Y) anchor)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _anchor = anchor;
        DataContext = viewModel;

        viewModel.CloseRequested += (_, _) => Close();
        viewModel.InputRequested += (_, _) => Dispatcher.BeginInvoke(() => { InputBox.Focus(); Keyboard.Focus(InputBox); });

        SourceInitialized += (_, _) => Place();
        // The window grows when the result arrives; keep it inside the screen.
        SizeChanged += (_, _) => Place();
        Loaded += (_, _) => ForegroundWindow.Activate(new WindowInteropHelper(this).Handle);
    }

    private void Place() => ForegroundWindow.PlaceNear(new WindowInteropHelper(this).Handle, _anchor.X, _anchor.Y);

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        // Digits run actions, except while typing an instruction.
        if (!InputBox.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.None && DigitOf(e.Key) is { } digit)
            e.Handled = _viewModel.TryRunShortcut(digit);

        base.OnPreviewKeyDown(e);
    }

    private static string? DigitOf(Key key) => key switch
    {
        >= Key.D1 and <= Key.D9 => ((int)(key - Key.D1) + 1).ToString(),
        >= Key.NumPad1 and <= Key.NumPad9 => ((int)(key - Key.NumPad1) + 1).ToString(),
        _ => null,
    };

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.Cancel();
        base.OnClosing(e);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RequestSettings();
        Close();
    }
}
