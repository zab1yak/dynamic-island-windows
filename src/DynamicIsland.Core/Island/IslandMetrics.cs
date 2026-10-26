namespace DynamicIsland.Core.Island;

/// <summary>Visual mode of the island.</summary>
public enum IslandMode
{
    /// <summary>Hidden (fullscreen app in the foreground).</summary>
    Hidden,

    /// <summary>Small pill: status and time.</summary>
    Compact,

    /// <summary>Wider pill while something is going on (music, timer).</summary>
    LiveActivity,

    /// <summary>Full card with the current module page.</summary>
    Expanded,

    /// <summary>Expanded and kept open until clicked again.</summary>
    Pinned,

    /// <summary>Expanded as a drop zone while files are dragged toward it.</summary>
    DropTarget,
}

/// <summary>Size of the island in one mode, in DIPs at scale 1.</summary>
public readonly record struct IslandSize(double Width, double Height, double BottomRadius, double Shoulder);

/// <summary>Island sizes per mode.</summary>
public static class IslandMetrics
{
    /// <summary>Compact pill.</summary>
    public static IslandSize Compact { get; } = new(200, 34, 16, 8);

    /// <summary>Live-activity pill.</summary>
    public static IslandSize Live { get; } = new(300, 34, 16, 8);

    /// <summary>Expanded card.</summary>
    public static IslandSize Expanded { get; } = new(580, 160, 30, 14);

    /// <summary>Drop zone.</summary>
    public static IslandSize Drop { get; } = new(580, 140, 30, 14);

    /// <summary>Hidden: the pill shrinks into the screen edge.</summary>
    public static IslandSize Hidden { get; } = new(120, 0, 0, 0);

    /// <summary>Extra room around the largest size for the shoulders and the soft shadow.</summary>
    public const double WindowMargin = 28;

    /// <summary>Window width that fits every mode.</summary>
    public static double WindowWidth => Expanded.Width + (2 * (Expanded.Shoulder + WindowMargin));

    /// <summary>Window height that fits every mode.</summary>
    public static double WindowHeight => Expanded.Height + WindowMargin;

    /// <summary>Returns the size for <paramref name="mode"/>.</summary>
    public static IslandSize For(IslandMode mode) => mode switch
    {
        IslandMode.Hidden => Hidden,
        IslandMode.Compact => Compact,
        IslandMode.LiveActivity => Live,
        IslandMode.Expanded or IslandMode.Pinned => Expanded,
        IslandMode.DropTarget => Drop,
        _ => Compact,
    };

    /// <summary>Whether the mode shows the expanded card.</summary>
    public static bool IsExpanded(IslandMode mode) => mode is IslandMode.Expanded or IslandMode.Pinned or IslandMode.DropTarget;
}
