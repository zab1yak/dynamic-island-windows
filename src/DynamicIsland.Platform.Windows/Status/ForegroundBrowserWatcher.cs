using System.Diagnostics;
using System.Runtime.InteropServices;
using DynamicIsland.Core.Status;
using DynamicIsland.Platform.Windows.Native;

namespace DynamicIsland.Platform.Windows.Status;

/// <summary>
/// Tab title of the browser in the foreground. Event-driven: one WinEvent hook for foreground
/// changes, and — only while a browser is in front — a second hook for title changes limited to
/// that browser's process (switching tabs renames the window). Must be started on the UI thread.
/// </summary>
public sealed unsafe class ForegroundBrowserWatcher : IDisposable
{
    private const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    private const int OBJID_WINDOW = 0;

    private static ForegroundBrowserWatcher? s_current;

    private nint _foregroundHook;
    private nint _nameHook;
    private uint _browserPid;
    private nint _browserWindow;
    private string? _browserProcess;

    /// <summary>Current tab title, or <c>null</c> when no browser is in the foreground.</summary>
    public string? TabTitle { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="TabTitle"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Starts watching.</summary>
    public void Start()
    {
        if (_foregroundHook != 0)
        {
            return;
        }

        s_current = this;
        _foregroundHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND, 0,
            &OnForeground, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
        Evaluate(NativeMethods.GetForegroundWindow());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Unhook(ref _nameHook);
        Unhook(ref _foregroundHook);
        if (s_current == this)
        {
            s_current = null;
        }
    }

    private void Evaluate(nint hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        string? processName = null;
        if (pid != 0 && pid != (uint)Environment.ProcessId)
        {
            try
            {
                using var process = Process.GetProcessById((int)pid);
                processName = process.ProcessName;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // The process ended in the meantime.
            }
        }
        else if (pid == (uint)Environment.ProcessId)
        {
            // Our own island or settings window: keep showing the browser that was in front.
            return;
        }

        if (!BrowserTitles.IsBrowser(processName))
        {
            _browserWindow = 0;
            Unhook(ref _nameHook);
            _browserPid = 0;
            Publish(null);
            return;
        }

        _browserWindow = hwnd;
        _browserProcess = processName;
        if (_browserPid != pid)
        {
            Unhook(ref _nameHook);
            _browserPid = pid;
            _nameHook = NativeMethods.SetWinEventHook(EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_NAMECHANGE, 0, &OnNameChange, pid, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        }

        Publish(BrowserTitles.TabTitle(processName, ReadTitle(hwnd)));
    }

    private void Publish(string? title)
    {
        if (title == TabTitle)
        {
            return;
        }

        TabTitle = title;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string ReadTitle(nint hwnd)
    {
        var buffer = stackalloc char[512];
        var length = NativeMethods.GetWindowText(hwnd, buffer, 512);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private static void Unhook(ref nint hook)
    {
        if (hook != 0)
        {
            NativeMethods.UnhookWinEvent(hook);
            hook = 0;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnForeground(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            s_current?.Evaluate(hwnd);
        }
        catch (Exception)
        {
            // Never let an exception cross the native boundary.
        }
    }

    [UnmanagedCallersOnly]
    private static void OnNameChange(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (s_current is { } watcher && idObject == OBJID_WINDOW && idChild == 0 && hwnd == watcher._browserWindow)
            {
                watcher.Publish(BrowserTitles.TabTitle(watcher._browserProcess, ReadTitle(hwnd)));
            }
        }
        catch (Exception)
        {
            // Never let an exception cross the native boundary.
        }
    }
}
