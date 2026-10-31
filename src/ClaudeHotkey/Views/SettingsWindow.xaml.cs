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

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        InputBindings.Add(new KeyBinding(viewModel.SaveCommand, Key.S, ModifierKeys.Control));
    }

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
