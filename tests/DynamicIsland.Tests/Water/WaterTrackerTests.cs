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
    public void Sound_is_additive_for_legacy_data_and_preserves_explicit_opt_out()
    {
        var legacy = System.Text.Json.JsonSerializer.Deserialize<WaterState>("{\"GoalMl\":2500}")!;
        legacy.Normalize();
        Assert.True(legacy.Sound);
        var optedOut = System.Text.Json.JsonSerializer.Deserialize<WaterState>("{\"Sound\":false,\"GoalMl\":2500}")!;
        optedOut.Normalize();
        Assert.False(optedOut.Sound);
        Assert.Equal(2500, optedOut.GoalMl);
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
        Assert.Equal(700, state.StepMl);
        Assert.Equal(15, state.ReminderIntervalMinutes);
        Assert.Equal(23, state.ActiveToHour);
    }
}

public sealed class WaterPortionTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 27, 5, 0, 0, TimeSpan.Zero));

    private WaterTracker NewTracker(out WaterState state)
    {
        state = new WaterState();
        state.Normalize();
        return new WaterTracker(state, _time);
    }

    [Fact]
    public void Plus_and_minus_change_the_picked_portion_not_the_total()
    {
        var tracker = NewTracker(out var state);
        Assert.Equal(250, state.StepMl);
        Assert.Equal(50, state.FineStepMl);

        tracker.ChangePortion(1);
        tracker.ChangePortion(1);
        Assert.Equal(350, state.StepMl);
        Assert.Equal(0, tracker.TotalMl);

        tracker.Drink();
        Assert.Equal(350, tracker.TotalMl);

        tracker.ChangePortion(-1);
        tracker.Drink();
        Assert.Equal(650, tracker.TotalMl);
    }

    [Theory]
    [InlineData(-100, 50)]
    [InlineData(100, 700)]
    public void Portion_stays_between_50_and_700_ml(int steps, int expected)
    {
        var tracker = NewTracker(out var state);
        for (var i = 0; i < Math.Abs(steps); i++)
        {
            tracker.ChangePortion(Math.Sign(steps));
        }

        Assert.Equal(expected, state.StepMl);
    }

    [Fact]
    public void Saved_portion_is_clamped_to_the_allowed_range()
    {
        var state = new WaterState { StepMl = 10 };
        state.Normalize();
        Assert.Equal(WaterState.MinPortionMl, state.StepMl);
    }

    [Fact]
    public void Undo_removes_the_last_drink_and_the_total_never_goes_below_zero()
    {
        var tracker = NewTracker(out var state);
        tracker.Drink();
        tracker.ChangePortion(1);
        tracker.Drink();
        Assert.Equal(550, tracker.TotalMl);

        Assert.True(tracker.Undo());
        Assert.Equal(250, tracker.TotalMl);
        Assert.True(tracker.Undo());
        Assert.False(tracker.Undo());
        Assert.Equal(0, tracker.TotalMl);
        Assert.Empty(state.Today);
    }

    [Fact]
    public void Percent_is_capped_at_100_when_the_goal_is_exceeded()
    {
        var tracker = NewTracker(out var state);
        state.GoalMl = 1000;
        for (var i = 0; i < 6; i++)
        {
            tracker.Drink(); // 6 × 250 = 1500 ml
        }

        Assert.Equal(1500, tracker.TotalMl);
        Assert.Equal(100, tracker.Percent);
        Assert.Equal(1.0, tracker.Progress);
        Assert.True(tracker.GoalReached);
    }

    [Fact]
    public void Percent_rounds_and_is_zero_for_an_empty_day()
    {
        var tracker = NewTracker(out var state);
        Assert.Equal(0, tracker.Percent);
        tracker.Drink(); // 250 of 2000
        Assert.Equal(13, tracker.Percent);
        Assert.False(tracker.GoalReached);
    }

    [Fact]
    public void Midnight_moves_the_day_into_history_and_resets_the_total()
    {
        var tracker = NewTracker(out var state);
        tracker.Drink();
        _time.Advance(TimeSpan.FromDays(1));

        Assert.True(tracker.RollDay());
        Assert.Equal(0, tracker.TotalMl);
        Assert.Equal(250, state.History.Values.Last());
        Assert.False(tracker.Undo());
    }

    [Fact]
    public void Legacy_fine_step_always_normalizes_to_50()
    {
        var state = new WaterState { FineStepMl = 0 };
        state.Normalize();
        Assert.Equal(50, state.FineStepMl);
        state.FineStepMl = 99999;
        state.Normalize();
        Assert.Equal(50, state.FineStepMl);
    }
    [Theory]
    [InlineData(1000, 700)]
    [InlineData(701, 700)]
    [InlineData(274, 250)]
    [InlineData(275, 300)]
    [InlineData(1, 50)]
    public void Legacy_portion_migration_preserves_recorded_drinks_and_history(int portion, int expected)
    {
        var day = new DateOnly(2026, 10, 26);
        var state = new WaterState { StepMl = portion, FineStepMl = 100 };
        state.Today.Add(new WaterEntry(_time.GetUtcNow(), 1000));
        state.History[day] = 3000;

        state.Normalize();
        state.Normalize(); // Migration is idempotent.

        Assert.Equal(expected, state.StepMl);
        Assert.Equal(50, state.FineStepMl);
        Assert.Equal(1000, Assert.Single(state.Today).Ml);
        Assert.Equal(3000, state.History[day]);
    }

    [Theory]
    [InlineData(int.MaxValue, 700)]
    [InlineData(int.MinValue, 50)]
    public void Large_portion_step_counts_do_not_overflow(int steps, int expected)
    {
        var tracker = NewTracker(out var state);
        tracker.ChangePortion(steps);
        Assert.Equal(expected, state.StepMl);
        Assert.Equal(0, tracker.TotalMl);
    }

}
