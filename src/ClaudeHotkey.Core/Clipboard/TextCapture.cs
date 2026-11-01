using ClaudeHotkey.Core.Input;
using ClaudeHotkey.Core.Localization;

namespace ClaudeHotkey.Core.Clipboard;

/// <param name="Text">The selected text, or null when none could be captured.</param>
/// <param name="Message">Message for the user (UI language) when <paramref name="Text"/> is null.</param>
public sealed record CaptureResult(string? Text, string? Message)
{
    public bool Success => Text is not null;
}

/// <summary>Seam over the clipboard so <see cref="TextCapture"/> can be tested without the real one.</summary>
public interface IClipboard
{
    uint SequenceNumber { get; }
    string? GetText();
    void SetText(string text);
    ClipboardSnapshot Backup();
    void Restore(ClipboardSnapshot snapshot);
    Task<bool> WaitForChangeAsync(uint sequence, TimeSpan timeout);
}

public sealed class SystemClipboard(ClipboardService service) : IClipboard
{
    public uint SequenceNumber => ClipboardService.SequenceNumber;
    public string? GetText() => service.GetText();
    public void SetText(string text) => service.SetText(text);
    public ClipboardSnapshot Backup() => service.Backup();
    public void Restore(ClipboardSnapshot snapshot) => service.Restore(snapshot);
    public Task<bool> WaitForChangeAsync(uint sequence, TimeSpan timeout) => ClipboardService.WaitForChangeAsync(sequence, timeout);
}

/// <summary>Gets the selected text of the foreground window, for both triggers.</summary>
public sealed class TextCapture(IClipboard clipboard, IKeyboardInput keyboard)
{
    public static readonly TimeSpan CopyTimeout = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan ModifierTimeout = TimeSpan.FromSeconds(2);
    private const int VirtualKeyC = 0x43;

    public static string NoTextMessage => Lang.T(
        "Kein markierter Text gefunden. Das Programm gibt den Text eventuell nicht per Strg+C heraus.",
        "No selected text found. The program may not hand out its text via Ctrl+C.");
    public static string KeysHeldMessage => Lang.T("Tasten des Hotkeys wurden nicht losgelassen.", "The hotkey keys were not released.");

    /// <summary>
    /// After Ctrl+C+C: the user copied on purpose, so the text stays in the clipboard (as with
    /// DeepL). Waits for the app to finish its copy, then reads the text.
    /// </summary>
    public async Task<CaptureResult> AfterUserCopyAsync(uint sequenceAtSecondCopy)
    {
        await clipboard.WaitForChangeAsync(sequenceAtSecondCopy, CopyTimeout);
        // Even without a change the first Ctrl+C has already put the text there.
        return FromText(clipboard.GetText());
    }

    /// <summary>
    /// After the second hotkey: sends Ctrl+C itself and puts the previous clipboard content back
    /// afterwards, so the user's clipboard is unchanged.
    /// </summary>
    /// <param name="hotkeyVirtualKey">Key of the hotkey; it has to be released before Ctrl+C is sent.</param>
    public async Task<CaptureResult> BySendingCopyAsync(int hotkeyVirtualKey)
    {
        // With Alt still held, Ctrl+C would arrive as Ctrl+Alt+C.
        if (!await keyboard.WaitForModifiersReleasedAsync(ModifierTimeout, hotkeyVirtualKey))
            return new CaptureResult(null, KeysHeldMessage);

        var backup = clipboard.Backup();
        var sequence = clipboard.SequenceNumber;
        keyboard.SendCtrlChord(VirtualKeyC);
        if (!await clipboard.WaitForChangeAsync(sequence, CopyTimeout))
            return new CaptureResult(null, NoTextMessage); // nothing changed, nothing to restore

        try
        {
            return FromText(clipboard.GetText());
        }
        finally
        {
            clipboard.Restore(backup);
        }
    }

    private static CaptureResult FromText(string? text) =>
        string.IsNullOrEmpty(text) ? new CaptureResult(null, NoTextMessage) : new CaptureResult(text, null);
}
