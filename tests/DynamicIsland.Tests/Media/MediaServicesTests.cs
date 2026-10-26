using DynamicIsland.Core.Island;
using DynamicIsland.Core.Modules;
using DynamicIsland.Modules.Media.Interop;
using DynamicIsland.Modules.Media.Services;
using DynamicIsland.Modules.Media.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace DynamicIsland.Tests.Media;

internal static class Snapshots
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    public static MediaSnapshot Track(string app = "Spotify.exe", PlaybackState state = PlaybackState.Playing,
        double positionSeconds = 60, double durationSeconds = 200, DateTimeOffset? updated = null, bool canSeek = true) =>
        new(app, app.Replace(".exe", string.Empty, StringComparison.Ordinal), "Song", "Artist", "Album", null, state,
            TimeSpan.FromSeconds(positionSeconds), TimeSpan.FromSeconds(durationSeconds), updated ?? T0, 1.0, canSeek, true, true);
}

public sealed class PositionInterpolatorTests
{
    [Fact]
    public void Playing_track_advances_from_the_last_report()
    {
        var position = PositionInterpolator.At(Snapshots.Track(), Snapshots.T0.AddSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(70), position);
    }

    [Fact]
    public void Paused_track_stays_put()
    {
        var position = PositionInterpolator.At(Snapshots.Track(state: PlaybackState.Paused), Snapshots.T0.AddMinutes(5));
        Assert.Equal(TimeSpan.FromSeconds(60), position);
    }

    [Fact]
    public void Position_is_clamped_to_duration_and_progress_is_fractional()
    {
        var snapshot = Snapshots.Track();
        Assert.Equal(snapshot.Duration, PositionInterpolator.At(snapshot, Snapshots.T0.AddHours(1)));
        Assert.Equal(0.3, PositionInterpolator.Progress(snapshot, Snapshots.T0), 3);
    }

    [Fact]
    public void Playback_rate_is_respected()
    {
        var fast = Snapshots.Track() with { PlaybackRate = 2 };
        Assert.Equal(TimeSpan.FromSeconds(80), PositionInterpolator.At(fast, Snapshots.T0.AddSeconds(10)));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(100, "1:40")]
    [InlineData(3725, "1:02:05")]
    [InlineData(-5, "0:00")]
    public void Formats_like_a_player(double seconds, string expected)
    {
        Assert.Equal(expected, PositionInterpolator.Format(TimeSpan.FromSeconds(seconds)));
    }
}

public sealed class DominantColorTests
{
    private static byte[] Solid(byte r, byte g, byte b, int pixels = 64)
    {
        var data = new byte[pixels * 4];
        for (var i = 0; i < pixels; i++)
        {
            data[(i * 4) + 0] = b;
            data[(i * 4) + 1] = g;
            data[(i * 4) + 2] = r;
            data[(i * 4) + 3] = 255;
        }

        return data;
    }

    [Fact]
    public void Red_artwork_gives_a_muted_red()
    {
        var color = DominantColor.FromBgra(Solid(230, 20, 20));
        Assert.True(color.R > color.G + 40 && color.R > color.B + 40);
        var (_, s, l) = DominantColor.ToHsl(color.R, color.G, color.B);
        Assert.True(s <= 0.56);
        Assert.InRange(l, 0.44, 0.63);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(255, 255, 255)]
    public void Black_or_white_artwork_falls_back(byte r, byte g, byte b)
    {
        Assert.Equal(DominantColor.Fallback, DominantColor.FromBgra(Solid(r, g, b)));
    }

    [Fact]
    public void Empty_input_falls_back()
    {
        Assert.Equal(DominantColor.Fallback, DominantColor.FromBgra([]));
    }
}

public sealed class VisualizerModelTests
{
    [Fact]
    public void Silence_rests_and_sound_raises_bars()
    {
        var model = new VisualizerModel(4);
        for (var i = 0; i < 20; i++)
        {
            model.Push(0);
        }

        Assert.All(model.Levels, l => Assert.True(l < 0.1));
        for (var i = 0; i < 20; i++)
        {
            model.Push(0.8);
        }

        Assert.All(model.Levels, l => Assert.True(l > 0.5));
    }

    [Fact]
    public void Levels_stay_within_bounds()
    {
        var model = new VisualizerModel(5);
        foreach (var peak in new[] { 5.0, -1, double.MaxValue, 0.5 })
        {
            model.Push(peak);
            Assert.All(model.Levels, l => Assert.InRange(l, 0, 1));
        }
    }
}

public sealed class AppNameTests
{
    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "ZuneMusic")]
    [InlineData("chrome", "Chrome")]
    [InlineData(@"C:\Program Files\foobar2000\foobar2000.exe", "Foobar2000")]
    [InlineData("Yandex.Music", "Music")]
    public void Friendly_names(string appId, string expected)
    {
        Assert.Equal(expected, GsmtcSessionSource.FriendlyName(appId));
    }
}

