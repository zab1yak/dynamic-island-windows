namespace DynamicIsland.Modules.Media.Services;

/// <summary>Result of trying to resume the saved track.</summary>
public enum RestoreOutcome
{
    /// <summary>Playing and seeked to the saved position.</summary>
    Resumed,

    /// <summary>Playing, but the app started another track or cannot seek.</summary>
    ResumedWithoutPosition,

    /// <summary>The app could not be started automatically.</summary>
    CannotLaunch,

    /// <summary>The app is a browser: it cannot reopen the tab by itself.</summary>
    Browser,

    /// <summary>The app started but no media session appeared in time.</summary>
    TimedOut,
}

/// <summary>Starts a media app from its AppUserModelID.</summary>
public interface IAppLauncher
{
    /// <summary>Tries to start the app; returns <c>false</c> when no safe way is known.</summary>
    bool TryLaunch(string appId);
}

/// <summary>
/// Resumes the saved track after a reboot: if the app's session exists, play and seek; if not,
/// start the app, wait for its session (up to <see cref="SessionTimeout"/>), then play and seek.
/// Nothing ever starts by itself: this runs only when the user presses Play.
/// </summary>
public sealed class RestoreFlow(IMediaSessionSource source, IAppLauncher launcher, TimeProvider time)
{
    /// <summary>How long to wait for the app's media session.</summary>
    public static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Pause after the session appears, so the app can load its track before Play.</summary>
    public TimeSpan SettleDelay { get; init; } = TimeSpan.FromMilliseconds(800);

    private static readonly string[] BrowserIds = ["chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "yandex", "browser"];

    /// <summary>Whether <paramref name="appId"/> belongs to a web browser.</summary>
    public static bool IsBrowser(string appId)
    {
        var id = appId.ToLowerInvariant();
        return BrowserIds.Any(b => id == b || id.StartsWith(b + ".exe", StringComparison.Ordinal) || id.Contains(b + ".exe", StringComparison.Ordinal)
            || id.StartsWith("microsoft.microsoftedge", StringComparison.Ordinal) || id.Contains("firefox", StringComparison.Ordinal)
            // Yandex Browser registers as Yandex.<random id>; Yandex Music is a separate app.
            || (id.StartsWith("yandex.", StringComparison.Ordinal) && !id.Contains("music", StringComparison.Ordinal) && !id.Contains("disk", StringComparison.Ordinal)));
    }

    /// <summary>Runs the flow for <paramref name="saved"/>.</summary>
    public async Task<RestoreOutcome> ResumeAsync(MediaSnapshot saved, CancellationToken cancellationToken = default)
    {
        var session = Find(saved.AppId);
        if (session is null)
        {
            if (IsBrowser(saved.AppId))
            {
                return RestoreOutcome.Browser;
            }

            if (!launcher.TryLaunch(saved.AppId))
            {
                return RestoreOutcome.CannotLaunch;
            }

            session = await WaitForSessionAsync(saved.AppId, cancellationToken);
            if (session is null)
            {
                return RestoreOutcome.TimedOut;
            }
        }

        if (!await source.PlayAsync(saved.AppId))
        {
            return RestoreOutcome.ResumedWithoutPosition;
        }

        // Seek only if the app reopened the same track; otherwise we would jump inside a different song.
        session = Find(saved.AppId) ?? session;
        var sameTrack = string.Equals(session.Title, saved.Title, StringComparison.Ordinal) || string.IsNullOrEmpty(session.Title);
        if (sameTrack && session.CanSeek && saved.Position > TimeSpan.FromSeconds(1)
            && await source.SeekAsync(saved.AppId, saved.Position))
        {
            return RestoreOutcome.Resumed;
        }

        return RestoreOutcome.ResumedWithoutPosition;
    }

    private MediaSnapshot? Find(string appId) => source.Sessions.FirstOrDefault(s => s.AppId == appId);

    private async Task<MediaSnapshot?> WaitForSessionAsync(string appId, CancellationToken cancellationToken)
    {
        var found = new TaskCompletionSource<MediaSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, EventArgs e)
        {
            if (Find(appId) is { } s)
            {
                found.TrySetResult(s);
            }
        }

        source.Changed += OnChanged;
        try
        {
            OnChanged(null, EventArgs.Empty);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await using var registration = timeout.Token.Register(() => found.TrySetCanceled());
            using var timer = time.CreateTimer(_ => found.TrySetCanceled(), null, SessionTimeout, Timeout.InfiniteTimeSpan);
            try
            {
                var session = await found.Task;
                if (SettleDelay > TimeSpan.Zero)
                {
                    await Task.Delay(SettleDelay, time, cancellationToken);
                }
                return Find(appId) ?? session;
            }
            catch (TaskCanceledException)
            {
                return null;
            }
        }
        finally
        {
            source.Changed -= OnChanged;
        }
    }
}
