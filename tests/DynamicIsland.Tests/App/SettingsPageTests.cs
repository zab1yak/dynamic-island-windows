using DynamicIsland.App.Island;
using DynamicIsland.Core.Input;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DynamicIsland.Tests.App;

public sealed class SettingsPageTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly IslandStateMachine _island;
    private readonly TempFolder _temp = new();

    public SettingsPageTests()
    {
        _island = new IslandStateMachine(_time, new InlineDispatcher());
    }

    public void Dispose()
    {
        _island.Dispose();
        _temp.Dispose();
    }

    private sealed class FakeModule(string id, int order) : IIslandModule
    {
        public string Id => id;

        public string TitleKey => id;

        public string IconKey => "Circle";

        public int Order => order;

        public object ExpandedViewModel => this;

        public object? SettingsViewModel => null;

        public ILiveActivitySource? LiveActivities => null;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ShutdownAsync() => Task.CompletedTask;
    }

    [Fact]
    public void Locked_island_ignores_hover_clicks_hotkey_and_drags()
    {
        _island.SetLocked(true);

        _island.PointerEntered();
        _time.Advance(TimeSpan.FromSeconds(1));
        _island.Clicked();
        _island.HotkeyPressed();
        _island.DragNear();

        Assert.Equal(IslandMode.Compact, _island.Mode);
    }

    [Fact]
    public void Locking_collapses_an_open_island_and_unlocking_restores_hover()
    {
        _island.Clicked();
        Assert.Equal(IslandMode.Pinned, _island.Mode);

        _island.SetLocked(true);
        Assert.Equal(IslandMode.Compact, _island.Mode);

        _island.SetLocked(false);
        _island.PointerExited();
        _island.PointerEntered();
        _time.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Equal(IslandMode.Expanded, _island.Mode);
    }

    [Fact]
    public void Settings_page_is_always_last_and_cannot_be_hidden()
    {
        IIslandModule[] modules = [new FakeModule(ModuleHost.SettingsPageId, int.MaxValue), new FakeModule("media", 0), new FakeModule("water", 2)];
        var settings = new AppSettings
        {
            ModuleOrder = [ModuleHost.SettingsPageId, "water", "media"],
            DisabledModules = [ModuleHost.SettingsPageId],
        };

        var pages = ModuleHost.Order(modules, settings);

        Assert.Equal(["water", "media", ModuleHost.SettingsPageId], pages.Select(m => m.Id));
    }

    [Fact]
    public void Quit_needs_a_second_press_within_four_seconds()
    {
        var gate = new ConfirmGate(_time);
        Assert.False(gate.Press());
        Assert.True(gate.IsArmed);
        Assert.True(gate.Press());

        Assert.False(gate.Press());
        _time.Advance(ConfirmGate.Window);
        Assert.False(gate.IsArmed);
        Assert.False(gate.Press());
    }

    [Fact]
    public async Task Background_and_lock_are_saved_and_read_back()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new JsonStorageService(_temp.Path, NullLogger<JsonStorageService>.Instance);
        var service = new SettingsService(storage, NullLogger<SettingsService>.Instance);
        await service.LoadAsync(ct);
        Assert.Equal(IslandBackground.Black, service.Current.Background);
        Assert.False(service.Current.IslandLocked);

        service.Current.Background = IslandBackground.LightGray;
        service.Current.IslandLocked = true;
        await service.SaveAsync(ct);

        var again = new SettingsService(storage, NullLogger<SettingsService>.Instance);
        await again.LoadAsync(ct);
        Assert.Equal(IslandBackground.LightGray, again.Current.Background);
        Assert.True(again.Current.IslandLocked);
    }
}
