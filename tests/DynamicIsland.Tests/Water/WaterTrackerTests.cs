using DynamicIsland.Modules.Water.Services;
using Microsoft.Extensions.Time.Testing;

namespace DynamicIsland.Tests.Water;

public sealed class WaterTrackerTests
{
    // 10:00 local in UTC+5 (the developer's zone); FakeTimeProvider lets us fix the zone too.
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("UTC+5", TimeSpan.FromHours(5), "UTC+5", "UTC+5");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 27, 5, 0, 0, TimeSpan.Zero));

    public WaterTrackerTests()
    {
        _time.SetLocalTimeZone(Zone);
    }

    private WaterTracker NewTracker(WaterState? state = null)
    {
        var s = state ?? new WaterState();
        s.Normalize();
        return new WaterTracker(s, _time);
    }

    [Fact]
    public void Drink_adds_a_portion_and_undo_removes_it()
    {
        var tracker = NewTracker();
        tracker.Drink();
        tracker.Drink();
        Assert.Equal(500, tracker.TotalMl);
        Assert.Equal(0.25, tracker.Progress);

        Assert.True(tracker.Undo());
        Assert.Equal(250, tracker.TotalMl);
        Assert.True(tracker.Undo());
        Assert.False(tracker.Undo());
    }

    [Fact]
    public void Local_midnight_moves_today_into_history()
    {
        var tracker = NewTracker();
        tracker.Drink();
        tracker.Drink();
        tracker.Drink();

        _time.Advance(TimeSpan.FromHours(14)); // 00:00 local next day
        Assert.True(tracker.RollDay());

        Assert.Equal(0, tracker.TotalMl);
        Assert.Equal(750, tracker.State.History[new DateOnly(2026, 10, 27)]);
    }

    [Fact]
    public void Just_before_midnight_is_still_today()
    {
        var tracker = NewTracker();
        tracker.Drink();
        _time.Advance(TimeSpan.FromHours(13.99));
        Assert.False(tracker.RollDay());
        Assert.Equal(250, tracker.TotalMl);
    }

    [Fact]
    public void Old_history_is_trimmed()
    {
        var state = new WaterState();
        state.History[new DateOnly(2026, 1, 1)] = 1000;
        state.History[new DateOnly(2026, 10, 20)] = 1500;
        var tracker = NewTracker(state);

        tracker.RollDay();

        Assert.False(state.History.ContainsKey(new DateOnly(2026, 1, 1)));
        Assert.True(state.History.ContainsKey(new DateOnly(2026, 10, 20)));
    }

    [Fact]
    public void Reminder_comes_one_interval_after_the_last_drink()
    {
        var tracker = NewTracker();
        tracker.Drink();
        Assert.Equal(_time.GetUtcNow().AddMinutes(60), tracker.NextReminderAt());
    }

    [Fact]
    public void No_reminder_when_goal_reached_or_disabled()
    {
        var tracker = NewTracker(new WaterState { GoalMl = 500 });
        tracker.Drink();
        tracker.Drink();
        Assert.Null(tracker.NextReminderAt());

        var off = NewTracker(new WaterState { RemindersEnabled = false });
        Assert.Null(off.NextReminderAt());
    }

    [Fact]
    public void Reminders_wait_for_active_hours()
    {
        var early = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 1, 0, 0, TimeSpan.Zero)); // 06:00 local
        early.SetLocalTimeZone(Zone);
        var state = new WaterState();
        state.Normalize();
        var tracker = new WaterTracker(state, early);

        var due = tracker.NextReminderAt();

        // Nothing drunk yet: the interval counts from the start of the active hours (09:00).
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.FromHours(5)), due);
    }

    [Fact]
    public void Late_evening_reminder_moves_to_next_morning()
    {
        _time.Advance(TimeSpan.FromHours(11.5)); // 21:30 local
        var tracker = NewTracker();
        tracker.Drink();

        var due = tracker.NextReminderAt();

        Assert.Equal(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.FromHours(5)), due);
    }

    [Fact]
    public void Reminder_interval_counts_from_the_last_reminder_too()
    {
        var tracker = NewTracker();
        tracker.Drink();
        _time.Advance(TimeSpan.FromMinutes(60));
        tracker.MarkReminded();

        Assert.Equal(_time.GetUtcNow().AddMinutes(60), tracker.NextReminderAt());
    }

    [Fact]
    public void Not_before_delays_the_first_reminder()
    {
        var tracker = NewTracker();
        tracker.NotBefore = _time.GetUtcNow().AddMinutes(60);

        Assert.Equal(_time.GetUtcNow().AddMinutes(60), tracker.NextReminderAt());
    }

    [Fact]
    public void Settings_are_clamped()
    {
        var state = new WaterState { GoalMl = 1, StepMl = 99999, ReminderIntervalMinutes = 1, ActiveFromHour = 22, ActiveToHour = 3 };
        state.Normalize();
        Assert.Equal(250, state.GoalMl);
        Assert.Equal(2000, state.StepMl);
        Assert.Equal(15, state.ReminderIntervalMinutes);
        Assert.Equal(23, state.ActiveToHour);
    }
}

public sealed class WaterAdjustTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 27, 5, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Plus_and_minus_change_by_the_fine_step_and_never_go_below_zero()
    {
        var state = new WaterState();
        state.Normalize();
        var tracker = new WaterTracker(state, _time);
        Assert.Equal(50, state.FineStepMl);

        tracker.Drink();                 // 250
        tracker.Adjust(state.FineStepMl); // 300
        Assert.Equal(300, tracker.TotalMl);

        tracker.Adjust(-state.FineStepMl); // 250
        tracker.Adjust(-200);              // 50
        Assert.Equal(50, tracker.TotalMl);

        tracker.Adjust(-500);
        Assert.Equal(0, tracker.TotalMl);
        Assert.Empty(state.Today);
    }

    [Fact]
    public void Fine_step_is_clamped_and_defaults_to_50()
    {
        var state = new WaterState { FineStepMl = 0 };
        state.Normalize();
        Assert.Equal(50, state.FineStepMl);
        state.FineStepMl = 99999;
        state.Normalize();
        Assert.Equal(500, state.FineStepMl);
    }
}
