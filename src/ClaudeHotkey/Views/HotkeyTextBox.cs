using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ClaudeHotkey.Views;

/// <summary>Turns two quick presses of the same combination into a double press ("ctrl+x" → "ctrl+x+x").</summary>
public static class HotkeyCaptureRules
{
    public const int DoublePressWindowMs = 600;

    /// <param name="previousCombo">The combination of the press before, or null.</param>
    /// <returns>The text for the box: <paramref name="combo"/>, or its double press.</returns>
    public static string Combine(string? previousCombo, long previousMs, string combo, long nowMs) =>
        previousCombo == combo && nowMs - previousMs <= DoublePressWindowMs
            ? $"{combo}+{combo[(combo.LastIndexOf('+') + 1)..]}"
            : combo;
}

/// <summary>
/// Text box that records the pressed key combination ("ctrl+alt+k") instead of typing; pressing
/// it twice quickly records a double press ("ctrl+x+x"). Backspace or Delete clears it, Tab moves
/// on. While it has focus the app's global hotkeys are suspended
/// (<see cref="CaptureStarted"/>/<see cref="CaptureEnded"/>), otherwise an already registered
/// combination would never reach the box.
/// </summary>
public sealed class HotkeyTextBox : TextBox
{
    public static event EventHandler? CaptureStarted;
    public static event EventHandler? CaptureEnded;

    private string? _previousCombo;
    private long _previousMs;

    public HotkeyTextBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        ContextMenu = null;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        CaptureStarted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        CaptureEnded?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        if (key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift)
            return; // keep normal focus navigation

        e.Handled = true;
        if (e.IsRepeat)
            return; // holding the key is not a second press
        if (key is Key.Back or Key.Delete && modifiers == ModifierKeys.None)
        {
            _previousCombo = null;
            SetText("");
            return;
        }
        if (KeyName(key) is not { } name)
            return; // only a modifier so far, or a key we do not support

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("win");
        parts.Add(name);
        var combo = string.Join('+', parts);

        var now = Environment.TickCount64;
        var text = HotkeyCaptureRules.Combine(_previousCombo, _previousMs, combo, now);
        // After a double press, the next press starts over with a single one.
        _previousCombo = text == combo ? combo : null;
        _previousMs = now;
        SetText(text);
    }

    private void SetText(string text)
    {
        Text = text;
        GetBindingExpression(TextProperty)?.UpdateSource();
    }

    internal static string? KeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ((char)('a' + (key - Key.A))).ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
        >= Key.F1 and <= Key.F24 => $"f{key - Key.F1 + 1}",
        Key.Space => "space",
        Key.Enter => "enter",
        Key.Insert => "insert",
        Key.Delete => "delete",
        Key.Home => "home",
        Key.End => "end",
        Key.PageUp => "pageup",
        Key.PageDown => "pagedown",
        Key.Pause => "pause",
        _ => null,
    };
}
