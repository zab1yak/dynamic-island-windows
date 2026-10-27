namespace DynamicIsland.Modules.Todo.Services;

/// <summary>One task.</summary>
public sealed class TodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public bool Done { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DoneAt { get; set; }
}

/// <summary>Persisted list (<c>todo.json</c>), in display order.</summary>
public sealed class TodoState
{
    public int SchemaVersion { get; set; } = 1;

    public List<TodoItem> Items { get; set; } = [];
}

/// <summary>Task list rules. Titles are trimmed and bounded; empty titles are ignored.</summary>
public sealed class TodoList(TodoState state, TimeProvider time)
{
    /// <summary>Longest title kept.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>Most tasks kept (older completed ones are dropped first).</summary>
    public const int MaxItems = 500;

    public TodoState State { get; } = Sanitized(state);

    /// <summary>Cleans a loaded list: no nulls, clean titles, unique ids, bounded size.</summary>
    private static TodoState Sanitized(TodoState state)
    {
        state.Items ??= [];
        var seen = new HashSet<Guid>();
        state.Items.RemoveAll(i => i is null || Clean(i.Title) is null || !seen.Add(i.Id));
        foreach (var item in state.Items)
        {
            item.Title = Clean(item.Title)!;
        }

        if (state.Items.Count > MaxItems)
        {
            state.Items.RemoveRange(MaxItems, state.Items.Count - MaxItems);
        }

        return state;
    }

    public IReadOnlyList<TodoItem> Items => State.Items;

    /// <summary>Tasks completed today (local date).</summary>
    public int DoneToday
    {
        get
        {
            var today = time.GetLocalNow().Date;
            return State.Items.Count(i => i.Done && i.DoneAt is { } at && at.ToOffset(time.GetLocalNow().Offset).Date == today);
        }
    }

    /// <summary>Adds a task at the top of the open tasks. Returns it, or <c>null</c> for an empty title.</summary>
    public TodoItem? Add(string? title)
    {
        var clean = Clean(title);
        if (clean is null)
        {
            return null;
        }

        var item = new TodoItem { Title = clean, CreatedAt = time.GetUtcNow() };
        State.Items.Insert(0, item);
        Trim();
        return item;
    }

    /// <summary>Renames a task; an empty title leaves it unchanged.</summary>
    public bool Rename(Guid id, string? title)
    {
        var clean = Clean(title);
        if (clean is null || Find(id) is not { } item)
        {
            return false;
        }

        item.Title = clean;
        return true;
    }

    /// <summary>Toggles completion. Completed tasks sink below the open ones.</summary>
    public bool Toggle(Guid id)
    {
        if (Find(id) is not { } item)
        {
            return false;
        }

        item.Done = !item.Done;
        item.DoneAt = item.Done ? time.GetUtcNow() : null;
        State.Items.Remove(item);
        if (item.Done)
        {
            State.Items.Add(item);
        }
        else
        {
            var firstDone = State.Items.FindIndex(i => i.Done);
            State.Items.Insert(firstDone < 0 ? State.Items.Count : firstDone, item);
        }

        return true;
    }

    /// <summary>Deletes a task.</summary>
    public bool Remove(Guid id) => Find(id) is { } item && State.Items.Remove(item);

    /// <summary>Moves a task to <paramref name="newIndex"/> (drag-to-reorder).</summary>
    public bool Move(Guid id, int newIndex)
    {
        if (Find(id) is not { } item)
        {
            return false;
        }

        State.Items.Remove(item);
        State.Items.Insert(Math.Clamp(newIndex, 0, State.Items.Count), item);
        return true;
    }

    private TodoItem? Find(Guid id) => State.Items.FirstOrDefault(i => i.Id == id);

    private static string? Clean(string? title)
    {
        var clean = string.Join(' ', (title ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0)
        {
            return null;
        }

        return clean.Length > MaxTitleLength ? clean[..MaxTitleLength] : clean;
    }

    private void Trim()
    {
        while (State.Items.Count > MaxItems)
        {
            // Drop the task completed longest ago first, then the oldest open one.
            var victim = State.Items.Where(i => i.Done).MinBy(i => i.DoneAt) ?? State.Items[^1];
            State.Items.Remove(victim);
        }
    }
}
