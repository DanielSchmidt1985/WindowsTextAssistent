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
    [InlineData("ctrl+a+b")]          // two different keys
    [InlineData("ctrl+x+x+x")]        // triple press
    [InlineData("x+ctrl+x")]          // modifier after a key
    [InlineData("shift+x+x")]         // double press still needs Ctrl, Alt or Win
    [InlineData("ctrl+unbekannt")]
    [InlineData("ctrl+f25")]
    public void Rejects_invalid_gestures(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }

    [Theory]
    [InlineData("ctrl+x+x", HotkeyModifiers.Ctrl, 'X')]
    [InlineData("Strg+Alt+Leertaste+Leertaste", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x20)]
    [InlineData("alt+f2+f2", HotkeyModifiers.Alt, 0x71)]
    public void Parses_double_press(string text, HotkeyModifiers modifiers, int virtualKey)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(new HotkeyGesture(modifiers, virtualKey, IsDoublePress: true), gesture);
    }

    [Fact]
    public void Single_and_double_press_differ_but_share_keys()
    {
        HotkeyGesture.TryParse("ctrl+x", out var single);
        HotkeyGesture.TryParse("ctrl+x+x", out var twice);

        Assert.NotEqual(single, twice);
        Assert.True(single!.SharesKeysWith(twice!));
    }

    [Fact]
    public void Default_main_hotkey_is_ctrl_c_c()
    {
        Assert.Equal("ctrl+c+c", HotkeyGesture.DoubleCopy.ToString());
    }

    [Theory]
    [InlineData("ctrl+c+c", true)]
    [InlineData("ctrl+x+x", true)]
    [InlineData("ctrl+shift+c+c", false)] // the app does not copy on Ctrl+Shift+C
    [InlineData("ctrl+alt+space", false)]
    [InlineData("ctrl+q+q", false)]
    public void Knows_when_the_app_copies_itself(string text, bool expected)
    {
        HotkeyGesture.TryParse(text, out var gesture);

        Assert.Equal(expected, gesture!.AppCopiesItself);
    }

    [Theory]
    [InlineData("ctrl+c", true)]
    [InlineData("ctrl+x", true)]
    [InlineData("ctrl+v", true)]
    [InlineData("ctrl+c+c", false)]
    [InlineData("ctrl+alt+c", false)]
    public void Knows_which_single_hotkeys_would_block_the_clipboard(string text, bool expected)
    {
        HotkeyGesture.TryParse(text, out var gesture);

        Assert.Equal(expected, gesture!.BlocksClipboardKeys);
    }

    [Theory]
    [InlineData("ctrl+alt+space")]
    [InlineData("ctrl+shift+t")]
    [InlineData("win+f12")]
    [InlineData("ctrl+x+x")]
    [InlineData("ctrl+alt+f2+f2")]
    public void Round_trips_through_string(string text)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(text, gesture.ToString());
    }
}
