using DynamicIsland.Modules.Todo.Services;
using Microsoft.Extensions.Time.Testing;

namespace DynamicIsland.Tests.Todo;

public sealed class TodoListTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero));

    private TodoList NewList() => new(new TodoState(), _time);

    [Fact]
    public void Add_trims_collapses_spaces_and_ignores_empty()
    {
        var list = NewList();
        Assert.Null(list.Add("   "));
        Assert.Null(list.Add(null));
        var item = list.Add("  buy   milk \n");
        Assert.Equal("buy milk", item!.Title);
        Assert.Single(list.Items);
    }

    [Fact]
    public void New_tasks_go_on_top()
    {
        var list = NewList();
        list.Add("first");
        list.Add("second");
        Assert.Equal(["second", "first"], list.Items.Select(i => i.Title));
    }

    [Fact]
    public void Long_titles_are_cut()
    {
        Assert.Equal(TodoList.MaxTitleLength, NewList().Add(new string('x', 1000))!.Title.Length);
    }

    [Fact]
    public void Completed_tasks_sink_and_count_for_today()
    {
        var list = NewList();
        var a = list.Add("a")!;
        list.Add("b");
        list.Add("c");

        list.Toggle(a.Id);
        Assert.Equal(["c", "b", "a"], list.Items.Select(i => i.Title));
        Assert.Equal(1, list.DoneToday);

        var c = list.Items[0];
        list.Toggle(c.Id);
        Assert.Equal(["b", "a", "c"], list.Items.Select(i => i.Title));

        // Reopened tasks return above the completed ones.
        list.Toggle(a.Id);
        Assert.Equal(["b", "a", "c"], list.Items.Select(i => i.Title));
        Assert.False(list.Items[1].Done);
        Assert.Equal(1, list.DoneToday);
    }

    [Fact]
    public void Yesterday_completions_do_not_count_today()
    {
        var list = NewList();
        list.Toggle(list.Add("a")!.Id);
        _time.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, list.DoneToday);
    }

    [Fact]
    public void Rename_remove_and_move()
    {
        var list = NewList();
        var a = list.Add("a")!;
        var b = list.Add("b")!;

        Assert.True(list.Rename(a.Id, "alpha"));
        Assert.False(list.Rename(a.Id, " "));
        Assert.True(list.Move(b.Id, 5));
        Assert.Equal(["alpha", "b"], list.Items.Select(i => i.Title));

        Assert.True(list.Remove(a.Id));
        Assert.False(list.Remove(a.Id));
        Assert.Equal(["b"], list.Items.Select(i => i.Title));
    }

    [Fact]
    public void List_is_capped_dropping_completed_first()
    {
        var list = NewList();
        var done = list.Add("old done")!;
        list.Toggle(done.Id);
        for (var i = 0; i < TodoList.MaxItems; i++)
        {
            list.Add($"t{i}");
        }

        Assert.Equal(TodoList.MaxItems, list.Items.Count);
        Assert.DoesNotContain(list.Items, i => i.Id == done.Id);
    }

    [Fact]
    public void Shift_enter_line_breaks_are_kept_and_cleaned()
    {
        var item = NewList().Add("  buy \r\n\r\n  milk   and bread \n");
        Assert.Equal("buy\nmilk and bread", item!.Title);
    }
}
