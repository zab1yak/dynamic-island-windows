namespace DynamicIsland.Core.Modules;

/// <summary>Kind of a live activity; used for default priorities and compact layout.</summary>
public enum ActivityKind
{
    /// <summary>Media playback.</summary>
    Media,

    /// <summary>A running timer (Pomodoro).</summary>
    Timer,

    /// <summary>A reminder (water).</summary>
    Reminder,

    /// <summary>A file operation (Shelf copy).</summary>
    Transfer,
}

/// <summary>Something worth showing in the compact island while it is happening.</summary>
/// <param name="ModuleId">Owner module id.</param>
/// <param name="Kind">Activity kind.</param>
/// <param name="Priority">Higher wins the main slot (see <c>LiveActivityPriority</c>).</param>
/// <param name="ViewModel">View model rendered in the live-activity pill.</param>
/// <param name="StartedAt">When the activity started; newer wins on equal priority.</param>
/// <param name="IsAlert">A short attention-grabbing event (timer finished), shown first for <paramref name="Ttl"/>.</param>
/// <param name="Ttl">How long an alert stays; ignored for non-alerts.</param>
public sealed record LiveActivity(
    string ModuleId,
    ActivityKind Kind,
    int Priority,
    object ViewModel,
    DateTimeOffset StartedAt,
    bool IsAlert = false,
    TimeSpan? Ttl = null);

/// <summary>Default priorities of live activities.</summary>
public static class LiveActivityPriority
{
    /// <summary>A timer in its last minute.</summary>
    public const int TimerEnding = 90;

    /// <summary>Media is playing.</summary>
    public const int MediaPlaying = 70;

    /// <summary>A timer is running.</summary>
    public const int TimerRunning = 60;

    /// <summary>A file transfer is running.</summary>
    public const int Transfer = 50;

    /// <summary>Media was paused recently.</summary>
    public const int MediaPaused = 20;
}
