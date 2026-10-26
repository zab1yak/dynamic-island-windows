namespace DynamicIsland.Core.Status;

/// <summary>How the battery indicator should look.</summary>
public enum BatteryAppearance
{
    /// <summary>No battery (desktop PC): the indicator is hidden.</summary>
    Hidden,

    /// <summary>Normal level, white.</summary>
    Normal,

    /// <summary>Plugged in and charging: green with a bolt.</summary>
    Charging,

    /// <summary>Below the low threshold: red.</summary>
    Low,

    /// <summary>Energy saver is on: yellow.</summary>
    Saver,
}

/// <summary>A snapshot of the system battery.</summary>
/// <param name="IsPresent">Whether the device has a battery.</param>
/// <param name="Percent">Charge level 0–100.</param>
/// <param name="IsCharging">Plugged in and charging (or full on AC).</param>
/// <param name="IsSaverOn">Energy saver / battery saver is active.</param>
public sealed record BatteryStatus(bool IsPresent, int Percent, bool IsCharging, bool IsSaverOn)
{
    /// <summary>Below this level the indicator turns red.</summary>
    public const int LowThreshold = 20;

    /// <summary>State used when no battery information is available.</summary>
    public static BatteryStatus None { get; } = new(false, 0, false, false);

    /// <summary>Visual state; charging wins over saver, saver wins over low.</summary>
    public BatteryAppearance Appearance => !IsPresent
        ? BatteryAppearance.Hidden
        : IsCharging
            ? BatteryAppearance.Charging
            : IsSaverOn
                ? BatteryAppearance.Saver
                : Percent < LowThreshold
                    ? BatteryAppearance.Low
                    : BatteryAppearance.Normal;
}

/// <summary>Source of battery updates.</summary>
public interface IBatteryService
{
    /// <summary>Latest status.</summary>
    BatteryStatus Current { get; }

    /// <summary>Raised on the UI thread when the status changes.</summary>
    event EventHandler? Changed;

    /// <summary>Starts listening for system events.</summary>
    void Start();
}
