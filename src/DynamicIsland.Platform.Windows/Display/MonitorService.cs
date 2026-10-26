using System.Runtime.InteropServices;
using DynamicIsland.Platform.Windows.Native;

namespace DynamicIsland.Platform.Windows.Display;

/// <summary>A display as Windows reports it, in physical pixels.</summary>
/// <param name="Handle">HMONITOR (valid until the display configuration changes).</param>
/// <param name="DeviceName">Stable-ish name such as <c>\\.\DISPLAY1</c>, stored in settings.</param>
/// <param name="Bounds">Full monitor rectangle.</param>
/// <param name="WorkArea">Rectangle without the taskbar.</param>
/// <param name="IsPrimary">Whether this is the primary display.</param>
/// <param name="Dpi">Effective DPI (96 = 100 %).</param>
public sealed record MonitorInfo(nint Handle, string DeviceName, RECT Bounds, RECT WorkArea, bool IsPrimary, uint Dpi)
{
    /// <summary>Scale factor relative to 96 DPI.</summary>
    public double Scale => Dpi / 96.0;
}

/// <summary>Enumerates displays. Per-Monitor DPI v2 is assumed (set in the app manifest).</summary>
public static class MonitorService
{
    private const int MDT_EFFECTIVE_DPI = 0;

    /// <summary>All connected displays; the primary one first.</summary>
    public static unsafe IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var handles = new List<nint>();
        var gc = GCHandle.Alloc(handles);
        try
        {
            NativeMethods.EnumDisplayMonitors(0, 0, &Collect, GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }

        return handles.Select(Describe).OfType<MonitorInfo>().OrderByDescending(m => m.IsPrimary).ToList();
    }

    /// <summary>The display named <paramref name="deviceName"/>, or the primary display when it is gone.</summary>
    public static MonitorInfo? Resolve(string? deviceName)
    {
        var monitors = GetMonitors();
        return monitors.FirstOrDefault(m => string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            ?? monitors.FirstOrDefault(m => m.IsPrimary)
            ?? (monitors.Count > 0 ? monitors[0] : null);
    }

    /// <summary>The display that contains most of <paramref name="hwnd"/>.</summary>
    public static MonitorInfo? FromWindow(nint hwnd) =>
        Describe(NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST));

    private static unsafe MonitorInfo? Describe(nint handle)
    {
        if (handle == 0)
        {
            return null;
        }

        var info = new MONITORINFOEXW { cbSize = sizeof(MONITORINFOEXW) };
        if (!NativeMethods.GetMonitorInfo(handle, &info))
        {
            return null;
        }

        var name = new string(info.szDevice);
        var nul = name.IndexOf('\0', StringComparison.Ordinal);
        if (nul >= 0)
        {
            name = name[..nul];
        }

        var dpi = NativeMethods.GetDpiForMonitor(handle, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? dpiX : 96u;
        return new MonitorInfo(handle, name, info.rcMonitor, info.rcWork, (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0, dpi);
    }

    [UnmanagedCallersOnly]
    private static unsafe int Collect(nint monitor, nint hdc, RECT* rect, nint data)
    {
        var list = (List<nint>)GCHandle.FromIntPtr(data).Target!;
        list.Add(monitor);
        return 1;
    }
}
