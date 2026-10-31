using System.ComponentModel;
using System.Runtime.InteropServices;
using static ClaudeHotkey.Core.Interop.NativeMethods;

namespace ClaudeHotkey.Core.Hotkeys;

/// <param name="Gesture">The double press that was recognised, e.g. Ctrl+C+C.</param>
/// <param name="ClipboardSequence">Clipboard sequence number at the second press, before the app reacted to it.</param>
public sealed record DoublePressEventArgs(HotkeyGesture Gesture, uint ClipboardSequence);

/// <summary>
/// Low-level keyboard hook (WH_KEYBOARD_LL) that detects double presses: Ctrl+C+C and the ones
/// configured in <see cref="Gestures"/>. Keys are never swallowed, so the app still sees both
/// presses. Injected input (including our own SendInput) is ignored. Must be created on a thread
/// with a message loop (the WPF UI thread).
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private readonly DoublePressDetector _detector;
    private readonly SynchronizationContext _context;
    private readonly LowLevelKeyboardProc _callback; // kept in a field so the GC does not collect it
    private IntPtr _hook;

    public KeyboardHook(int doublePressMilliseconds)
    {
        _detector = new DoublePressDetector(doublePressMilliseconds);
        _context = SynchronizationContext.Current
            ?? throw new InvalidOperationException("KeyboardHook needs a thread with a SynchronizationContext.");
        _callback = HookCallback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx failed.");
    }

    /// <summary>Raised on the creating thread, after the hook callback has returned.</summary>
    public event EventHandler<DoublePressEventArgs>? DoublePress;

    public int DoublePressMilliseconds
    {
        get => _detector.WindowMilliseconds;
        set => _detector.WindowMilliseconds = value;
    }

    /// <summary>Double presses to detect; set from the UI thread.</summary>
    public IReadOnlyCollection<HotkeyGesture> Gestures
    {
        get => _detector.Gestures;
        set => _detector.Gestures = value;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((data.flags & LLKHF_INJECTED) == 0)
            {
                var message = (int)wParam;
                if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    if (_detector.OnKeyDown((int)data.vkCode, CurrentModifiers(), data.time) is { } gesture)
                    {
                        // Keep the callback short: Windows drops hooks that take too long.
                        var args = new DoublePressEventArgs(gesture, GetClipboardSequenceNumber());
                        _context.Post(_ => DoublePress?.Invoke(this, args), null);
                    }
                }
                else if (message is WM_KEYUP or WM_SYSKEYUP)
                {
                    _detector.OnKeyUp((int)data.vkCode);
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static HotkeyModifiers CurrentModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        if (IsKeyDown(VK_CONTROL)) modifiers |= HotkeyModifiers.Ctrl;
        if (IsKeyDown(VK_MENU)) modifiers |= HotkeyModifiers.Alt;
        if (IsKeyDown(VK_SHIFT)) modifiers |= HotkeyModifiers.Shift;
        if (IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN)) modifiers |= HotkeyModifiers.Win;
        return modifiers;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
