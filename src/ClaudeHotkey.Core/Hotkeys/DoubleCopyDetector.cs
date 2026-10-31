namespace ClaudeHotkey.Core.Hotkeys;

/// <summary>
/// Recognises Ctrl+C pressed twice within a time window. Pure state machine without Win32, fed
/// by <see cref="KeyboardHook"/>. Auto-repeat (C held down) does not count as a second press, and
/// any other key in between starts over.
/// </summary>
public sealed class DoubleCopyDetector(int windowMilliseconds)
{
    private const int VirtualKeyC = 0x43;

    private bool _cIsDown;
    private uint? _lastCopyTime;

    public int WindowMilliseconds { get; set; } = windowMilliseconds;

    /// <param name="time">Event time in milliseconds (the hook's timestamp; may wrap around).</param>
    /// <returns>True on the second Ctrl+C within the window.</returns>
    public bool OnKeyDown(int virtualKey, bool ctrlDown, bool altDown, uint time)
    {
        if (IsModifier(virtualKey))
            return false;

        if (virtualKey != VirtualKeyC || !ctrlDown || altDown)
        {
            _lastCopyTime = null;
            if (virtualKey == VirtualKeyC)
                _cIsDown = true;
            return false;
        }

        if (_cIsDown)
            return false; // auto-repeat
        _cIsDown = true;

        if (_lastCopyTime is { } last && unchecked(time - last) <= (uint)WindowMilliseconds)
        {
            _lastCopyTime = null;
            return true;
        }
        _lastCopyTime = time;
        return false;
    }

    public void OnKeyUp(int virtualKey)
    {
        if (virtualKey == VirtualKeyC)
            _cIsDown = false;
    }

    private static bool IsModifier(int virtualKey) =>
        virtualKey is 0x10 or 0x11 or 0x12 // Shift, Ctrl, Alt
            or >= 0xA0 and <= 0xA5        // left/right Shift, Ctrl, Alt
            or 0x5B or 0x5C;              // Windows keys
}
