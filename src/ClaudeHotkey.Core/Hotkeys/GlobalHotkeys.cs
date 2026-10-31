using System.Windows.Interop;
using static ClaudeHotkey.Core.Interop.NativeMethods;

namespace ClaudeHotkey.Core.Hotkeys;

/// <summary>
/// System-wide hotkeys via RegisterHotKey, received by a hidden message-only window.
/// Must be used on a thread with a WPF dispatcher.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const int HWND_MESSAGE = -3;

    private readonly HwndSource _window;
    private readonly Dictionary<int, HotkeyGesture> _registered = [];
    private int _nextId = 1;

    public GlobalHotkeys()
    {
        _window = new HwndSource(new HwndSourceParameters("ClaudeHotkey.Hotkeys") { ParentWindow = new IntPtr(HWND_MESSAGE) });
        _window.AddHook(WndProc);
    }

    /// <summary>Raised on the UI thread with the gesture that was pressed.</summary>
    public event EventHandler<HotkeyGesture>? Pressed;

    /// <summary>Registers the gesture. False when another program already uses it.</summary>
    public bool TryRegister(HotkeyGesture gesture)
    {
        var id = _nextId++;
        if (!RegisterHotKey(_window.Handle, id, (uint)gesture.Modifiers | MOD_NOREPEAT, (uint)gesture.VirtualKey))
            return false;
        _registered[id] = gesture;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered.Keys)
            UnregisterHotKey(_window.Handle, id);
        _registered.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _registered.TryGetValue(wParam.ToInt32(), out var gesture))
        {
            handled = true;
            Pressed?.Invoke(this, gesture);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }
}
