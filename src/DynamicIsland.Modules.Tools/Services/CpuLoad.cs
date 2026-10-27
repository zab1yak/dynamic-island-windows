using System.Runtime.InteropServices;

namespace DynamicIsland.Modules.Tools.Services;

/// <summary>Load level for the colour of the CPU bar.</summary>
public enum LoadLevel
{
    /// <summary>Below 50 %: green.</summary>
    Low,

    /// <summary>50–80 %: yellow.</summary>
    Medium,

    /// <summary>80 % and more: red.</summary>
    High,
}

/// <summary>
/// Total CPU usage from <c>GetSystemTimes</c> deltas — the same numbers Task Manager uses, without
/// performance counters. Call <see cref="Sample"/> once a second.
/// </summary>
public sealed partial class CpuLoad
{
    private ulong _idle;
    private ulong _kernel;
    private ulong _user;
    private bool _primed;

    /// <summary>Percent busy since the previous sample (0–100), or <c>null</c> on the first call.</summary>
    public double? Sample()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return null;
        }

        var result = _primed ? Percent(_idle, _kernel, _user, idle, kernel, user) : (double?)null;
        (_idle, _kernel, _user, _primed) = (idle, kernel, user, true);
        return result;
    }

    /// <summary>Busy share between two samples. Kernel time includes idle time on Windows.</summary>
    public static double Percent(ulong idle0, ulong kernel0, ulong user0, ulong idle1, ulong kernel1, ulong user1)
    {
        var idle = idle1 - idle0;
        var total = (kernel1 - kernel0) + (user1 - user0);
        return total == 0 ? 0 : Math.Clamp(100.0 * (total - idle) / total, 0, 100);
    }

    /// <summary>Colour band for <paramref name="percent"/>.</summary>
    public static LoadLevel Level(double percent) => percent switch
    {
        >= 80 => LoadLevel.High,
        >= 50 => LoadLevel.Medium,
        _ => LoadLevel.Low,
    };

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);
}
