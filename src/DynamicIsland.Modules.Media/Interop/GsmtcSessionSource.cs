using System.Runtime.InteropServices;
using DynamicIsland.Core.Threading;
using DynamicIsland.Modules.Media.Services;
using Microsoft.Extensions.Logging;
using Windows.ApplicationModel;
using Windows.Media.Control;
using Windows.Storage.Streams;
using GsmtcPlaybackStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using SessionManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;

namespace DynamicIsland.Modules.Media.Interop;

/// <summary>
/// <see cref="IMediaSessionSource"/> over the Windows Global System Media Transport Controls: works
/// with every app that shows up in the Windows media flyout (Spotify, Yandex Music, browsers, players).
/// WinRT events arrive on thread-pool threads; every state change goes through one async gate and
/// the UI only ever sees immutable snapshot lists.
/// </summary>
internal sealed class GsmtcSessionSource(IUiDispatcher dispatcher, TimeProvider time, ILogger<GsmtcSessionSource> logger) : IMediaSessionSource, IDisposable
{
    internal const int MaxArtworkBytes = 4 * 1024 * 1024;

    /// <summary>Timelines older than this are a zero FILETIME (1601) or garbage: position is unknown.</summary>
    private static readonly DateTimeOffset MinValidTimestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, MediaSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SessionManager? _manager;
    private volatile IReadOnlyList<MediaSnapshot> _ordered = [];
    private bool _disposed;

    /// <inheritdoc />
    public IReadOnlyList<MediaSnapshot> Sessions => _ordered;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Detach();
            try
            {
                _manager = await SessionManager.RequestAsync().AsTask(cancellationToken);
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Media session manager is unavailable");
                PublishLocked();
                return;
            }