internal sealed class FakeMediaSource : IMediaSessionSource
{
    public List<MediaSnapshot> List { get; } = [];

    public List<string> Commands { get; } = [];

    public bool Succeed { get; set; } = true;

    public IReadOnlyList<MediaSnapshot> Sessions => List;

    public event EventHandler? Changed;

    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> PlayAsync(string appId) => Record($"play {appId}");

    public Task<bool> PauseAsync(string appId) => Record($"pause {appId}");

    public Task<bool> NextAsync(string appId) => Record($"next {appId}");

    public Task<bool> PreviousAsync(string appId) => Record($"prev {appId}");

    public Task<bool> SeekAsync(string appId, TimeSpan position) => Record($"seek {appId} {position.TotalSeconds}");

    private Task<bool> Record(string command)
    {
        Commands.Add(command);
        return Task.FromResult(Succeed);
    }
}

internal sealed class SilentAudio : IAudioLevelSource
{
    public float ReadPeak() => 0.5f;

    public void Reset()
    {
    }
}

public sealed class MediaViewModelTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(Snapshots.T0);
    private readonly FakeMediaSource _source = new();
    private readonly IslandStateMachine _island;
    private readonly MediaViewModel _vm;

    public MediaViewModelTests()
    {
        _island = new IslandStateMachine(_time, new InlineDispatcher());
        _vm = new MediaViewModel(_source, new SilentAudio(), _time, new InlineDispatcher(), _island, _ => Task.FromResult(DominantColor.Fallback));
    }

    public void Dispose()
    {
        _vm.Dispose();
        _island.Dispose();
    }

    [Fact]
    public void Playing_session_is_shown_and_becomes_a_live_activity()
    {
        _source.List.Add(Snapshots.Track());
        _source.Raise();

        Assert.True(_vm.HasMedia);
        Assert.Equal("Song", _vm.Title);
        Assert.Equal("1:00", _vm.PositionText);
        Assert.Equal(LiveActivityPriority.MediaPlaying, _vm.Current?.Priority);
    }

    [Fact]
    public void Paused_recently_is_low_priority_then_disappears()
    {
        _source.List.Add(Snapshots.Track());
        _source.Raise();
        _source.List[0] = Snapshots.Track(state: PlaybackState.Paused);
        _source.Raise();

        Assert.Equal(LiveActivityPriority.MediaPaused, _vm.Current?.Priority);
        _time.Advance(TimeSpan.FromMinutes(6));
        Assert.Null(_vm.Current);
    }

    [Fact]
    public async Task Play_pause_targets_the_shown_app()
    {
        _source.List.Add(Snapshots.Track(state: PlaybackState.Paused));
        _source.Raise();

        await _vm.PlayPauseCommand.ExecuteAsync(null);

        Assert.Equal(["play Spotify.exe"], _source.Commands);
    }

    [Fact]
    public async Task Seek_updates_position_optimistically()
    {
        _source.List.Add(Snapshots.Track(state: PlaybackState.Paused));
        _source.Raise();

        await _vm.SeekCommand.ExecuteAsync(0.5);

        Assert.Equal("1:40", _vm.PositionText);
        Assert.Equal(["seek Spotify.exe 100"], _source.Commands);
    }

    [Fact]
    public async Task Failed_command_shows_a_notice_instead_of_throwing()
    {
        _source.Succeed = false;
        _source.List.Add(Snapshots.Track());
        _source.Raise();

        await _vm.NextCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(_vm.Notice));
    }

    [Fact]
    public void Next_session_cycles_between_apps()
    {
        _source.List.Add(Snapshots.Track("Spotify.exe"));
        _source.List.Add(Snapshots.Track("chrome", PlaybackState.Paused));
        _source.Raise();
        Assert.True(_vm.HasMultipleSessions);
        Assert.Equal("Spotify", _vm.AppName);

        _vm.NextSessionCommand.Execute(null);

        Assert.Equal("chrome", _vm.AppName);
    }

    [Fact]
    public void Restored_track_is_shown_paused_until_a_real_session_appears()
    {
        _vm.ShowRestored(Snapshots.Track("Yandex.Music", PlaybackState.Playing, positionSeconds: 100));

        Assert.True(_vm.HasMedia);
        Assert.False(_vm.IsPlaying);
        Assert.Equal("1:40", _vm.PositionText);

        _source.Raise();
        Assert.True(_vm.HasMedia);

        _source.List.Add(Snapshots.Track("Spotify.exe"));
        _source.Raise();
        Assert.Equal("Spotify", _vm.AppName);
        Assert.Null(_vm.RestoredAppId);
    }
}
