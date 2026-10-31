using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ClaudeHotkey.Views;

/// <summary>
/// One scale for text and controls of all windows: the ScaleTransform "UiScale" in the app
/// resources, used as LayoutTransform (DynamicResource) at each window's root.
/// </summary>
public static class UiScale
{
    public const string ResourceKey = "UiScale";

    /// <summary>
    /// Sets the scale by putting a new transform into the resources; open windows follow through
    /// their DynamicResource. Changing the existing one does not work: Application.Resources
    /// freezes a Freezable when it is added (also when created in code, and despite
    /// PresentationOptions:Freeze="False"), and setting ScaleX then throws.
    /// </summary>
    public static void Apply(int percent)
    {
        var app = Application.Current;
        if (app is null)
            return;
        var transform = new ScaleTransform(percent / 100.0, percent / 100.0);
        transform.Freeze();
        app.Resources[ResourceKey] = transform;
        // Display mode is sharpest at 100 %, but snaps glyphs to pixels and looks uneven when scaled.
        foreach (Window window in app.Windows)
            UpdateTextMode(window, percent);
    }

    public static void UpdateTextMode(Window window, int percent) =>
        TextOptions.SetTextFormattingMode(window, percent == 100 ? TextFormattingMode.Display : TextFormattingMode.Ideal);

    /// <summary>
    /// Ctrl+Plus / Ctrl+Minus / Ctrl+0 and Ctrl+mouse wheel in <paramref name="window"/> call
    /// <paramref name="step"/> with +1, -1 or 0.
    /// </summary>
    public static void AttachShortcuts(Window window, Action<int> step)
    {
        window.PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
                return;
            int? direction = e.Key switch
            {
                Key.OemPlus or Key.Add => 1,
                Key.OemMinus or Key.Subtract => -1,
                Key.D0 or Key.NumPad0 => 0,
                _ => null,
            };
            if (direction is { } d)
            {
                step(d);
                e.Handled = true;
            }
        };
        window.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
                return;
            step(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        };
    }
}
