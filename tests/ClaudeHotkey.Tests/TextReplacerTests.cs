using ClaudeHotkey.Core.Windows;
using static ClaudeHotkey.Tests.TextCaptureTests;

namespace ClaudeHotkey.Tests;

public class TextReplacerTests
{
    private static readonly IntPtr Source = new(0x1234);
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(500);

    private sealed class FakeWindows : IWindowActivator
    {
        public List<string> Log { get; } = [];
        public bool WindowExists { get; init; } = true;
        public bool BecomesForeground { get; init; } = true;

        public bool Exists(IntPtr hwnd) => WindowExists;

        public bool Activate(IntPtr hwnd)
        {
            Log.Add($"activate {hwnd:X}");
            return BecomesForeground;
        }

        public Task<bool> WaitUntilForegroundAsync(IntPtr hwnd, TimeSpan timeout) => Task.FromResult(BecomesForeground);
    }

    [Fact]
    public async Task Pastes_result_and_restores_previous_clipboard()
    {
        var clipboard = new FakeClipboard { Text = "vorher" };
        string? clipboardAtPaste = null;
        var keyboard = new FakeKeyboard(() => { });
        var windows = new FakeWindows();
        var replacer = new TextReplacer(clipboard, keyboard, windows, () =>
        {
            clipboardAtPaste = clipboard.Text; // what the target app reads after Ctrl+V
            return Task.CompletedTask;
        });

        var result = await replacer.ReplaceAsync(Source, "Ergebnis", Delay);

        Assert.True(result.Success);
        Assert.Equal(["activate 1234"], windows.Log);
        Assert.Equal([0x56], keyboard.Sent);
        Assert.Equal("Ergebnis", clipboardAtPaste);
        Assert.Equal("vorher", clipboard.Text);
    }

    [Fact]
    public async Task Does_not_overwrite_what_the_user_copied_meanwhile()
    {
        var clipboard = new FakeClipboard { Text = "vorher" };
        var replacer = new TextReplacer(clipboard, new FakeKeyboard(() => { }), new FakeWindows(), () =>
        {
            clipboard.SimulateCopy("neu kopiert");
            return Task.CompletedTask;
        });

        await replacer.ReplaceAsync(Source, "Ergebnis", Delay);

        Assert.Equal("neu kopiert", clipboard.Text);
        Assert.Equal(0, clipboard.Restores);
    }

    [Fact]
    public async Task Closed_source_window_reports_and_leaves_clipboard()
    {
        var clipboard = new FakeClipboard { Text = "vorher" };
        var keyboard = new FakeKeyboard(() => { });

        var result = await new TextReplacer(clipboard, keyboard, new FakeWindows { WindowExists = false }).ReplaceAsync(Source, "Ergebnis", Delay);

        Assert.False(result.Success);
        Assert.Equal(TextReplacer.WindowGoneMessage, result.Message);
        Assert.Equal("vorher", clipboard.Text);
        Assert.Empty(keyboard.Sent);
    }

    [Fact]
    public async Task Failed_activation_keeps_result_in_clipboard_and_sends_nothing()
    {
        var clipboard = new FakeClipboard { Text = "vorher" };
        var keyboard = new FakeKeyboard(() => { });

        var result = await new TextReplacer(clipboard, keyboard, new FakeWindows { BecomesForeground = false }).ReplaceAsync(Source, "Ergebnis", Delay);

        Assert.False(result.Success);
        Assert.Equal(TextReplacer.ActivateFailedMessage, result.Message);
        Assert.Equal("Ergebnis", clipboard.Text);
        Assert.Empty(keyboard.Sent);
    }

    [Fact]
    public async Task Held_keys_prevent_paste()
    {
        var clipboard = new FakeClipboard { Text = "vorher" };
        var keyboard = new FakeKeyboard(() => { }) { ReleaseSucceeds = false };

        var result = await new TextReplacer(clipboard, keyboard, new FakeWindows()).ReplaceAsync(Source, "Ergebnis", Delay);

        Assert.Equal(TextReplacer.KeysHeldMessage, result.Message);
        Assert.Empty(keyboard.Sent);
    }
}
