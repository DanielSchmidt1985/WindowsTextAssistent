namespace ClaudeHotkey.Core.Hotkeys;

/// <summary>
/// Recognises double presses such as Ctrl+C+C or Ctrl+X+X: modifiers held, key pressed twice
/// within a time window. Pure state machine without Win32, fed by <see cref="KeyboardHook"/>.
/// Modifiers must match exactly, auto-repeat (key held down) does not count as a second press,
/// and any other key in between starts over.
/// </summary>
public sealed class DoublePressDetector(int windowMilliseconds)
{
    /// <summary>
    /// A key-down of a key we think is still held counts as a new press after this long. Guards
    /// against a lost key-up (e.g. while the UAC desktop was shown); Windows' longest
    /// auto-repeat delay is 1 s.
    /// </summary>
    private const uint StaleKeyMilliseconds = 1100;

    private readonly Dictionary<int, uint> _heldSince = []; // key → time of its last key-down event
    private HashSet<HotkeyGesture> _gestures = [HotkeyGesture.DoubleCopy];
    private (HotkeyGesture Gesture, uint Time)? _first;

    public int WindowMilliseconds { get; set; } = windowMilliseconds;

    /// <summary>The double-press gestures to look for; single-press gestures in the list are ignored.</summary>
    public IReadOnlyCollection<HotkeyGesture> Gestures
    {
        get => _gestures;
        set
        {
            _gestures = value.Where(g => g.IsDoublePress).ToHashSet();
            _first = null;
        }
    }

    /// <param name="time">Event time in milliseconds (the hook's timestamp; may wrap around).</param>
    /// <returns>The gesture on its second press within the window, otherwise null.</returns>
    public HotkeyGesture? OnKeyDown(int virtualKey, HotkeyModifiers modifiers, uint time)
    {
        if (IsModifier(virtualKey))
            return null;

        var isRepeat = _heldSince.TryGetValue(virtualKey, out var last) && unchecked(time - last) < StaleKeyMilliseconds;
        _heldSince[virtualKey] = time;
        if (isRepeat)
            return null;

        var candidate = new HotkeyGesture(modifiers, virtualKey, IsDoublePress: true);
        if (!_gestures.Contains(candidate))
        {
            _first = null;
            return null;
        }
        if (_first is { } first && first.Gesture == candidate && unchecked(time - first.Time) <= (uint)WindowMilliseconds)
        {
            _first = null;
            return candidate;
        }
        _first = (candidate, time);
        return null;
    }

    public void OnKeyUp(int virtualKey) => _heldSince.Remove(virtualKey);

    private static bool IsModifier(int virtualKey) =>
        virtualKey is 0x10 or 0x11 or 0x12 // Shift, Ctrl, Alt
            or >= 0xA0 and <= 0xA5        // left/right Shift, Ctrl, Alt
            or 0x5B or 0x5C;              // Windows keys
}
