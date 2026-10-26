namespace DynamicIsland.Core.Modules;

/// <summary>What the compact island shows: the main activity and one secondary hint.</summary>
public sealed record ArbiterResult(LiveActivity? Primary, LiveActivity? Secondary)
{
    /// <summary>No activity.</summary>
    public static ArbiterResult Empty { get; } = new(null, null);
}

/// <summary>
/// Picks the most important live activity. Alerts (a finished timer) win; otherwise the highest
/// priority, newer first on ties. The primary may switch at most once per
/// <see cref="Hysteresis"/> (except to an alert), so the pill does not flicker between sources.
/// </summary>
public sealed class LiveActivityArbiter(TimeProvider time)
{
    /// <summary>Minimum time between two changes of the primary activity.</summary>
    public static readonly TimeSpan Hysteresis = TimeSpan.FromSeconds(1.5);

    private DateTimeOffset _lastSwitch = DateTimeOffset.MinValue;

    /// <summary>
    /// A better activity is waiting for the hysteresis to pass; call <see cref="Update"/> again
    /// after <see cref="Hysteresis"/>.
    /// </summary>
    public bool IsSwitchPending { get; private set; }

    /// <summary>Last result.</summary>
    public ArbiterResult Current { get; private set; } = ArbiterResult.Empty;

    /// <summary>Re-evaluates with the current activities; returns <c>true</c> when the result changed.</summary>
    public bool Update(IEnumerable<LiveActivity?> activities)
    {
        var ranked = activities
            .OfType<LiveActivity>()
            .OrderByDescending(a => a.IsAlert)
            .ThenByDescending(a => a.Priority)
            .ThenByDescending(a => a.StartedAt)
            .ToList();

        var best = ranked.FirstOrDefault();
        var primary = best;
        IsSwitchPending = false;
        var now = time.GetUtcNow();
        var currentStillThere = Current.Primary is { } current && ranked.Any(a => a.ModuleId == current.ModuleId);
        if (best is not null && Current.Primary is { } previous && best.ModuleId != previous.ModuleId
            && !best.IsAlert && currentStillThere && now - _lastSwitch < Hysteresis)
        {
            // Too soon to switch: keep the previous module (with its fresh data) in front.
            primary = ranked.First(a => a.ModuleId == previous.ModuleId);
            IsSwitchPending = true;
        }

        var secondary = ranked.FirstOrDefault(a => primary is null || a.ModuleId != primary.ModuleId);
        var result = new ArbiterResult(primary, secondary);
        if (result == Current)
        {
            return false;
        }

        if (result.Primary?.ModuleId != Current.Primary?.ModuleId)
        {
            _lastSwitch = now;
        }

        Current = result;
        return true;
    }
}
