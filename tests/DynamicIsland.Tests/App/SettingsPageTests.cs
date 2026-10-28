using DynamicIsland.Core.Island;
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
