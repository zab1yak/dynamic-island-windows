namespace DynamicIsland.Modules.Water.Services;

/// <summary>Persisted water data (<c>water.json</c>).</summary>
public sealed class WaterState
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Daily goal in millilitres.</summary>
    public int GoalMl { get; set; } = 2000;

    /// <summary>Smallest portion the user can pick.</summary>
    public const int MinPortionMl = 50;

    /// <summary>Largest portion the user can pick.</summary>
    public const int MaxPortionMl = 1000;

    /// <summary>The portion the user picked with − / +; "Drink" adds it (250 ml by default).</summary>
    public int StepMl { get; set; } = 250;

    /// <summary>How much − and + change the picked portion (50 ml by default).</summary>
    public int FineStepMl { get; set; } = 50;

    /// <summary>Remind to drink.</summary>
    public bool RemindersEnabled { get; set; } = true;

    /// <summary>Minutes between reminders.</summary>
    public int ReminderIntervalMinutes { get; set; } = 60;

    /// <summary>Reminders only between these local hours.</summary>
    public int ActiveFromHour { get; set; } = 9;

    /// <summary>End hour (exclusive) of the reminder window.</summary>
    public int ActiveToHour { get; set; } = 22;

    /// <summary>Local date the <see cref="Today"/> entries belong to.</summary>
    public DateOnly? Day { get; set; }

    /// <summary>Today's drinks, oldest first (UTC time, millilitres). Used for undo.</summary>
    public List<WaterEntry> Today { get; set; } = [];

    /// <summary>Totals of past days.</summary>
    public SortedDictionary<DateOnly, int> History { get; set; } = [];

    /// <summary>Last reminder shown (UTC).</summary>
    public DateTimeOffset? LastReminderAt { get; set; }

    /// <summary>Clamps values to sane ranges.</summary>
    public void Normalize()
    {
        GoalMl = Math.Clamp(GoalMl, 250, 10_000);
        StepMl = Math.Clamp(StepMl, MinPortionMl, MaxPortionMl);
        FineStepMl = Math.Clamp(FineStepMl == 0 ? 50 : FineStepMl, 10, 500);
        ReminderIntervalMinutes = Math.Clamp(ReminderIntervalMinutes, 15, 480);
        ActiveFromHour = Math.Clamp(ActiveFromHour, 0, 23);
        ActiveToHour = Math.Clamp(ActiveToHour, ActiveFromHour + 1, 24);
        Today ??= [];
        History ??= [];
        // A hand-edited or damaged file must not make sums overflow or the UI crawl.
        Today.RemoveAll(e => e is null || e.Ml is < 1 or > 5000);
        if (Today.Count > 500)
        {
            Today.RemoveRange(0, Today.Count - 500);
        }

        foreach (var day in History.Where(kv => kv.Value is < 0 or > 100_000).Select(kv => kv.Key).ToList())
        {
            History.Remove(day);
        }
    }
}

/// <summary>One drink.</summary>
public sealed record WaterEntry(DateTimeOffset At, int Ml);

/// <summary>Water intake rules: daily total, undo, midnight reset with history, reminder schedule.</summary>
public sealed class WaterTracker(WaterState state, TimeProvider time)
{
    /// <summary>Days of history kept.</summary>
    public const int HistoryDays = 90;

    /// <summary>The state being driven (persist it after every change).</summary>
    public WaterState State { get; } = state;

    /// <summary>No reminder before this moment (set to start-up + interval, so launching the app never nags at once).</summary>
    public DateTimeOffset? NotBefore { get; set; }

    /// <summary>Millilitres drunk today.</summary>
    public int TotalMl => State.Today.Sum(e => e.Ml);

    /// <summary>Share of the goal, 0–1 (capped).</summary>
    public double Progress => Math.Clamp(TotalMl / (double)State.GoalMl, 0, 1);

