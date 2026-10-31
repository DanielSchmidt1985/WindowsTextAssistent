using System.ComponentModel;
using System.Runtime.InteropServices;
using static ClaudeHotkey.Core.Interop.NativeMethods;

namespace ClaudeHotkey.Core.Hotkeys;

/// <param name="ClipboardSequence">Clipboard sequence number at the second Ctrl+C, before the app copied.</param>
public sealed record DoubleCopyEventArgs(uint ClipboardSequence);

/// <summary>
/// Low-level keyboard hook (WH_KEYBOARD_LL) that detects Ctrl+C+C. Keys are never swallowed, so
/// copying keeps working normally. Injected input (including our own SendInput) is ignored.
/// Must be created on a thread with a message loop (the WPF UI thread).
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private readonly DoubleCopyDetector _detector;
    private readonly SynchronizationContext _context;
    private readonly LowLevelKeyboardProc _callback; // kept in a field so the GC does not collect it
    private IntPtr _hook;

    public KeyboardHook(int doubleCopyMilliseconds)
    {
        _detector = new DoubleCopyDetector(doubleCopyMilliseconds);
        _context = SynchronizationContext.Current
            ?? throw new InvalidOperationException("KeyboardHook needs a thread with a SynchronizationContext.");
        _callback = HookCallback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx failed.");
    }

    /// <summary>Raised on the creating thread, after the hook callback has returned.</summary>
    public event EventHandler<DoubleCopyEventArgs>? DoubleCopy;

    public int DoubleCopyMilliseconds
    {
        get => _detector.WindowMilliseconds;
        set => _detector.WindowMilliseconds = value;
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
                    if (_detector.OnKeyDown((int)data.vkCode, IsKeyDown(VK_CONTROL), IsKeyDown(VK_MENU), data.time))
                    {
                        // Keep the callback short: Windows drops hooks that take too long.
                        var args = new DoubleCopyEventArgs(GetClipboardSequenceNumber());
                        _context.Post(_ => DoubleCopy?.Invoke(this, args), null);
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

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
