using DynamicIsland.Core.Storage;
using DynamicIsland.Modules.Media.Interop;
using DynamicIsland.Modules.Media.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DynamicIsland.Tests.Media;

public sealed class LastTrackStoreTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeTimeProvider _time = new(Snapshots.T0);
    private readonly JsonStorageService _storage;
    private readonly LastTrackStore _store;

    public LastTrackStoreTests()
    {
        _storage = new JsonStorageService(_temp.Path, NullLogger<JsonStorageService>.Instance);
        _store = new LastTrackStore(_storage, _time);
    }

    public void Dispose()
    {
        _store.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task Saves_track_with_interpolated_position_and_artwork_then_restores_it_paused()
    {
        var art = new byte[] { 1, 2, 3, 4 };
        var playing = Snapshots.Track("Yandex.Music", positionSeconds: 90) with { Artwork = art };
        _time.Advance(TimeSpan.FromSeconds(10));

        Assert.True(await _store.OnSnapshotAsync(playing));

        var fresh = new LastTrackStore(_storage, _time);
        var loaded = await fresh.LoadAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(loaded);
        var snapshot = LastTrackStore.ToSnapshot(loaded.Value.Track, loaded.Value.Artwork);
        Assert.Equal(PlaybackState.Paused, snapshot.State);
        Assert.Equal(TimeSpan.FromSeconds(100), snapshot.Position);
        Assert.Equal(art, snapshot.Artwork);
        Assert.Equal("Song", snapshot.Title);
        fresh.Dispose();
    }

    [Fact]
    public async Task Saves_on_change_pause_and_every_five_seconds_but_not_more()
    {
        var track = Snapshots.Track();
        Assert.True(await _store.OnSnapshotAsync(track));
        Assert.False(await _store.OnSnapshotAsync(track));

        _time.Advance(TimeSpan.FromSeconds(4));
        Assert.False(await _store.OnSnapshotAsync(track));
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await _store.OnSnapshotAsync(track));

        Assert.True(await _store.OnSnapshotAsync(track with { State = PlaybackState.Paused }));
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.False(await _store.OnSnapshotAsync(track with { State = PlaybackState.Paused }));

        Assert.True(await _store.OnSnapshotAsync(track with { Title = "Other", State = PlaybackState.Paused }));
        Assert.True(await _store.OnSnapshotAsync(track with { Title = "Other", State = PlaybackState.Paused }, force: true));
    }

    [Fact]
    public async Task Empty_titles_are_not_saved()
    {
        Assert.False(await _store.OnSnapshotAsync(Snapshots.Track() with { Title = "" }));
        Assert.False(await _store.OnSnapshotAsync(null));
    }

    [Fact]
    public async Task Tampered_artwork_name_is_ignored()
    {
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(_temp.Path, LastTrackStore.FileName),
            """{ "appId": "Spotify.exe", "title": "x", "artworkFile": "..\\..\\secret.txt" }""", ct);

        var loaded = await _store.LoadAsync(ct);

        Assert.NotNull(loaded);
        Assert.Null(loaded.Value.Artwork);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef.img", true)]
    [InlineData("0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef.img", false)]
    [InlineData("../evil.img", false)]
    [InlineData(null, false)]
    public void Artwork_names_are_strictly_validated(string? name, bool ok)
    {
        Assert.Equal(ok, LastTrackStore.IsSafeArtworkName(name));
    }
}

internal sealed class FakeLauncher(FakeMediaSource source, MediaSnapshot? appears) : IAppLauncher
{
    public List<string> Launched { get; } = [];

    public bool CanLaunch { get; set; } = true;

    public bool TryLaunch(string appId)
    {
        Launched.Add(appId);
        if (CanLaunch && appears is not null)
        {
            source.List.Add(appears);
            source.Raise();
        }

        return CanLaunch;
    }
}

public sealed class RestoreFlowTests
{
    private readonly FakeTimeProvider _time = new(Snapshots.T0);
    private readonly FakeMediaSource _source = new();

