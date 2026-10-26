namespace DynamicIsland.Core.Modules;

/// <summary>Publishes the module's current live activity, if any.</summary>
public interface ILiveActivitySource
{
    /// <summary>The current activity, or <c>null</c> when nothing is going on.</summary>
    LiveActivity? Current { get; }

    /// <summary>Raised on the UI thread when <see cref="Current"/> changes.</summary>
    event EventHandler? Changed;
}