    /// <summary>Whether the goal is reached.</summary>
    public bool GoalReached => TotalMl >= State.GoalMl;

    /// <summary>Adds the picked portion to today's total.</summary>
    public void Drink()
    {
        RollDay();
        State.Today.Add(new WaterEntry(time.GetUtcNow(), State.StepMl));
    }

    /// <summary>
    /// Changes the picked portion by one step up (<paramref name="steps"/> &gt; 0) or down, within
    /// <see cref="WaterState.MinPortionMl"/>–<see cref="WaterState.MaxPortionMl"/>. Today's total is not touched.
    /// </summary>
    public void ChangePortion(int steps)
    {
        State.StepMl = Math.Clamp(State.StepMl + (steps * State.FineStepMl), WaterState.MinPortionMl, WaterState.MaxPortionMl);
    }

    /// <summary>Share of the goal in whole percent, never above 100.</summary>
    public int Percent => (int)Math.Round(Progress * 100, MidpointRounding.AwayFromZero);

    /// <summary>Removes the last portion of today. Returns <c>false</c> when there is nothing to undo.</summary>
    public bool Undo()
    {
        RollDay();
        if (State.Today.Count == 0)
        {
            return false;
        }

        State.Today.RemoveAt(State.Today.Count - 1);
        return true;
    }

    /// <summary>
    /// Moves a finished day into history (local midnight). Safe to call any time: after sleep,
    /// after a time-zone change, on start-up. Returns <c>true</c> when the day changed.
    /// </summary>
    public bool RollDay()
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        if (State.Day == today)
        {
            return false;
        }

        if (State.Day is { } previous && State.Today.Count > 0)
        {
            State.History[previous] = State.Today.Sum(e => e.Ml);
        }

        State.Day = today;
        State.Today.Clear();
        State.LastReminderAt = null;
        foreach (var old in State.History.Keys.Where(d => d < today.AddDays(-HistoryDays)).ToList())
        {
            State.History.Remove(old);
        }

        return true;
    }

    /// <summary>Time until the next local midnight.</summary>
    public TimeSpan UntilMidnight()
    {
        var now = time.GetLocalNow();
        var midnight = new DateTimeOffset(now.Date.AddDays(1), now.Offset);
        var left = midnight - now;
        return left > TimeSpan.Zero ? left : TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// When the next reminder is due (UTC), or <c>null</c> when reminders are off or the goal is met.
    /// The interval counts from the last drink or the last reminder, whichever is later, and the
    /// result is moved into the active hours.
    /// </summary>
    public DateTimeOffset? NextReminderAt()
    {
        RollDay();
        if (!State.RemindersEnabled || GoalReached)
        {
            return null;
        }

        var now = time.GetLocalNow();
        var interval = TimeSpan.FromMinutes(State.ReminderIntervalMinutes);
        var anchor = new[] { State.Today.LastOrDefault()?.At, State.LastReminderAt }.Max() ?? DayStart(now);
        var due = anchor.ToOffset(now.Offset) + interval;
        if (due < now)
        {
            due = now;
        }

        if (NotBefore is { } notBefore && due < notBefore)
        {
            due = notBefore.ToOffset(now.Offset);
        }

        var from = new DateTimeOffset(due.Date.AddHours(State.ActiveFromHour), now.Offset);
        var to = new DateTimeOffset(due.Date.AddHours(State.ActiveToHour), now.Offset);
        if (due < from)
        {
            due = from;
        }
        else if (due >= to)
        {
            // Next morning; the day rolls over before that, so this only matters for scheduling.
            due = from.AddDays(1);
        }

        return due.ToUniversalTime();
    }

    /// <summary>Records that a reminder was shown now.</summary>
    public void MarkReminded() => State.LastReminderAt = time.GetUtcNow();

    private DateTimeOffset DayStart(DateTimeOffset now) => new(now.Date.AddHours(State.ActiveFromHour), now.Offset);
}
