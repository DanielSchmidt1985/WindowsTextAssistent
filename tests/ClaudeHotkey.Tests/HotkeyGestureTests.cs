using ClaudeHotkey.Core.Hotkeys;

namespace ClaudeHotkey.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("ctrl+alt+space", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x20)]
    [InlineData("Strg+Alt+Leertaste", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x20)]
    [InlineData("ctrl + shift + t", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 'T')]
    [InlineData("win+f12", HotkeyModifiers.Win, 0x7B)]
    [InlineData("alt+1", HotkeyModifiers.Alt, '1')]
    public void Parses_valid_gestures(string text, HotkeyModifiers modifiers, int virtualKey)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(new HotkeyGesture(modifiers, virtualKey), gesture);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ctrl+alt")]          // no key
    [InlineData("shift+a")]           // would fire while typing
    [InlineData("a")]
    [InlineData("ctrl+a+b")]          // two keys
    [InlineData("ctrl+unbekannt")]
    [InlineData("ctrl+f25")]
    public void Rejects_invalid_gestures(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }

    [Theory]
    [InlineData("ctrl+alt+space")]
    [InlineData("ctrl+shift+t")]
    [InlineData("win+f12")]
    public void Round_trips_through_string(string text)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(text, gesture.ToString());
    }
}
