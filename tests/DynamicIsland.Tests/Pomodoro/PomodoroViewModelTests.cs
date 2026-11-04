using DynamicIsland.Core.Island;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Storage;
using DynamicIsland.Modules.Pomodoro.Services;
using DynamicIsland.Modules.Pomodoro.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DynamicIsland.Tests.Pomodoro;

internal sealed class RecordingNotifications : INotificationService
{
    public List<(string Title, bool Sound)> Shown { get; } = [];

    public void Show(string title, string message, bool sound) => Shown.Add((title, sound));
}

internal sealed class RecordingAlertAudio : IAlertAudio
{
    public List<AlertSound> Played { get; } = [];
    public bool Succeeds { get; set; } = true;
    public Task<bool> PlayAsync(AlertSound sound, CancellationToken cancellationToken = default)
    {
        Played.Add(sound);
        return Task.FromResult(Succeeds);
    }
}

public sealed class PomodoroViewModelTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    private readonly RecordingNotifications _notifications = new();
    private readonly RecordingAlertAudio _audio = new();
    private readonly JsonStorageService _storage;
    private readonly IslandStateMachine _island;
    private readonly PomodoroViewModel _vm;

    public PomodoroViewModelTests()
    {
        _storage = new JsonStorageService(_temp.Path, NullLogger<JsonStorageService>.Instance);
        _island = new IslandStateMachine(_time, new InlineDispatcher());
        _vm = new PomodoroViewModel(_storage, _time, new InlineDispatcher(), _island, _notifications, _audio);
    }

    public void Dispose()
    {
        _vm.Dispose();
        _island.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task Start_counts_down_and_raises_a_live_activity()
    {
        await _vm.LoadAsync(TestContext.Current.CancellationToken);
        await _vm.StartPauseCommand.ExecuteAsync(null);
        _time.Advance(TimeSpan.FromSeconds(61));

        Assert.True(_vm.IsRunning);
        Assert.Equal("23:59", _vm.RemainingText);
        Assert.Equal(LiveActivityPriority.TimerRunning, _vm.Current?.Priority);
    }

    [Fact]
    public async Task Phase_end_notifies_with_sound_and_shows_an_alert()
    {
        await _vm.LoadAsync(TestContext.Current.CancellationToken);
        await _vm.StartPauseCommand.ExecuteAsync(null);
        _time.Advance(TimeSpan.FromMinutes(25));

        Assert.Single(_notifications.Shown);
        Assert.False(_notifications.Shown[0].Sound);
        Assert.Equal(AlertSound.Pomodoro, Assert.Single(_audio.Played));
        Assert.True(_vm.Current?.IsAlert);
        Assert.Equal(PomodoroPhase.ShortBreak, _vm.Phase);
        Assert.Equal(1, _vm.CompletedToday);

        _time.Advance(TimeSpan.FromSeconds(6));
        Assert.Null(_vm.Current);
    }

    [Fact]
    public async Task Recheck_does_not_repeat_completion_audio_and_sound_can_be_disabled()
    {
        await _vm.LoadAsync(TestContext.Current.CancellationToken);
        await _vm.StartPauseCommand.ExecuteAsync(null);
        _time.Advance(TimeSpan.FromMinutes(25));
        _vm.RecheckSoon();
        _vm.RecheckSoon();
        Assert.Single(_audio.Played);
        await _vm.ApplySettingsAsync(new PomodoroSettings { Sound = false });
        await _vm.StartPauseCommand.ExecuteAsync(null);
        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.Single(_audio.Played);
        Assert.Equal(2, _notifications.Shown.Count);
    }

    [Fact]
    public async Task Audio_failure_keeps_the_visible_notification_and_reports_failure()
    {
        _audio.Succeeds = false;
        await _vm.LoadAsync(TestContext.Current.CancellationToken);
        await _vm.StartPauseCommand.ExecuteAsync(null);
        _time.Advance(TimeSpan.FromMinutes(25));
        Assert.Single(_audio.Played);
        Assert.Equal(2, _notifications.Shown.Count);
        Assert.True(_vm.Current?.IsAlert);
    }

    [Fact]
    public async Task Running_timer_is_restored_from_disk()
    {
        await _vm.LoadAsync(TestContext.Current.CancellationToken);
        await _vm.StartPauseCommand.ExecuteAsync(null);
        _time.Advance(TimeSpan.FromMinutes(5));

        using var reopened = new PomodoroViewModel(_storage, _time, new InlineDispatcher(), _island, _notifications, _audio);
        await reopened.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(reopened.IsRunning);
        Assert.Equal("20:00", reopened.RemainingText);
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(0.2, "00:01")]
    [InlineData(1500, "25:00")]
    [InlineData(3725, "1:02:05")]
    public void Countdown_rounds_up(double seconds, string expected)
    {
        Assert.Equal(expected, PomodoroViewModel.Format(TimeSpan.FromSeconds(seconds)));
    }
    [Fact]
    public async Task Island_duration_picker_applies_to_next_session_and_survives_restart()
    {
        await _vm.LoadAsync(TestContext.Current.CancellationToken);
        _vm.DurationMinutes = 37;
        // The property starts persistence asynchronously; await an explicit flush for this test.
        await _vm.SaveAsync();
        Assert.Equal(37, _vm.Settings.FocusMinutes);
        Assert.Equal("37:00", _vm.RemainingText);
        await _vm.StartPauseCommand.ExecuteAsync(null);
        _time.Advance(TimeSpan.FromMinutes(5));
        _vm.DurationMinutes = 42;
        await _vm.SaveAsync();
        Assert.Equal("32:00", _vm.RemainingText);
        Assert.Equal(5.0 / 37, _vm.Progress, 6);

        using var reopened = new PomodoroViewModel(_storage, _time, new InlineDispatcher(), _island, _notifications, _audio);
        await reopened.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(42, reopened.DurationMinutes);
        Assert.Equal("32:00", reopened.RemainingText);
        Assert.Equal(_vm.Progress, reopened.Progress);
        await reopened.ResetCommand.ExecuteAsync(null);
        Assert.Equal("42:00", reopened.RemainingText);
    }

    [Theory]
    [InlineData(PomodoroPhase.Focus, 180)]
    [InlineData(PomodoroPhase.ShortBreak, 60)]
    [InlineData(PomodoroPhase.LongBreak, 120)]
    public async Task Island_duration_picker_uses_phase_limits(PomodoroPhase phase, int maximum)
    {
        await _vm.LoadAsync(TestContext.Current.CancellationToken);
        await _vm.SelectPhaseCommand.ExecuteAsync(phase);
        Assert.Equal(maximum, _vm.MaximumDurationMinutes);
        _vm.DurationMinutes = int.MaxValue;
        Assert.Equal(maximum, _vm.DurationMinutes);
        _vm.DurationMinutes = 0;
        Assert.Equal(1, _vm.DurationMinutes);
        await _vm.SaveAsync();
    }

}
