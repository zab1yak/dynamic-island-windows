using DynamicIsland.Core.Input;
using DynamicIsland.Platform.Windows.Native;

namespace DynamicIsland.Platform.Windows.Windowing;

/// <summary>A system-wide hotkey bound to a window (<c>RegisterHotKey</c>).</summary>
public sealed class GlobalHotkey(nint hwnd, int id) : IDisposable
{
    private bool _registered;

    /// <summary>Raised on the window's thread when the hotkey is pressed.</summary>
    public event EventHandler? Pressed;

    /// <summary>The currently registered gesture, if any.</summary>
    public HotkeyGesture? Gesture { get; private set; }

    /// <summary>
    /// Registers <paramref name="gesture"/>, replacing the previous one. Returns <c>false</c> when
    /// another application already owns that combination.
    /// </summary>
    public bool Register(HotkeyGesture gesture)
    {
        Unregister();
        _registered = NativeMethods.RegisterHotKey(hwnd, id, (uint)gesture.Modifiers | NativeMethods.MOD_NOREPEAT, (uint)gesture.VirtualKey);
        Gesture = _registered ? gesture : null;
        return _registered;
    }

    /// <summary>Releases the hotkey.</summary>
    public void Unregister()
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(hwnd, id);
            _registered = false;
            Gesture = null;
        }
    }

    /// <summary>Feeds a window message; returns <c>true</c> when it was this hotkey.</summary>
    public bool HandleMessage(int msg, nint wParam)
    {
        if (msg != NativeMethods.WM_HOTKEY || wParam != id)
        {
            return false;
        }

        Pressed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <inheritdoc />
    public void Dispose() => Unregister();
}
