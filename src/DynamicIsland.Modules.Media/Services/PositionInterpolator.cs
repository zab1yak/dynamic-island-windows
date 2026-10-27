namespace DynamicIsland.Modules.Media.Services;

/// <summary>
/// Apps report the playback position only now and then (on seek, pause, track change). Between
/// reports the position is extrapolated locally from the last report and the playback rate.
/// </summary>
public static class PositionInterpolator
{
    /// <summary>Position of <paramref name="snapshot"/> at <paramref name="now"/>, clamped to the track.</summary>
    public static TimeSpan At(MediaSnapshot snapshot, DateTimeOffset now)
    {
        var position = snapshot.Position;
        if (snapshot.IsPlaying && snapshot.PositionUpdatedAt != default)
        {
            var elapsed = now - snapshot.PositionUpdatedAt;
            if (elapsed > TimeSpan.Zero)
            {
                var rate = snapshot.PlaybackRate > 0 && double.IsFinite(snapshot.PlaybackRate) ? snapshot.PlaybackRate : 1.0;
                position += elapsed * rate;
            }
        }

        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return snapshot.Duration > TimeSpan.Zero && position > snapshot.Duration ? snapshot.Duration : position;
    }

    /// <summary>Progress 0–1, or 0 when the duration is unknown.</summary>
    public static double Progress(MediaSnapshot snapshot, DateTimeOffset now) =>
        snapshot.Duration > TimeSpan.Zero ? At(snapshot, now) / snapshot.Duration : 0;

    /// <summary>Formats a position as <c>m:ss</c> or <c>h:mm:ss</c>.</summary>
    /// <summary>Time left as «-2:48», as on the reference photos; empty when the length is unknown.</summary>
    public static string FormatRemaining(MediaSnapshot snapshot, DateTimeOffset now) =>
        snapshot.Duration > TimeSpan.Zero ? "-" + Format(snapshot.Duration - At(snapshot, now)) : string.Empty;

    public static string Format(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes}:{value.Seconds:00}";
    }
}
