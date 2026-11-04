using DynamicIsland.Core.Storage;
using DynamicIsland.Modules.Water;
using DynamicIsland.Tests.Pomodoro;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DynamicIsland.Tests.Water;

public sealed class WaterViewModelTests : IDisposable
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("UTC+5", TimeSpan.FromHours(5), "UTC+5", "UTC+5");
    private readonly TempFolder _temp = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 27, 5, 0, 0, TimeSpan.Zero));
    private readonly RecordingNotifications _notifications = new();
    private readonly JsonStorageService _storage;

    public WaterViewModelTests()
    {
        _time.SetLocalTimeZone(Zone);
        _storage = new JsonStorageService(_temp.Path, NullLogger<JsonStorageService>.Instance);
    }

    public void Dispose() => _temp.Dispose();

    private WaterViewModel NewVm() => new(_storage, _time, new InlineDispatcher(), _notifications);

    [Fact]
    public async Task Drinks_and_history_survive_a_restart()
    {
        using var vm = NewVm();
        await vm.LoadAsync(TestContext.Current.CancellationToken);
        await vm.DrinkCommand.ExecuteAsync(null);
        _time.Advance(TimeSpan.FromDays(1));
        await vm.RecheckAsync();
        await vm.DrinkCommand.ExecuteAsync(null);

        using var reopened = NewVm();
        await reopened.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(250, reopened.State.Today.Sum(e => e.Ml));
        Assert.Equal(250, reopened.State.History[new DateOnly(2026, 10, 27)]);
        Assert.Contains("0.3", reopened.TotalText.Replace(',', '.'), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reminder_fires_once_per_interval_with_sound_and_an_alert()
    {
        using var vm = NewVm();
        await vm.LoadAsync(TestContext.Current.CancellationToken);
        await vm.DrinkCommand.ExecuteAsync(null);

        _time.Advance(TimeSpan.FromMinutes(59));
        Assert.Empty(_notifications.Shown);
        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.Single(_notifications.Shown);
        Assert.True(_notifications.Shown[0].Sound);
        Assert.True(vm.Current?.IsAlert);
    }

    [Fact]
    public async Task Drinking_postpones_the_reminder()
    {
        using var vm = NewVm();
        await vm.LoadAsync(TestContext.Current.CancellationToken);
        await vm.DrinkCommand.ExecuteAsync(null);
        _time.Advance(TimeSpan.FromMinutes(50));
        await vm.DrinkCommand.ExecuteAsync(null);
        _time.Advance(TimeSpan.FromMinutes(30));

        Assert.Empty(_notifications.Shown);
    }
    [Fact]
    public async Task Legacy_serving_migrates_and_saved_portion_survives_restart_without_truncating_drinks()
    {
        var state = new DynamicIsland.Modules.Water.Services.WaterState
        {
            Day = new DateOnly(2026, 10, 27),
            StepMl = 1000,
            FineStepMl = 100,
        };
        state.Today.Add(new DynamicIsland.Modules.Water.Services.WaterEntry(_time.GetUtcNow(), 1000));
        await _storage.SaveAsync(WaterViewModel.FileName, state, TestContext.Current.CancellationToken);
        using var vm = NewVm();
        await vm.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(700, vm.State.StepMl);
        Assert.False(vm.CanIncreasePortion);
        Assert.True(vm.CanDecreasePortion);
        Assert.Equal(1000, Assert.Single(vm.State.Today).Ml);

        await vm.DecreaseCommand.ExecuteAsync(null);
        Assert.Equal(650, vm.State.StepMl);
        await vm.DrinkCommand.ExecuteAsync(null);
        Assert.Equal(1650, vm.State.Today.Sum(e => e.Ml));
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(1000, Assert.Single(vm.State.Today).Ml);

        using var reopened = NewVm();
        await reopened.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(650, reopened.State.StepMl);
        for (var i = 0; i < 20; i++)
        {
            await reopened.DecreaseCommand.ExecuteAsync(null);
        }

        Assert.Equal(50, reopened.State.StepMl);
        Assert.False(reopened.CanDecreasePortion);
    }

}
