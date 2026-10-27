using DynamicIsland.Platform.Windows.Native;

namespace DynamicIsland.Platform.Windows.Windowing;

/// <summary>Extended-style and placement helpers for the island window.</summary>
public static class WindowInterop
{
    /// <summary>
    /// Makes the window a topmost tool window that never takes focus: hidden from the taskbar and
    /// Alt+Tab, and clicks on it do not activate it.
    /// </summary>
    public static void ApplyIslandStyles(nint hwnd)
    {
        var style = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        style |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST;
        style &= ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (nint)style);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
    }

    /// <summary>
    /// Temporarily allows (or forbids again) activation, so a text box in the island can receive
    /// keyboard input. When allowing, the window is brought to the foreground.
    /// </summary>
    public static void SetActivatable(nint hwnd, bool activatable)
    {
        var style = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        style = activatable ? style & ~NativeMethods.WS_EX_NOACTIVATE : style | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (nint)style);
        if (activatable)
        {
            // Allowed: the user just clicked our window, so we own the last input event.
            NativeMethods.SetForegroundWindow(hwnd);
        }
    }

    /// <summary>Moves and sizes the window in physical pixels, keeping it topmost and inactive.</summary>
    public static void Place(nint hwnd, int x, int y, int width, int height) =>
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, x, y, width, height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

    /// <summary>Re-asserts topmost order (another topmost window may have covered us).</summary>
    public static void EnsureTopmost(nint hwnd) =>
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

    /// <summary>Current cursor position in physical pixels and whether the left button is held.</summary>
    public static (int X, int Y, bool LeftButtonDown) GetCursor()
    {
        NativeMethods.GetCursorPos(out var p);
        const int VK_LBUTTON = 0x01;
        return (p.X, p.Y, (NativeMethods.GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0);
    }

    /// <summary>Asks DWM for a dark title bar (Windows 10 20H1 and later; ignored elsewhere).</summary>
    public static void UseDarkTitleBar(nint hwnd)
    {
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        var on = 1;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
    }

    /// <summary>Whether <paramref name="hwnd"/> is the foreground window.</summary>
    public static bool IsForeground(nint hwnd) => NativeMethods.GetForegroundWindow() == hwnd;
}
