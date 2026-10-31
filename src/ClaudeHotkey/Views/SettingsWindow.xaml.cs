using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClaudeHotkey.ViewModels;

namespace ClaudeHotkey.Views;

/// <summary>Settings dialog: actions on the left, their form on the right, general settings on a second tab.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private Point _dragStart;
    private ActionEditorViewModel? _dragItem;

    /// <param name="bounds">Last size and position, or null for the default (centered).</param>
    /// <param name="stepScale">Ctrl+Plus/Minus/0 and Ctrl+wheel: +1, -1 or 0.</param>
    public SettingsWindow(SettingsViewModel viewModel, Core.Config.WindowBounds? bounds = null, Action<int>? stepScale = null, int scalePercent = 100)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        InputBindings.Add(new KeyBinding(viewModel.SaveCommand, Key.S, ModifierKeys.Control));
        UiScale.UpdateTextMode(this, scalePercent);
        if (stepScale is not null)
            UiScale.AttachShortcuts(this, stepScale);
        if (bounds is not null && IsOnScreen(bounds))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            (Left, Top, Width, Height) = (bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        }
    }

    /// <summary>Size and position to remember (the normal bounds, also when maximized).</summary>
    public Core.Config.WindowBounds CurrentBounds =>
        new(RestoreBounds.Left, RestoreBounds.Top, RestoreBounds.Width, RestoreBounds.Height);

    /// <summary>A monitor may be gone since last time; then open centered instead of off-screen.</summary>
    private static bool IsOnScreen(Core.Config.WindowBounds b) =>
        b.Left + 100 < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
        && b.Left + b.Width - 100 > SystemParameters.VirtualScreenLeft
        && b.Top >= SystemParameters.VirtualScreenTop - 10
        && b.Top + 40 < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || !_viewModel.IsDirty)
        {
            _viewModel.CancelTests();
            return;
        }

        var answer = MessageBox.Show(this, "Die Änderungen sind noch nicht gespeichert. Jetzt speichern?",
            "ClaudeHotkey", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !_viewModel.Save()))
        {
            e.Cancel = true; // stay open, e.g. to fix the fields marked red
            return;
        }
        _viewModel.CancelTests();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>0 = Aktionen, 1 = Allgemein.</summary>
    public void SelectTab(int index) => Tabs.SelectedIndex = index;

    // Drag and drop to reorder the actions

    private void ActionList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(ActionList);
        _dragItem = ItemAt(e.OriginalSource as DependencyObject);
    }

    private void ActionList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null)
            return;
        var delta = e.GetPosition(ActionList) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var item = _dragItem;
        _dragItem = null;
        DragDrop.DoDragDrop(ActionList, item, DragDropEffects.Move);
    }

    private void ActionList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ActionEditorViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void ActionList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ActionEditorViewModel)) is not ActionEditorViewModel dragged)
            return;
        var target = ItemAt(e.OriginalSource as DependencyObject);
        var from = _viewModel.Actions.IndexOf(dragged);
        // Dropped below the last row: move to the end.
        var to = target is null ? _viewModel.Actions.Count - 1 : _viewModel.Actions.IndexOf(target);
        _viewModel.Move(from, to);
        _viewModel.Selected = dragged;
    }

    private static ActionEditorViewModel? ItemAt(DependencyObject? element)
    {
        while (element is not null and not ListBoxItem)
            element = VisualTreeHelper.GetParent(element);
        return (element as ListBoxItem)?.DataContext as ActionEditorViewModel;
    }
}
