namespace DynamicIsland.Modules.Media.Services;

/// <summary>Playback state of a media session.</summary>
public enum PlaybackState
{
    /// <summary>Stopped, closed or unknown.</summary>
    Stopped,

    /// <summary>Paused.</summary>
    Paused,

    /// <summary>Playing.</summary>
    Playing,
}

/// <summary>Immutable snapshot of one media session (Spotify, a browser tab, Yandex Music…).</summary>
/// <param name="AppId">AppUserModelID of the source app (for example <c>Spotify.exe</c>).</param>
/// <param name="AppName">Readable app name.</param>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Artist.</param>
/// <param name="Album">Album.</param>
/// <param name="Artwork">Encoded artwork image (PNG/JPEG), or <c>null</c>.</param>
/// <param name="State">Playback state.</param>
/// <param name="Position">Position at <paramref name="PositionUpdatedAt"/>.</param>
/// <param name="Duration">Track length (zero when unknown).</param>
/// <param name="PositionUpdatedAt">When the app last reported the position.</param>
/// <param name="PlaybackRate">Playback speed (1 = normal).</param>
/// <param name="CanSeek">Whether the app accepts seeking.</param>
/// <param name="CanNext">Whether "next" is enabled.</param>
/// <param name="CanPrevious">Whether "previous" is enabled.</param>
public sealed record MediaSnapshot(
    string AppId,
    string AppName,
    string Title,
    string Artist,
    string Album,
    byte[]? Artwork,
    PlaybackState State,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset PositionUpdatedAt,
    double PlaybackRate,
    bool CanSeek,
    bool CanNext,
    bool CanPrevious)
{
    /// <summary>Whether the session is playing.</summary>
    public bool IsPlaying => State == PlaybackState.Playing;
}

/// <summary>Access to the system media sessions (GSMTC). Events are raised on the UI thread.</summary>
public interface IMediaSessionSource
{
    /// <summary>All sessions, the system's current one first.</summary>
    IReadOnlyList<MediaSnapshot> Sessions { get; }

    /// <summary>Raised when any session appears, disappears or changes.</summary>
    event EventHandler? Changed;

    /// <summary>Connects to the session manager (also after resume from sleep).</summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Plays the session of <paramref name="appId"/>.</summary>
    Task<bool> PlayAsync(string appId);

    /// <summary>Pauses the session of <paramref name="appId"/>.</summary>
    Task<bool> PauseAsync(string appId);

    /// <summary>Skips to the next track.</summary>
    Task<bool> NextAsync(string appId);

    /// <summary>Skips to the previous track.</summary>
    Task<bool> PreviousAsync(string appId);

    /// <summary>Seeks to <paramref name="position"/>.</summary>
    Task<bool> SeekAsync(string appId, TimeSpan position);
}
