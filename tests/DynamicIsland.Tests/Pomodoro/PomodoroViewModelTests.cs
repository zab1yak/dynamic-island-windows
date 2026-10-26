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

public sealed class PomodoroViewModelTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    private readonly RecordingNotifications _notifications = new();
    private readonly JsonStorageService _storage;
    private readonly IslandStateMachine _island;
    private readonly PomodoroViewModel _vm;

    public PomodoroViewModelTests()
    {
        _storage = new JsonStorageService(_temp.Path, NullLogger<JsonStorageService>.Instance);
        _island = new IslandStateMachine(_time, new InlineDispatcher());
        _vm = new PomodoroViewModel(_storage, _time, new InlineDispatcher(), _island, _notifications);
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
        Assert.True(_notifications.Shown[0].Sound);
        Assert.True(_vm.Current?.IsAlert);
        Assert.Equal(PomodoroPhase.ShortBreak, _vm.Phase);
        Assert.Equal(1, _vm.CompletedToday);

        _time.Advance(TimeSpan.FromSeconds(6));
        Assert.Null(_vm.Current);
    }

    [Fact]
    public async Task Running_timer_is_restored_from_disk()
    {
        await _vm.LoadAsync(TestContext.Current.CancellationToken);
        await _vm.StartPauseCommand.ExecuteAsync(null);
        _time.Advance(TimeSpan.FromMinutes(5));

        using var reopened = new PomodoroViewModel(_storage, _time, new InlineDispatcher(), _island, _notifications);
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
}
