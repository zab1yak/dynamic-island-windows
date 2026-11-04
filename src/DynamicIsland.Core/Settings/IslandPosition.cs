namespace DynamicIsland.Core.Settings;

/// <summary>Horizontal anchor of the island on its selected monitor.</summary>
public enum IslandPosition
{
    /// <summary>Centred on the monitor (the original placement).</summary>
    Center,

    /// <summary>Near the monitor's left edge.</summary>
    Left,

    /// <summary>Near the monitor's right edge.</summary>
    Right,
}
