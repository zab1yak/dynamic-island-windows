using DynamicIsland.Core.Settings;

namespace DynamicIsland.Core.Island;

/// <summary>Monitor-relative placement in physical pixels, including the host's shadow margins.</summary>
public static class IslandPlacement
{
    /// <summary>Returns the host's left coordinate without allowing its edges past the monitor.</summary>
    public static int Left(int monitorLeft, int monitorWidth, int windowWidth, IslandPosition position, int insetPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(monitorWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowWidth);
        var available = Math.Max(0, monitorWidth - windowWidth);
        var inset = Math.Clamp(insetPixels, 0, available / 2);
        var offset = position switch
        {
            IslandPosition.Left => inset,
            IslandPosition.Right => available - inset,
            _ => available / 2,
        };
        return monitorLeft + offset;
    }
}
