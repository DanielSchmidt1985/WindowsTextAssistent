using System.Windows;
using ClaudeHotkey.Core.Clipboard;
using ClaudeHotkey.Core.Input;

namespace ClaudeHotkey.Tests;

/// <summary>
/// Uses the real Windows clipboard. The current content is saved before each test and put back
/// afterwards, so running the tests does not lose what the user has copied.
/// </summary>
[Collection("Clipboard")]
public class ClipboardServiceTests
{
    private readonly ClipboardService _service = new();

    private static void OnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            throw new Xunit.Sdk.XunitException($"Failed on STA thread: {error}");
    }

    private void WithUserClipboardKept(Action test) => OnSta(() =>
    {
        var userContent = _service.Backup();
        try { test(); }
        finally { _service.Restore(userContent); }
    });

    [Fact]
    public void Text_survives_backup_and_restore()
    {
        WithUserClipboardKept(() =>
        {
            const string original = "Grüße 😀\r\nZeile zwei\ttab";
            _service.SetText(original);
            var backup = _service.Backup();

            _service.SetText("überschrieben");
            _service.Restore(backup);

            Assert.Equal(original, _service.GetText());
        });
    }

    [Fact]
    public void Several_formats_survive_backup_and_restore()
    {
        WithUserClipboardKept(() =>
        {
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, "plain");
            data.SetData(DataFormats.Html, "<b>fett</b>");
            data.SetData(DataFormats.Rtf, @"{\rtf1 {\b fett}}");
            System.Windows.Clipboard.SetDataObject(data, copy: true);
            var backup = _service.Backup();

            _service.SetText("überschrieben");
            _service.Restore(backup);

            Assert.Equal("plain", _service.GetText());
            Assert.True(System.Windows.Clipboard.ContainsData(DataFormats.Html));
            Assert.True(System.Windows.Clipboard.ContainsData(DataFormats.Rtf));
        });
    }

    [Fact]
    public void Empty_clipboard_restores_to_empty()
    {
        WithUserClipboardKept(() =>
        {
            System.Windows.Clipboard.Clear();
            var backup = _service.Backup();

            _service.SetText("x");
            _service.Restore(backup);

            Assert.True(backup.IsEmpty);
            Assert.Null(_service.GetText());
        });
    }

    [Fact]
    public void Long_text_survives()
    {
        WithUserClipboardKept(() =>
        {
            var text = string.Concat(Enumerable.Repeat("Äpfel und Birnen 🍐 ", 400)); // ~8000 chars
            _service.SetText(text);
            var backup = _service.Backup();
            _service.SetText("x");
            _service.Restore(backup);

            Assert.Equal(text, _service.GetText());
        });
    }

    [Fact]
    public void Sequence_number_changes_on_write()
    {
        WithUserClipboardKept(() =>
        {
            var before = ClipboardService.SequenceNumber;
            _service.SetText("neu");

            Assert.NotEqual(before, ClipboardService.SequenceNumber);
        });
    }
}

public class TextCaptureTests
{
    [Fact]
    public async Task Hotkey_path_copies_reads_and_restores()
    {
        var clipboard = new FakeClipboard { Text = "alter Inhalt" };
        var keyboard = new FakeKeyboard(() => clipboard.SimulateCopy("markierter Text"));

        var result = await new TextCapture(clipboard, keyboard).BySendingCopyAsync(0x20);

        Assert.Equal("markierter Text", result.Text);
        Assert.Equal("alter Inhalt", clipboard.Text);
        Assert.Equal([0x43], keyboard.Sent);
        Assert.Equal(0x20, keyboard.WaitedForKey);
    }

    [Fact]
    public async Task Hotkey_path_without_selection_reports_and_leaves_clipboard()
    {
        var clipboard = new FakeClipboard { Text = "alter Inhalt" };
        var keyboard = new FakeKeyboard(() => { }); // app copies nothing

        var result = await new TextCapture(clipboard, keyboard).BySendingCopyAsync(0x20);

        Assert.False(result.Success);
        Assert.Equal(TextCapture.NoTextMessage, result.Message);
        Assert.Equal("alter Inhalt", clipboard.Text);
        Assert.Equal(0, clipboard.Restores);
    }

    [Fact]
    public async Task Hotkey_path_gives_up_when_keys_stay_held()
    {
        var clipboard = new FakeClipboard { Text = "alt" };
        var keyboard = new FakeKeyboard(() => clipboard.SimulateCopy("x")) { ReleaseSucceeds = false };

        var result = await new TextCapture(clipboard, keyboard).BySendingCopyAsync(0x20);

        Assert.Equal(TextCapture.KeysHeldMessage, result.Message);
        Assert.Empty(keyboard.Sent);
    }

    [Fact]
    public async Task Double_copy_path_reads_text_and_keeps_it()
    {
        var clipboard = new FakeClipboard { Text = "markiert" };
        var keyboard = new FakeKeyboard(() => { });

        var result = await new TextCapture(clipboard, keyboard).AfterUserCopyAsync(clipboard.SequenceNumber);

        Assert.Equal("markiert", result.Text);
        Assert.Equal("markiert", clipboard.Text);
        Assert.Empty(keyboard.Sent);
        Assert.Equal(0, clipboard.Restores);
    }

    [Fact]
    public async Task Double_copy_path_with_empty_clipboard_reports()
    {
        var clipboard = new FakeClipboard { Text = null };

        var result = await new TextCapture(clipboard, new FakeKeyboard(() => { })).AfterUserCopyAsync(clipboard.SequenceNumber);

        Assert.False(result.Success);
    }

    private sealed class FakeClipboard : IClipboard
    {
        public string? Text { get; set; }
        public uint SequenceNumber { get; private set; } = 1;
        public int Restores { get; private set; }

        public void SimulateCopy(string text)
        {
            Text = text;
            SequenceNumber++;
        }

        public string? GetText() => Text;

        public ClipboardSnapshot Backup() =>
            new(Text is null ? [] : [new KeyValuePair<string, object>(DataFormats.UnicodeText, Text)]);

        public void Restore(ClipboardSnapshot snapshot)
        {
            Restores++;
            Text = snapshot.IsEmpty ? null : (string)snapshot.Formats[0].Value;
            SequenceNumber++;
        }

        public Task<bool> WaitForChangeAsync(uint sequence, TimeSpan timeout) => Task.FromResult(SequenceNumber != sequence);
    }

    private sealed class FakeKeyboard(Action onCtrlC) : IKeyboardInput
    {
        public List<int> Sent { get; } = [];
        public int? WaitedForKey { get; private set; }
        public bool ReleaseSucceeds { get; init; } = true;

        public Task<bool> WaitForModifiersReleasedAsync(TimeSpan timeout, int? extraVirtualKey = null)
        {
            WaitedForKey = extraVirtualKey;
            return Task.FromResult(ReleaseSucceeds);
        }

        public void SendCtrlChord(int virtualKey)
        {
            Sent.Add(virtualKey);
            if (virtualKey == 0x43)
                onCtrlC();
        }
    }
}
