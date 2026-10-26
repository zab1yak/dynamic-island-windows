using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Threading;
using DynamicIsland.Modules.Media.Services;

namespace DynamicIsland.Modules.Media.ViewModels;

/// <summary>
/// The music page: the selected media session, its position (interpolated locally between app
/// reports), transport commands and the real-level visualizer. Timers run only while they are
/// visible: the progress clock while expanded and playing, the visualizer while playing.
/// </summary>
public sealed partial class MediaViewModel : ObservableObject, ILiveActivitySource, IDisposable
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan VisualizerInterval = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan RecentPauseWindow = TimeSpan.FromMinutes(5);

    private readonly IMediaSessionSource _source;
    private readonly IAudioLevelSource _audio;
    private readonly TimeProvider _time;
    private readonly IUiDispatcher _dispatcher;
    private readonly IslandStateMachine _island;
    private readonly Func<byte[]?, Task<Rgb>> _computeAccent;
    private readonly VisualizerModel _visualizer = new(4);

    private ITimer? _progressTimer;
    private ITimer? _visualizerTimer;
    private string? _selectedAppId;
    private MediaSnapshot? _current;
    private byte[]? _accentFor;
    private DateTimeOffset _activityStarted;
    private DateTimeOffset? _pausedAt;

    [ObservableProperty]
    private bool _hasMedia;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _artist = string.Empty;

    [ObservableProperty]
    private string _appName = string.Empty;

    [ObservableProperty]
    private byte[]? _artwork;

    [ObservableProperty]
    private uint _accent = DominantColor.Fallback.ToArgb();

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _canSeek;

    [ObservableProperty]
    private bool _canNext;

    [ObservableProperty]
    private bool _canPrevious;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _positionText = "0:00";

    [ObservableProperty]
    private string _durationText = "0:00";

    [ObservableProperty]
    private bool _hasMultipleSessions;

    [ObservableProperty]
    private IReadOnlyList<double> _levels = [0.08, 0.08, 0.08, 0.08];

    [ObservableProperty]
    private string? _notice;

    /// <summary>Creates the view model.</summary>
    /// <param name="accent">Computes the accent colour from artwork bytes.</param>
    public MediaViewModel(IMediaSessionSource source, IAudioLevelSource audio, TimeProvider time, IUiDispatcher dispatcher,
        IslandStateMachine island, Func<byte[]?, Task<Rgb>> accent)
    {
        _source = source;
        _audio = audio;
        _time = time;
        _dispatcher = dispatcher;
        _island = island;
        _computeAccent = accent;
        Live = new MediaLiveViewModel(this);
        source.Changed += (_, _) => OnSessionsChanged();
        island.ModeChanged += (_, _) => UpdateTimers();
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>View model of the compact live pill.</summary>
    public MediaLiveViewModel Live { get; }

    /// <summary>The session shown on the page, if any.</summary>
    public MediaSnapshot? CurrentSnapshot => _current;

    /// <summary>True while the user drags the scrubber (the clock then leaves the progress alone).</summary>
    public bool IsScrubbing { get; set; }

    /// <inheritdoc />
    public LiveActivity? Current
    {
        get
        {
            if (_current is null)
            {
                return null;
            }

            if (_current.IsPlaying)
            {
                return new LiveActivity("media", ActivityKind.Media, LiveActivityPriority.MediaPlaying, Live, _activityStarted);
            }

            return _pausedAt is { } paused && _time.GetUtcNow() - paused < RecentPauseWindow
                ? new LiveActivity("media", ActivityKind.Media, LiveActivityPriority.MediaPaused, Live, paused)
                : null;
        }
    }

    /// <summary>Raised when the shown session changed (used by the last-track store).</summary>
    public event EventHandler? SnapshotChanged;

    /// <inheritdoc />
    public void Dispose()
    {
        _progressTimer?.Dispose();
        _visualizerTimer?.Dispose();
    }

    /// <summary>Re-reads sessions and selection (call after start-up and resume).</summary>
    public void Refresh() => OnSessionsChanged();

    /// <summary>Shows a short message in place of the artist line for a few seconds.</summary>
    public void ShowNotice(string message)
    {
        Notice = message;
        var shown = message;
        _time.CreateTimer(_ => _dispatcher.Post(() =>
        {
            if (Notice == shown)
            {
                Notice = null;
            }
        }), null, TimeSpan.FromSeconds(4), Timeout.InfiniteTimeSpan);
    }

    [RelayCommand]
    private async Task PlayPauseAsync()
    {
        if (_current is null)
        {
            return;
        }

        var ok = _current.IsPlaying ? await _source.PauseAsync(_current.AppId) : await _source.PlayAsync(_current.AppId);
        if (!ok)
        {
            await OnCommandFailedAsync();
        }
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (_current is not null && !await _source.NextAsync(_current.AppId))
        {
            await OnCommandFailedAsync();
        }
    }

    [RelayCommand]
    private async Task PreviousAsync()
    {
        if (_current is not null && !await _source.PreviousAsync(_current.AppId))
        {
            await OnCommandFailedAsync();
        }
    }

    /// <summary>Seeks to a fraction (0–1) of the track.</summary>
    [RelayCommand]
    private async Task SeekAsync(double fraction)
    {
        if (_current is null || !_current.CanSeek || _current.Duration <= TimeSpan.Zero)
        {
            return;
        }

        var target = _current.Duration * Math.Clamp(fraction, 0, 1);
        // Optimistic update so the scrubber does not jump back while the app catches up.
        _current = _current with { Position = target, PositionUpdatedAt = _time.GetUtcNow() };
        UpdateProgress();
        if (!await _source.SeekAsync(_current.AppId, target))
        {
            await OnCommandFailedAsync();
        }
    }

    /// <summary>Switches to the next media session (when several apps play media).</summary>
    [RelayCommand]
    private void NextSession()
    {
        var sessions = _source.Sessions;
        if (sessions.Count < 2)
        {
            return;
        }

        var index = sessions.ToList().FindIndex(s => s.AppId == _current?.AppId);
        _selectedAppId = sessions[(index + 1) % sessions.Count].AppId;
        OnSessionsChanged();
    }

    /// <summary>Hook for the last-track restore flow when a command has no live session to act on.</summary>
    public Func<Task<bool>>? CommandFallback { get; set; }

    private async Task OnCommandFailedAsync()
    {
        if (CommandFallback is not null && await CommandFallback())
        {
            return;
        }

        ShowNotice(MediaStrings.Get("Media.CommandFailed"));
    }

    private void OnSessionsChanged()
    {
        var sessions = _source.Sessions;
        HasMultipleSessions = sessions.Count > 1;
        var next = sessions.FirstOrDefault(s => s.AppId == _selectedAppId) ?? sessions.FirstOrDefault(s => s.IsPlaying) ?? sessions.FirstOrDefault();
        if (next is null && _current is not null && _source.Sessions.Count == 0 && _current.AppId == RestoredAppId)
        {
            // Keep showing the restored last track until a real session appears.
            return;
        }

        Show(next);
    }

    /// <summary>App id of a snapshot restored from disk (not a live session), if any.</summary>
    public string? RestoredAppId { get; private set; }

    /// <summary>Shows a saved track as paused until a real session takes over.</summary>
    public void ShowRestored(MediaSnapshot snapshot)
    {
        if (_source.Sessions.Count > 0)
        {
            return;
        }

        RestoredAppId = snapshot.AppId;
        Show(snapshot with { State = PlaybackState.Paused });
    }

    private void Show(MediaSnapshot? snapshot)
    {
        var previous = _current;
        if (snapshot is not null && _source.Sessions.Any(s => s.AppId == snapshot.AppId))
        {
            RestoredAppId = null;
        }

        _current = snapshot;
        HasMedia = snapshot is not null;
        Title = snapshot?.Title ?? string.Empty;
        Artist = snapshot?.Artist ?? string.Empty;
        AppName = snapshot?.AppName ?? string.Empty;
        Artwork = snapshot?.Artwork;
        IsPlaying = snapshot?.IsPlaying ?? false;
        CanSeek = snapshot?.CanSeek ?? false;
        CanNext = snapshot?.CanNext ?? false;
        CanPrevious = snapshot?.CanPrevious ?? false;
        DurationText = PositionInterpolator.Format(snapshot?.Duration ?? TimeSpan.Zero);

        if (snapshot?.IsPlaying == true && previous?.IsPlaying != true)
        {
            _activityStarted = _time.GetUtcNow();
            _pausedAt = null;
        }
        else if (snapshot is not null && !snapshot.IsPlaying && previous?.IsPlaying == true)
        {
            _pausedAt = _time.GetUtcNow();
        }

        if (!ReferenceEquals(_accentFor, snapshot?.Artwork))
        {
            _accentFor = snapshot?.Artwork;
            _ = UpdateAccentAsync(snapshot?.Artwork);
        }

        UpdateProgress();
        UpdateTimers();
        Changed?.Invoke(this, EventArgs.Empty);
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task UpdateAccentAsync(byte[]? artwork)
    {
        var color = await _computeAccent(artwork);
        _dispatcher.Post(() =>
        {
            if (ReferenceEquals(_accentFor, artwork))
            {
                Accent = color.ToArgb();
            }
        });
    }

    private void UpdateProgress()
    {
        if (_current is null || IsScrubbing)
        {
            return;
        }

        var now = _time.GetUtcNow();
        PositionText = PositionInterpolator.Format(PositionInterpolator.At(_current, now));
        Progress = PositionInterpolator.Progress(_current, now);
        if (_current.IsPlaying && RestoredAppId is null)
        {
            // Lets the last-track store save the position every few seconds while playing.
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateTimers()
    {
        var mode = _island.Mode;
        var playing = _current?.IsPlaying == true;
        var visible = mode != IslandMode.Hidden;

        SetTimer(ref _progressTimer, playing && IslandMetrics.IsExpanded(mode), ProgressInterval, UpdateProgress);
        SetTimer(ref _visualizerTimer, playing && visible, VisualizerInterval, TickVisualizer);
        if (!playing)
        {
            _visualizer.Rest();
            Levels = [.. _visualizer.Levels];
        }
    }

    private void SetTimer(ref ITimer? timer, bool run, TimeSpan interval, Action tick)
    {
        if (run && timer is null)
        {
            timer = _time.CreateTimer(_ => _dispatcher.Post(tick), null, interval, interval);
        }
        else if (!run && timer is not null)
        {
            timer.Dispose();
            timer = null;
        }
    }

    private void TickVisualizer()
    {
        if (_visualizerTimer is null)
        {
            return;
        }

        _visualizer.Push(_audio.ReadPeak());
        Levels = [.. _visualizer.Levels];
    }
}

/// <summary>Compact live-activity pill for media: artwork thumbnail and visualizer.</summary>
public sealed class MediaLiveViewModel(MediaViewModel media)
{
    /// <summary>The full media view model the pill binds to.</summary>
    public MediaViewModel Media { get; } = media;
}
