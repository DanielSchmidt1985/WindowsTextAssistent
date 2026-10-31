using System.Runtime.InteropServices;

namespace ClaudeHotkey.Core.Windows;

/// <summary>
/// Foreground window handling: remember the source window, bring our popup to the front, and
/// place a window next to the mouse inside the monitor's work area. All coordinates are physical
/// pixels (the app is per-monitor DPI aware).
/// </summary>
public static class ForegroundWindow
{
    public static IntPtr Current => GetForegroundWindow();

    /// <summary>
    /// Brings <paramref name="hwnd"/> to the front. Windows only lets the process with the last
    /// input do that; a hook-triggered popup is not that process, so this attaches to the input
    /// queue of the current foreground thread for the duration of the call.
    /// </summary>
    public static bool Activate(IntPtr hwnd)
    {
        var foreground = GetForegroundWindow();
        if (foreground == hwnd)
            return true;

        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var ownThread = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != ownThread && AttachThreadInput(ownThread, foregroundThread, true);
        try
        {
            BringWindowToTop(hwnd);
            return SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
                AttachThreadInput(ownThread, foregroundThread, false);
        }
    }

    public static (int X, int Y) CursorPosition()
    {
        GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    /// <summary>
    /// Moves the window so its top-left corner sits just below-right of the point, shifted back
    /// inside the work area of the monitor under the point when it would stick out.
    /// </summary>
    public static void PlaceNear(IntPtr hwnd, int x, int y, int offset = 12)
    {
        if (!GetWindowRect(hwnd, out var window))
            return;
        var width = window.Right - window.Left;
        var height = window.Bottom - window.Top;

        var monitor = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        var area = info.rcWork;

        var (left, top) = Clamp(x + offset, y + offset, width, height, area.Left, area.Top, area.Right, area.Bottom, x, y, offset);
        SetWindowPos(hwnd, IntPtr.Zero, left, top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>Pure placement rule, separated for tests.</summary>
    internal static (int Left, int Top) Clamp(
        int left, int top, int width, int height,
        int areaLeft, int areaTop, int areaRight, int areaBottom,
        int pointX, int pointY, int offset)
    {
        // Not enough room to the right or below: open to the left of / above the mouse instead.
        if (left + width > areaRight)
            left = pointX - offset - width;
        if (top + height > areaBottom)
            top = pointY - offset - height;
        left = Math.Clamp(left, areaLeft, Math.Max(areaLeft, areaRight - width));
        top = Math.Clamp(top, areaTop, Math.Max(areaTop, areaBottom - height));
        return (left, top);
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
