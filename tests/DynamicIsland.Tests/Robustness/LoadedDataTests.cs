using DynamicIsland.Core.Storage;
using DynamicIsland.Modules.Pomodoro.Services;
using DynamicIsland.Modules.Pomodoro.ViewModels;
using DynamicIsland.Modules.Todo.Services;
using DynamicIsland.Modules.Water.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DynamicIsland.Tests.Robustness;

/// <summary>Damaged or hand-edited data files must never crash the island.</summary>
public sealed class LoadedDataTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero));

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Pomodoro_impossible_values_are_reset()
    {
        var state = new PomodoroState
        {
            Status = PomodoroStatus.Running,
            EndsAtUtc = _time.GetUtcNow().AddYears(5),
            RemainingSeconds = 1e300,
            CompletedFocus = -4,
            CompletedToday = int.MaxValue,
        };

        PomodoroViewModel.Sanitize(state, _time.GetUtcNow());

        Assert.Equal(PomodoroStatus.Idle, state.Status);
        Assert.InRange(state.RemainingSeconds, 0, 3 * 3600);
        Assert.Equal(0, state.CompletedFocus);
        Assert.Equal(1000, state.CompletedToday);
        _ = new PomodoroTimer(state, _time).Remaining;
    }

    [Fact]
    public void Water_bad_entries_are_dropped_and_sums_cannot_overflow()
    {
        var state = new WaterState
        {
            Today = [new WaterEntry(_time.GetUtcNow(), int.MaxValue), null!, new WaterEntry(_time.GetUtcNow(), 250)],
        };
        state.History[new DateOnly(2026, 10, 26)] = -5;

        state.Normalize();

        Assert.Equal(250, new WaterTracker(state, _time).TotalMl);
        Assert.Empty(state.History);
    }

    [Fact]
    public void Todo_nulls_duplicates_and_huge_titles_are_cleaned()
    {
        var id = Guid.NewGuid();
        var state = new TodoState
        {
            Items =
            [
                null!,
                new TodoItem { Id = id, Title = "a" },
                new TodoItem { Id = id, Title = "duplicate" },
                new TodoItem { Title = "   " },
                new TodoItem { Title = new string('x', 100_000) },
            ],
        };

        var list = new TodoList(state, _time);

        Assert.Equal(2, list.Items.Count);
        Assert.All(list.Items, i => Assert.True(i.Title.Length <= TodoList.MaxTitleLength));
    }

    [Fact]
    public async Task Oversized_documents_are_treated_as_corrupt()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new JsonStorageService(_temp.Path, NullLogger<JsonStorageService>.Instance);
        var path = Path.Combine(_temp.Path, "big.json");
        await using (var f = File.Create(path))
        {
            f.SetLength(JsonStorageService.MaxDocumentBytes + 1);
        }

        Assert.Null(await storage.LoadAsync<TodoState>("big.json", ct));
        Assert.False(File.Exists(path));
    }
}
