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
/// </summary>
internal sealed class GsmtcSessionSource(IUiDispatcher dispatcher, ILogger<GsmtcSessionSource> logger) : IMediaSessionSource, IDisposable
{
    private const int MaxArtworkBytes = 4 * 1024 * 1024;

    private readonly Dictionary<string, MediaSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private SessionManager? _manager;
    private IReadOnlyList<MediaSnapshot> _ordered = [];

    /// <inheritdoc />
    public IReadOnlyList<MediaSnapshot> Sessions => _ordered;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Detach();
        try
        {
            _manager = await SessionManager.RequestAsync().AsTask(cancellationToken);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Media session manager is unavailable");
            return;
        }

        _manager.SessionsChanged += OnSessionsChanged;
        _manager.CurrentSessionChanged += OnSessionsChanged;
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
    public Task<bool> SeekAsync(string appId, TimeSpan position) =>
        Run(appId, s => s.TryChangePlaybackPositionAsync(Math.Max(0, position.Ticks)).AsTask());

    /// <inheritdoc />
    public void Dispose()
    {
        Detach();
        _refreshGate.Dispose();
    }

    private async Task<bool> Run(string appId, Func<Session, Task<bool>> command)
    {
        if (!_sessions.TryGetValue(appId, out var session))
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
        if (_manager is null)
        {
            return;
        }

        await _refreshGate.WaitAsync();
        try
        {
            var live = _manager.GetSessions().GroupBy(s => s.SourceAppUserModelId).Select(g => g.First()).ToList();
            foreach (var gone in _sessions.Keys.Except(live.Select(s => s.SourceAppUserModelId)).ToList())
            {
                Unsubscribe(_sessions[gone]);
                _sessions.Remove(gone);
                _snapshots.Remove(gone);
            }

            foreach (var session in live)
            {
                if (_sessions.TryAdd(session.SourceAppUserModelId, session))
                {
                    session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                    session.PlaybackInfoChanged += OnPlaybackChanged;
                    session.TimelinePropertiesChanged += OnTimelineChanged;
                    await UpdateSnapshotAsync(session, includeMedia: true);
                }
            }
        }
        catch (COMException ex)
        {
            logger.LogWarning(ex, "Refreshing media sessions failed");
        }
        finally
        {
            _refreshGate.Release();
        }

        Publish();
    }

    private void OnMediaPropertiesChanged(Session sender, MediaPropertiesChangedEventArgs args) => _ = UpdateAndPublishAsync(sender, includeMedia: true);

    private void OnPlaybackChanged(Session sender, PlaybackInfoChangedEventArgs args) => _ = UpdateAndPublishAsync(sender, includeMedia: false);

    private void OnTimelineChanged(Session sender, TimelinePropertiesChangedEventArgs args) => _ = UpdateAndPublishAsync(sender, includeMedia: false);

    private async Task UpdateAndPublishAsync(Session session, bool includeMedia)
    {
        try
        {
            await UpdateSnapshotAsync(session, includeMedia);
            Publish();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or IOException)
        {
            logger.LogDebug(ex, "Media update failed");
        }
    }

    private async Task UpdateSnapshotAsync(Session session, bool includeMedia)
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
                artwork = await ReadArtworkAsync(media.Thumbnail);
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
        var duration = timeline is null ? TimeSpan.Zero : timeline.EndTime - timeline.StartTime;
        var snapshot = new MediaSnapshot(
            appId,
            previous?.AppName ?? ResolveAppName(appId),
            title,
            artist,
            album,
            artwork,
            state,
            timeline?.Position ?? TimeSpan.Zero,
            duration < TimeSpan.Zero ? TimeSpan.Zero : duration,
            timeline?.LastUpdatedTime ?? DateTimeOffset.UtcNow,
            playback?.PlaybackRate ?? 1.0,
            playback?.Controls.IsPlaybackPositionEnabled ?? false,
            playback?.Controls.IsNextEnabled ?? false,
            playback?.Controls.IsPreviousEnabled ?? false);
        _snapshots[appId] = snapshot;
    }

    private void Publish()
    {
        var currentId = SafeCurrentId();
        _ordered = _snapshots.Values
            .OrderByDescending(s => s.AppId == currentId)
            .ThenByDescending(s => s.IsPlaying)
            .ThenBy(s => s.AppName, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
