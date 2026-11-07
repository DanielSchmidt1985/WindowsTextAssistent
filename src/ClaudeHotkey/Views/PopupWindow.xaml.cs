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
    private readonly Func<double> _scale;
    private readonly Action<int> _widthChanged;
    private bool _movedByUser;

    /// <param name="anchor">Mouse position in physical pixels at the moment of the trigger.</param>
    /// <param name="width">Remembered popup width in DIPs.</param>
    /// <param name="scale">Current UI scale factor (1.0 = 100 %).</param>
    /// <param name="stepScale">Ctrl+Plus/Minus/0 and Ctrl+wheel: +1, -1 or 0.</param>
    /// <param name="widthChanged">Called with the new width after the user dragged the right edge.</param>
    public PopupWindow(PopupViewModel viewModel, (int X, int Y) anchor, int width, Func<double> scale, Action<int> stepScale, Action<int> widthChanged)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _anchor = anchor;
        _scale = scale;
        _widthChanged = widthChanged;
        DataContext = viewModel;
        Width = width;
        UiScale.UpdateTextMode(this, (int)Math.Round(scale() * 100));
        UiScale.AttachShortcuts(this, stepScale);

        viewModel.CloseRequested += (_, _) => Close();
        viewModel.InputRequested += (_, _) => Dispatcher.BeginInvoke(() => { InputBox.Focus(); Keyboard.Focus(InputBox); });
        // Deferred: the field is collapsed while the call runs and becomes visible with the result.
        viewModel.RefineFocusRequested += (_, _) => Dispatcher.BeginInvoke(() => { RefineBox.Focus(); Keyboard.Focus(RefineBox); },
                                                                           System.Windows.Threading.DispatcherPriority.Input);

        SourceInitialized += (_, _) => Place();
        // The window grows when the result arrives; keep it inside the screen (unless the user moved it).
        SizeChanged += (_, _) => { if (!_movedByUser) Place(); };
        Loaded += (_, _) =>
        {
            UpdateMaxHeights();
            ForegroundWindow.Activate(new WindowInteropHelper(this).Handle);
        };
        LayoutUpdated += (_, _) => UpdateMaxHeights();
        // Follow the text while it streams in, once it is longer than the box.
        StreamBox.TextChanged += (_, _) => StreamBox.ScrollToEnd();
    }

    private void Place() => ForegroundWindow.PlaceNear(new WindowInteropHelper(this).Handle, _anchor.X, _anchor.Y);

    /// <summary>Result and feedback may use about half / a third of the screen before they scroll.</summary>
    private void UpdateMaxHeights()
    {
        var available = SystemParameters.WorkArea.Height / _scale();
        ResultBox.MaxHeight = Math.Max(120, available * 0.45);
        StreamBox.MaxHeight = ResultBox.MaxHeight;
        FeedbackBox.MaxHeight = Math.Max(80, available * 0.3);
    }

    private void WidthGrip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        // The thumb sits inside the scaled content, so its offsets are in unscaled units.
        Width = Math.Clamp(Width + e.HorizontalChange * _scale(), Core.Config.UiState.MinPopupWidth, Core.Config.UiState.MaxPopupWidth);
    }

    private void WidthGrip_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
        _widthChanged((int)Math.Round(Width));

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        // Enter in the follow-up field sends the change request; with an empty field it falls through
        // to the default button "Ersetzen", so Enter after a follow-up still replaces.
        if (e.Key == Key.Enter && RefineBox.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.None
            && _viewModel.RefineCommand.CanExecute(null))
        {
            e.Handled = true;
            _viewModel.RefineCommand.Execute(null);
            return;
        }

        // Digits run actions, except while typing an instruction or a change request.
        var typing = InputBox.IsKeyboardFocusWithin || RefineBox.IsKeyboardFocusWithin;
        if (!typing && Keyboard.Modifiers == ModifierKeys.None && DigitOf(e.Key) is { } digit)
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
        if (e.ButtonState != MouseButtonState.Pressed)
            return;
        DragMove();
        _movedByUser = true; // from now on growing must not snap it back to the mouse
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RequestSettings();
        Close();
    }
}
