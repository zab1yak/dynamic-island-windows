using System.Runtime.InteropServices;
using DynamicIsland.Platform.Windows.Display;
using DynamicIsland.Platform.Windows.Native;

namespace DynamicIsland.Platform.Windows.Windowing;

/// <summary>
/// Detects a fullscreen app, game or presentation on the island's monitor without polling:
/// the shell's app-bar notification <c>ABN_FULLSCREENAPP</c> covers games and F11 browsers, the
/// foreground WinEvent covers switching windows, and <c>SHQueryUserNotificationState</c> covers
/// D3D exclusive mode and presentation mode.
/// </summary>
public sealed class FullscreenMonitor : IDisposable
{
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    private const int QUNS_PRESENTATION_MODE = 4;
    // Desktop, taskbars, Alt+Tab / Task View and the Start/Search hosts cover the monitor without being "fullscreen apps".
    private static readonly string[] ShellClasses =
    [
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "MultitaskingViewFrame", "XamlExplorerHostIslandWindow", "ForegroundStaging", "Windows.UI.Core.CoreWindow",
    ];

    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(250);

    private static FullscreenMonitor? s_current;

    private readonly nint _hwnd;
    private readonly Func<MonitorInfo?> _monitor;
    private readonly uint _callbackMessage;
    private readonly uint _taskbarCreatedMessage;
    private Timer? _settleTimer;
    private SynchronizationContext? _uiContext;
    private nint _hook;
    private bool _appBarRegistered;

    /// <summary>Creates the monitor for the island window <paramref name="hwnd"/>.</summary>
    /// <param name="monitor">Returns the display the island lives on.</param>
    public FullscreenMonitor(nint hwnd, Func<MonitorInfo?> monitor)
    {
        _hwnd = hwnd;
        _monitor = monitor;
        _callbackMessage = NativeMethods.RegisterWindowMessage("DynamicIsland.AppBarNotify");
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
    }

    /// <summary>Raised on the UI thread when the fullscreen state changes.</summary>
    public event EventHandler<bool>? Changed;

    /// <summary>Whether something fullscreen covers the island's monitor.</summary>
    public bool IsFullscreen { get; private set; }

    /// <summary>Starts listening. Call on the window's (UI) thread.</summary>
    public unsafe void Start()
    {
        if (s_current is not null && s_current != this)
        {
            throw new InvalidOperationException("Only one FullscreenMonitor can be active.");
        }

        if (_hook != 0)
        {
            return;
        }

        s_current = this;
        _uiContext = SynchronizationContext.Current;
        RegisterAppBar();
        _hook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND, 0,
            &OnWinEvent, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
        Evaluate();
    }

    /// <summary>Feeds a window message; returns <c>true</c> when it was the app-bar notification.</summary>
    public bool HandleMessage(int msg, nint wParam)
    {
        if ((uint)msg == _taskbarCreatedMessage)
        {
            // Explorer restarted: our app-bar registration is gone.
            _appBarRegistered = false;
            RegisterAppBar();
            Evaluate();
            return false;
        }

        if ((uint)msg != _callbackMessage)
        {
            return false;
        }

        if ((uint)wParam == NativeMethods.ABN_FULLSCREENAPP)
        {
            Evaluate();
            // The window may still be resizing into fullscreen (F11): check again once it settles.
            EvaluateLater();
        }

        return true;
    }

    private void EvaluateLater()
    {
        if (_uiContext is null)
        {
            return;
        }

        _settleTimer?.Dispose();
        _settleTimer = new Timer(_ => _uiContext.Post(_ => Evaluate(), null), null, SettleDelay, Timeout.InfiniteTimeSpan);
    }

    private void RegisterAppBar()
    {
        if (_appBarRegistered)
        {
            return;
        }

        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd, uCallbackMessage = _callbackMessage };
        _appBarRegistered = NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref data) != 0;
    }

    /// <summary>Re-checks the state (also call after display changes).</summary>
    public void Evaluate()
    {
        var now = Detect(NativeMethods.GetForegroundWindow(), _monitor());
        if (now == IsFullscreen)
        {
            return;
        }

        IsFullscreen = now;
        Changed?.Invoke(this, now);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _settleTimer?.Dispose();
        _settleTimer = null;
        if (_hook != 0)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = 0;
        }

        if (_appBarRegistered)
        {
            var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd };
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref data);
            _appBarRegistered = false;
        }

        if (s_current == this)
        {
            s_current = null;
        }
    }

    private bool Detect(nint foreground, MonitorInfo? monitor)
    {
        if (foreground == 0 || foreground == _hwnd || monitor is null
            || foreground == NativeMethods.GetShellWindow() || foreground == NativeMethods.GetDesktopWindow())
        {
            return false;
        }

        if (ShellClasses.Contains(NativeMethods.ClassNameOf(foreground), StringComparer.Ordinal))
        {
            return false;
        }

        // The notification state is global: trust it only when the foreground window is on our monitor.
        var onOurMonitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MONITOR_DEFAULTTONEAREST) == monitor.Handle;
        if (onOurMonitor && NativeMethods.SHQueryUserNotificationState(out var state) == 0
            && state is QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE)
        {
            return true;
        }

        // A maximized window covers the whole monitor when the taskbar auto-hides, but it is not
        // fullscreen: games, F11 browsers and video players set their size without maximizing.
        if (NativeMethods.IsZoomed(foreground))
        {
            return false;
        }

        return NativeMethods.GetWindowRect(foreground, out var rect) && Covers(rect, monitor.Bounds);
    }

    /// <summary>Whether <paramref name="window"/> covers all of <paramref name="monitor"/>.</summary>
    internal static bool Covers(RECT window, RECT monitor) =>
        window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            s_current?.Evaluate();
        }
        catch (Exception)
        {
            // Never let an exception cross the native boundary; the next event re-evaluates.
        }
    }
}
