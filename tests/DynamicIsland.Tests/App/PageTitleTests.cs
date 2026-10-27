using DynamicIsland.App.Island;

namespace DynamicIsland.Tests.App;

public sealed class PageTitleTests
{
    [Theory]
    [InlineData("Water Reminder", "Reminder", "Water ", "Reminder")]
    [InlineData("TodoList", "List", "Todo", "List")]
    [InlineData("Список задач", "задач", "Список ", "задач")]
    [InlineData("Pomodoro", "", "Pomodoro", "")]
    [InlineData("Water", "Reminder", "Water", "")]
    [InlineData("List", "List", "List", "")]
    public void Titles_split_into_white_and_coloured_parts(string full, string accent, string main, string coloured)
    {
        var (m, a) = ModuleHost.SplitTitle(full, accent);
        Assert.Equal(main, m);
        Assert.Equal(coloured, a);
    }
}
