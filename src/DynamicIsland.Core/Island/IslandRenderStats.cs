namespace DynamicIsland.Core.Island;

/// <summary>Render statistics of the island itself (published by the window, shown on the Tools page).</summary>
public sealed class IslandRenderStats
{
    /// <summary>Average frames per second of the last finished animation, or <c>null</c> before the first one.</summary>
    public double? LastAnimationFps { get; private set; }

    /// <summary>Longest frame of the last animation, in milliseconds.</summary>
    public double LongestFrameMs { get; private set; }

    /// <summary>Raised on the UI thread after an animation finished.</summary>
    public event EventHandler? Changed;

    /// <summary>Records a finished animation.</summary>
    public void Report(int frames, TimeSpan duration, TimeSpan longestFrame)
    {
        if (frames < 2 || duration <= TimeSpan.Zero)
        {
            return;
        }

        LastAnimationFps = frames / duration.TotalSeconds;
        LongestFrameMs = longestFrame.TotalMilliseconds;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