    private static MediaSnapshot Saved(string app = "Spotify.exe") =>
        Snapshots.Track(app, PlaybackState.Paused, positionSeconds: 100);

    [Fact]
    public async Task Existing_session_is_played_and_seeked()
    {
        _source.List.Add(Snapshots.Track(state: PlaybackState.Paused, positionSeconds: 0));
        var flow = new RestoreFlow(_source, new FakeLauncher(_source, null), _time);

        Assert.Equal(RestoreOutcome.Resumed, await flow.ResumeAsync(Saved(), TestContext.Current.CancellationToken));
        Assert.Equal(["play Spotify.exe", "seek Spotify.exe 100"], _source.Commands);
    }

    [Fact]
    public async Task Closed_app_is_launched_and_its_session_awaited()
    {
        var launcher = new FakeLauncher(_source, Snapshots.Track(state: PlaybackState.Paused, positionSeconds: 0));
        var flow = new RestoreFlow(_source, launcher, _time) { SettleDelay = TimeSpan.Zero };

        var outcome = await flow.ResumeAsync(Saved(), TestContext.Current.CancellationToken);

        Assert.Equal(["Spotify.exe"], launcher.Launched);
        Assert.Equal(RestoreOutcome.Resumed, outcome);
    }

    [Fact]
    public async Task Different_track_after_launch_is_played_without_seeking()
    {
        _source.List.Add(Snapshots.Track(state: PlaybackState.Paused) with { Title = "Another song" });
        var flow = new RestoreFlow(_source, new FakeLauncher(_source, null), _time);

        Assert.Equal(RestoreOutcome.ResumedWithoutPosition, await flow.ResumeAsync(Saved(), TestContext.Current.CancellationToken));
        Assert.Equal(["play Spotify.exe"], _source.Commands);
    }

    [Fact]
    public async Task Missing_session_times_out_after_fifteen_seconds()
    {
        var flow = new RestoreFlow(_source, new FakeLauncher(_source, null), _time);

        var resume = flow.ResumeAsync(Saved(), TestContext.Current.CancellationToken);
        _time.Advance(RestoreFlow.SessionTimeout);

        Assert.Equal(RestoreOutcome.TimedOut, await resume);
    }

    [Fact]
    public async Task Unlaunchable_app_reports_it()
    {
        var flow = new RestoreFlow(_source, new FakeLauncher(_source, null) { CanLaunch = false }, _time);
        Assert.Equal(RestoreOutcome.CannotLaunch, await flow.ResumeAsync(Saved(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("msedge.exe")]
    [InlineData("MSEdge")]
    [InlineData("firefox.exe")]
    [InlineData("Yandex.MCTNLS32EUOCCPUFOEQ65XIZAU")]
    public async Task Browsers_are_not_launched(string appId)
    {
        var launcher = new FakeLauncher(_source, null);
        var flow = new RestoreFlow(_source, launcher, _time);

        Assert.Equal(RestoreOutcome.Browser, await flow.ResumeAsync(Saved(appId), TestContext.Current.CancellationToken));
        Assert.Empty(launcher.Launched);
    }

    [Theory]
    [InlineData("ru.yandex.desktop.music", false)]
    [InlineData("Yandex.Music", false)]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", false)]
    [InlineData("Chrome", true)]
    public void Browser_detection(string appId, bool browser)
    {
        Assert.Equal(browser, RestoreFlow.IsBrowser(appId));
    }

    [Theory]
    [InlineData("Spotify.exe", true)]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", true)]
    [InlineData("calc.exe\" & del", false)]
    [InlineData(@"C:\Windows\System32\cmd.exe", false)]
    [InlineData("", false)]
    public void App_ids_are_validated_before_launching(string appId, bool plausible)
    {
        Assert.Equal(plausible, AppLauncher.IsPlausibleAppId(appId));
    }
}
