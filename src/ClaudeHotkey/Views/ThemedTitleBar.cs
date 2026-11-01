using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ClaudeHotkey.Views;

/// <summary>
/// Title bar of normal windows in the active theme (immersive dark mode, Windows 10 20H1 and
/// later). Called on SourceInitialized and again by ThemeManager when the theme switches.
/// </summary>
public static class ThemedTitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var dark = Themes.ThemeManager.IsLight ? 0 : 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