            _manager.SessionsChanged += OnSessionsChanged;
            _manager.CurrentSessionChanged += OnSessionsChanged;
        }
        finally
        {
            _gate.Release();
        }

        await RefreshSessionsAsync();
    }

    /// <inheritdoc />
    public Task<bool> PlayAsync(string appId) => Run(appId, s => s.TryPlayAsync().AsTask());

    /// <inheritdoc />
    public Task<bool> PauseAsync(string appId) => Run(appId, s => s.TryPauseAsync().AsTask());

    /// <inheritdoc />
    public Task<bool> NextAsync(string appId) => Run(appId, s => s.TrySkipNextAsync().AsTask());

    /// <inheritdoc />
    public Task<bool> PreviousAsync(string appId) => Run(appId, s => s.TrySkipPreviousAsync().AsTask());

    /// <inheritdoc />
    public Task<bool> SeekAsync(string appId, TimeSpan position) => Run(appId, async s =>
    {
        // Timeline positions are relative to StartTime (non-zero for some live streams).
        var start = s.GetTimelineProperties()?.StartTime ?? TimeSpan.Zero;
        return await s.TryChangePlaybackPositionAsync(Math.Max(0, (start + position).Ticks));
    });

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        Detach();
    }

    private async Task<bool> Run(string appId, Func<Session, Task<bool>> command)
    {
        Session? session;
        await _gate.WaitAsync();
        try
        {
            _sessions.TryGetValue(appId, out session);
        }
        finally
        {
            _gate.Release();
        }

        if (session is null)
        {
            return false;
        }

        try
        {
            return await command(session);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Media command failed for {App}", appId);
            return false;
        }
    }

    private void OnSessionsChanged(SessionManager sender, object args) => _ = RefreshSessionsAsync();

    private async Task RefreshSessionsAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (_manager is null)
            {
                return;
            }

            var live = _manager.GetSessions().GroupBy(s => s.SourceAppUserModelId).Select(g => g.First()).ToList();
            foreach (var (id, old) in _sessions.ToList())
            {
                // Gone, or the app restarted and registered a new session object under the same id.
                if (!live.Any(s => ReferenceEquals(s, old) || (s.SourceAppUserModelId == id && s.Equals(old))))
                {
                    Unsubscribe(old);
                    _sessions.Remove(id);
                    _snapshots.Remove(id);
                }
            }

            foreach (var session in live)
            {
                if (_sessions.TryAdd(session.SourceAppUserModelId, session))
                {
                    session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                    session.PlaybackInfoChanged += OnPlaybackChanged;
                    session.TimelinePropertiesChanged += OnTimelineChanged;
                    await UpdateSnapshotLockedAsync(session, includeMedia: true);
                }
            }

            PublishLocked();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Refreshing media sessions failed");
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnMediaPropertiesChanged(Session sender, MediaPropertiesChangedEventArgs args) => _ = UpdateAndPublishAsync(sender, includeMedia: true);

    private void OnPlaybackChanged(Session sender, PlaybackInfoChangedEventArgs args) => _ = UpdateAndPublishAsync(sender, includeMedia: false);

    private void OnTimelineChanged(Session sender, TimelinePropertiesChangedEventArgs args) => _ = UpdateAndPublishAsync(sender, includeMedia: false);

    private async Task UpdateAndPublishAsync(Session session, bool includeMedia)
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            // Ignore late events from a session we already dropped.
            if (_sessions.TryGetValue(session.SourceAppUserModelId, out var known) && ReferenceEquals(known, session))
            {
                await UpdateSnapshotLockedAsync(session, includeMedia);
                PublishLocked();
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or IOException)
        {
            logger.LogDebug(ex, "Media update failed");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task UpdateSnapshotLockedAsync(Session session, bool includeMedia)
    {
        var appId = session.SourceAppUserModelId;
        _snapshots.TryGetValue(appId, out var previous);

        string title = previous?.Title ?? string.Empty, artist = previous?.Artist ?? string.Empty, album = previous?.Album ?? string.Empty;
        var artwork = previous?.Artwork;
        if (includeMedia || previous is null)
        {
            var media = await session.TryGetMediaPropertiesAsync();
            if (media is not null)
            {
                title = media.Title ?? string.Empty;
                artist = media.Artist ?? string.Empty;
                album = media.AlbumTitle ?? string.Empty;
                var fresh = await ReadArtworkAsync(media.Thumbnail);
                // Same picture → keep the same array, so caches keyed by reference stay valid.
                artwork = fresh is not null && artwork is not null && fresh.AsSpan().SequenceEqual(artwork) ? artwork : fresh;
            }
        }

        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        var state = playback?.PlaybackStatus switch
        {
            GsmtcPlaybackStatus.Playing => PlaybackState.Playing,
            GsmtcPlaybackStatus.Paused => PlaybackState.Paused,
            _ => PlaybackState.Stopped,
        };

        var hasTimeline = timeline is not null && timeline.LastUpdatedTime >= MinValidTimestamp && timeline.EndTime > timeline.StartTime;
        var duration = hasTimeline ? timeline!.EndTime - timeline.StartTime : TimeSpan.Zero;
        var position = hasTimeline ? timeline!.Position - timeline.StartTime : TimeSpan.Zero;
        var updatedAt = hasTimeline ? timeline!.LastUpdatedTime : default;
        if (hasTimeline && state == PlaybackState.Playing && previous is { IsPlaying: false } && previous.PositionUpdatedAt == updatedAt)
        {
            // Resumed, but the app has not sent a fresh timeline yet: count from now, not from before the pause.
            updatedAt = time.GetUtcNow();
        }

        _snapshots[appId] = new MediaSnapshot(
            appId,
            previous?.AppName ?? ResolveAppName(appId),
            title,
            artist,
            album,
            artwork,
            state,
            position < TimeSpan.Zero ? TimeSpan.Zero : position,
            duration,
            updatedAt,
            playback?.PlaybackRate ?? 1.0,
            hasTimeline && (playback?.Controls.IsPlaybackPositionEnabled ?? false),
            playback?.Controls.IsNextEnabled ?? false,
            playback?.Controls.IsPreviousEnabled ?? false);
    }

    private void PublishLocked()
    {
        var currentId = SafeCurrentId();
        _ordered = _snapshots.Values
            .OrderByDescending(s => s.AppId == currentId)
            .ThenByDescending(s => s.IsPlaying)
            .ThenBy(s => s.AppName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        dispatcher.Post(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    private string? SafeCurrentId()
    {
        try
        {
            return _manager?.GetCurrentSession()?.SourceAppUserModelId;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private async Task<byte[]?> ReadArtworkAsync(IRandomAccessStreamReference? reference)
    {
        if (reference is null)
        {
            return null;
        }

        try
        {
            using var stream = await reference.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > MaxArtworkBytes)
            {
                return null;
            }

            var buffer = new Windows.Storage.Streams.Buffer((uint)stream.Size);
            var read = await stream.ReadAsync(buffer, (uint)stream.Size, InputStreamOptions.None);
            var bytes = new byte[read.Length];
            using var reader = DataReader.FromBuffer(read);
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Artwork could not be read");
            return null;
        }
    }

    /// <summary>Readable name for an AppUserModelID; falls back to a cleaned-up id.</summary>
    internal static string ResolveAppName(string appId)
    {
        try
        {
            var info = AppInfo.GetFromAppUserModelId(appId);
            if (!string.IsNullOrWhiteSpace(info?.DisplayInfo?.DisplayName))
            {
                return info.DisplayInfo.DisplayName;
            }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or UnauthorizedAccessException)
        {
            // Desktop (Win32) apps have no package info; use the id below.
        }

        return FriendlyName(appId);
    }

    /// <summary><c>Spotify.exe</c> → <c>Spotify</c>; <c>Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic</c> → <c>ZuneMusic</c>.</summary>
    internal static string FriendlyName(string appId)
    {
        var name = appId;
        var bang = name.IndexOf('!', StringComparison.Ordinal);
        if (bang > 0)
        {
            name = name[..bang];
        }

        var underscore = name.IndexOf('_', StringComparison.Ordinal);
        if (underscore > 0)
        {
            name = name[..underscore];
        }

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        var slash = name.LastIndexOfAny(['\\', '/']);
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        var dot = name.LastIndexOf('.');
        if (dot >= 0 && dot < name.Length - 1)
        {
            name = name[(dot + 1)..];
        }

        return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : appId;
    }

    private void Unsubscribe(Session session)
    {
        session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        session.PlaybackInfoChanged -= OnPlaybackChanged;
        session.TimelinePropertiesChanged -= OnTimelineChanged;
    }

    private void Detach()
    {
        if (_manager is not null)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
            _manager.CurrentSessionChanged -= OnSessionsChanged;
            _manager = null;
        }

        foreach (var session in _sessions.Values)
        {
            Unsubscribe(session);
        }

        _sessions.Clear();
        _snapshots.Clear();
    }
}
