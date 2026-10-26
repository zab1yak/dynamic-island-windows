using System.Text.Json.Nodes;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicIsland.Tests.Settings;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly JsonStorageService _storage;

    public SettingsServiceTests()
    {
        _storage = new JsonStorageService(_temp.Path, NullLogger<JsonStorageService>.Instance);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Missing_file_gives_defaults()
    {
        var service = new SettingsService(_storage, NullLogger<SettingsService>.Instance);
        await service.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(service.Current.LaunchAtStartup);
        Assert.Equal("Ctrl+Alt+Space", service.Current.ToggleHotkey);
        Assert.Equal(150, service.Current.HoverExpandDelayMs);
    }

    [Fact]
    public async Task Save_and_reload_keeps_values_and_raises_Changed()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new SettingsService(_storage, NullLogger<SettingsService>.Instance);
        await service.LoadAsync(ct);
        var raised = false;
        service.Changed += (_, _) => raised = true;

        service.Current.LaunchAtStartup = false;
        service.Current.ModuleOrder = ["todo", "media", "todo"];
        await service.SaveAsync(ct);

        var reloaded = new SettingsService(_storage, NullLogger<SettingsService>.Instance);
        await reloaded.LoadAsync(ct);
        Assert.True(raised);
        Assert.False(reloaded.Current.LaunchAtStartup);
        Assert.Equal(["todo", "media"], reloaded.Current.ModuleOrder);
    }

    [Fact]
    public async Task Out_of_range_values_are_clamped()
    {
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(_temp.Path, SettingsService.FileName),
            """{ "schemaVersion": 1, "islandScale": 9, "hoverExpandDelayMs": -5, "language": "xx", "toggleHotkey": "" }""", ct);

        var service = new SettingsService(_storage, NullLogger<SettingsService>.Instance);
        await service.LoadAsync(ct);

        Assert.Equal(1.3, service.Current.IslandScale);
        Assert.Equal(0, service.Current.HoverExpandDelayMs);
        Assert.Null(service.Current.Language);
        Assert.Equal("Ctrl+Alt+Space", service.Current.ToggleHotkey);
    }

    [Fact]
    public async Task Old_schema_is_migrated_and_saved()
    {
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(_temp.Path, SettingsService.FileName),
            """{ "autostart": false }""", ct);
        var migrations = new Dictionary<int, Action<JsonObject>>
        {
            [0] = json =>
            {
                json["launchAtStartup"] = json["autostart"]?.GetValue<bool>();
                json.Remove("autostart");
            },
        };

        var service = new SettingsService(_storage, NullLogger<SettingsService>.Instance, migrations);
        await service.LoadAsync(ct);

        Assert.False(service.Current.LaunchAtStartup);
        var onDisk = await File.ReadAllTextAsync(Path.Combine(_temp.Path, SettingsService.FileName), ct);
        Assert.Contains("\"schemaVersion\": 1", onDisk, StringComparison.Ordinal);
        Assert.DoesNotContain("autostart", onDisk, StringComparison.Ordinal);
    }
}
