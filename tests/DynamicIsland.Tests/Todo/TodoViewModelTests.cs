using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Storage;
using DynamicIsland.Modules.Todo;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DynamicIsland.Tests.Todo;

internal sealed class FakeHost : IIslandHost
{
    public int KeyboardRequests { get; private set; }

    public int KeyboardReleases { get; private set; }

    public void RequestKeyboard() => KeyboardRequests++;

    public void ReleaseKeyboard() => KeyboardReleases++;

    public void OpenSettings(string? moduleId = null)
    {
    }
}

public sealed class TodoViewModelTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeHost _host = new();
    private readonly JsonStorageService _storage;

    public TodoViewModelTests()
    {
        _storage = new JsonStorageService(_temp.Path, NullLogger<JsonStorageService>.Instance);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Adding_takes_the_keyboard_and_tasks_persist()
    {
        var vm = new TodoViewModel(_storage, _time, _host);
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.BeginAddCommand.Execute(null);
        vm.NewTitle = "Write tests";
        await vm.CommitAddCommand.ExecuteAsync(null);
        await vm.ToggleCommand.ExecuteAsync(vm.Items[0]);
        vm.CancelAddCommand.Execute(null);

        Assert.Equal(1, _host.KeyboardRequests);
        Assert.Equal(1, _host.KeyboardReleases);
        Assert.True(vm.Items[0].Done);

        var reopened = new TodoViewModel(_storage, _time, _host);
        await reopened.LoadAsync(TestContext.Current.CancellationToken);
        var item = Assert.Single(reopened.Items);
        Assert.True(item.Done);
        Assert.Equal("Write tests", item.Title);
    }

    [Fact]
    public async Task Rename_and_delete()
    {
        var vm = new TodoViewModel(_storage, _time, _host);
        await vm.LoadAsync(TestContext.Current.CancellationToken);
        vm.NewTitle = "a";
        await vm.CommitAddCommand.ExecuteAsync(null);

        var row = vm.Items[0];
        vm.BeginRenameCommand.Execute(row);
        row.Draft = "alpha";
        await vm.CommitRenameCommand.ExecuteAsync(row);
        Assert.Equal("alpha", vm.Items[0].Title);

        await vm.DeleteCommand.ExecuteAsync(vm.Items[0]);
        Assert.True(vm.IsEmpty);
    }
}
