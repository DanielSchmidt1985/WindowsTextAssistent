using ClaudeHotkey.Core.Clipboard;
using ClaudeHotkey.Core.Input;
using ClaudeHotkey.Core.Localization;

namespace ClaudeHotkey.Core.Windows;

/// <summary>Seam over <see cref="ForegroundWindow"/> so the replace flow can be tested.</summary>
public interface IWindowActivator
{
    bool Exists(IntPtr hwnd);
    bool Activate(IntPtr hwnd);
    Task<bool> WaitUntilForegroundAsync(IntPtr hwnd, TimeSpan timeout);
}

public sealed class SystemWindowActivator : IWindowActivator
{
    public bool Exists(IntPtr hwnd) => ForegroundWindow.Exists(hwnd);
    public bool Activate(IntPtr hwnd) => ForegroundWindow.Activate(hwnd);
    public Task<bool> WaitUntilForegroundAsync(IntPtr hwnd, TimeSpan timeout) => ForegroundWindow.WaitUntilForegroundAsync(hwnd, timeout);
}

/// <param name="Message">Message (UI language) when the text could not be pasted; null on success.</param>
public sealed record ReplaceResult(bool Success, string? Message);

/// <summary>
/// Puts the result into the source window: result into the clipboard, source window back to the
/// front, Ctrl+V, then the previous clipboard content back.
/// </summary>
public sealed class TextReplacer(IClipboard clipboard, IKeyboardInput keyboard, IWindowActivator windows, Func<Task>? delay = null)
{
    private static readonly TimeSpan ActivateTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ModifierTimeout = TimeSpan.FromSeconds(2);
    private const int VirtualKeyV = 0x56;

    public static string WindowGoneMessage => Lang.T(
        "Das Zielfenster ist nicht mehr geöffnet. Das Ergebnis lässt sich nur kopieren.",
        "The target window is no longer open. The result can only be copied.");
    public static string ActivateFailedMessage => Lang.T(
        "Das Zielfenster ließ sich nicht aktivieren. Das Ergebnis liegt in der Zwischenablage.",
        "The target window could not be activated. The result is in the clipboard.");
    public static string KeysHeldMessage => Lang.T(
        "Tasten wurden nicht losgelassen. Das Ergebnis liegt in der Zwischenablage.",
        "Keys were not released. The result is in the clipboard.");

    /// <param name="pasteDelay">
    /// How long the target app gets to read the clipboard after Ctrl+V before the old content is
    /// put back. Too short and slow apps (Word, Teams) paste the old content.
    /// </param>
    public async Task<ReplaceResult> ReplaceAsync(IntPtr source, string text, TimeSpan pasteDelay)
    {
        if (!windows.Exists(source))
            return new ReplaceResult(false, WindowGoneMessage);

        var backup = clipboard.Backup();
        clipboard.SetText(text);
        var ourSequence = clipboard.SequenceNumber;

        windows.Activate(source);
        if (!await windows.WaitUntilForegroundAsync(source, ActivateTimeout))
            return new ReplaceResult(false, ActivateFailedMessage); // keep the result for manual pasting

        // E.g. Ctrl from Ctrl+Enter in the popup: Ctrl+V must not arrive as Ctrl+Shift+V or similar.
        if (!await keyboard.WaitForModifiersReleasedAsync(ModifierTimeout))
            return new ReplaceResult(false, KeysHeldMessage);

        keyboard.SendCtrlChord(VirtualKeyV);
        await (delay?.Invoke() ?? Task.Delay(pasteDelay));

        // Only put the old content back if nobody copied something new in the meantime.
        if (clipboard.SequenceNumber == ourSequence)
            clipboard.Restore(backup);
        return new ReplaceResult(true, null);
    }
}
