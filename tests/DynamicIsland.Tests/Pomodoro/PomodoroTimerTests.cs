using DynamicIsland.Modules.Pomodoro.Services;
using Microsoft.Extensions.Time.Testing;

namespace DynamicIsland.Tests.Pomodoro;

public sealed class PomodoroTimerTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));

    private PomodoroTimer NewTimer(PomodoroState? state = null) => new(state ?? new PomodoroState(), _time);

    [Fact]
    public void Missed_completion_is_consumed_once_even_after_long_sleep()
    {
        var timer = NewTimer();
        timer.Start();
        _time.Advance(TimeSpan.FromHours(12));
        Assert.NotNull(timer.CheckCompletion());
        Assert.Null(timer.CheckCompletion());
        Assert.Equal(1, timer.State.CompletedToday);
        Assert.Equal(PomodoroStatus.Idle, timer.State.Status);
    }

    [Fact]
    public void Idle_shows_the_full_focus_duration()
    {
        var timer = NewTimer();
        Assert.Equal(TimeSpan.FromMinutes(25), timer.Remaining);
        Assert.Equal(0, timer.Progress);
    }

    [Fact]
    public void Running_counts_down_from_the_stored_end_time()
    {
        var timer = NewTimer();
        timer.Start();
        _time.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(TimeSpan.FromMinutes(15), timer.Remaining);
        Assert.Equal(0.4, timer.Progress, 3);
        Assert.Equal(_time.GetUtcNow().AddMinutes(15), timer.State.EndsAtUtc);
    }

    [Fact]
    public void Pause_and_resume_keep_the_remaining_time()
    {
        var timer = NewTimer();
        timer.Start();
        _time.Advance(TimeSpan.FromMinutes(5));
        timer.Pause();
        _time.Advance(TimeSpan.FromHours(2));

        Assert.Equal(TimeSpan.FromMinutes(20), timer.Remaining);
        timer.Start();
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.FromMinutes(19), timer.Remaining);
    }

    [Fact]
    public void State_survives_a_restart_because_the_end_time_is_stored()
    {
        var first = NewTimer();
        first.Start();
        _time.Advance(TimeSpan.FromMinutes(3));

        // "Restart": a new timer over the same persisted state.
        var second = NewTimer(first.State);
        _time.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(TimeSpan.FromMinutes(20), second.Remaining);
    }

    [Fact]
    public void Focus_end_leads_to_short_breaks_then_a_long_break_every_fourth()
    {
        var timer = NewTimer();
        var nexts = new List<PomodoroPhase>();
        for (var i = 0; i < 4; i++)
        {
            timer.Select(PomodoroPhase.Focus);
            timer.Start();
            _time.Advance(TimeSpan.FromMinutes(25));
            nexts.Add(timer.CheckCompletion()!.Next);
        }

        Assert.Equal([PomodoroPhase.ShortBreak, PomodoroPhase.ShortBreak, PomodoroPhase.ShortBreak, PomodoroPhase.LongBreak], nexts);
        Assert.Equal(4, timer.State.CompletedToday);
        Assert.Equal(PomodoroStatus.Idle, timer.State.Status);

        timer.Start();
        _time.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(new PhaseCompleted(PomodoroPhase.LongBreak, PomodoroPhase.Focus), timer.CheckCompletion());
        Assert.Equal(0, timer.State.CompletedFocus);
    }

    [Fact]
    public void Phase_that_ended_while_the_app_was_closed_completes_on_start_up()
    {
        var timer = NewTimer();
        timer.Start();
        _time.Advance(TimeSpan.FromHours(1));

        var reopened = NewTimer(timer.State);
        Assert.Equal(PomodoroPhase.Focus, reopened.CheckCompletion()?.Finished);
        Assert.Null(reopened.CheckCompletion());
    }

    [Fact]
    public void Not_finished_yet_completes_nothing()
    {
        var timer = NewTimer();
        timer.Start();
        _time.Advance(TimeSpan.FromMinutes(24));
        Assert.Null(timer.CheckCompletion());
    }

    [Fact]
    public void Daily_counter_resets_on_a_new_day()
    {
        var timer = NewTimer();
        timer.Start();
        _time.Advance(TimeSpan.FromMinutes(25));
        timer.CheckCompletion();
        Assert.Equal(1, timer.State.CompletedToday);

        _time.Advance(TimeSpan.FromDays(1));
        timer.CheckCompletion();
        Assert.Equal(0, timer.State.CompletedToday);
    }

    [Fact]
    public void Settings_are_clamped()
    {
        var settings = new PomodoroSettings { FocusMinutes = 0, ShortBreakMinutes = 500, LongBreakEvery = 1 };
        settings.Normalize();
        Assert.Equal(1, settings.FocusMinutes);
        Assert.Equal(60, settings.ShortBreakMinutes);
        Assert.Equal(2, settings.LongBreakEvery);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(180)]
    public void Custom_duration_counts_down_pauses_and_completes(int minutes)
    {
        var timer = NewTimer();
        timer.ApplySettings(new PomodoroSettings { FocusMinutes = minutes });
        timer.Start();
        _time.Advance(TimeSpan.FromSeconds(30));
        timer.Pause();
        Assert.Equal(TimeSpan.FromMinutes(minutes) - TimeSpan.FromSeconds(30), timer.Remaining);
        _time.Advance(TimeSpan.FromMinutes(10));
        timer.Start();
        _time.Advance(timer.Remaining);
        Assert.Equal(new PhaseCompleted(PomodoroPhase.Focus, PomodoroPhase.ShortBreak), timer.CheckCompletion());
        Assert.Equal(1, timer.State.CompletedToday);
    }

    [Fact]
    public void Duration_edit_preserves_active_deadline_and_progress_until_reset()
    {
        var timer = NewTimer();
        timer.Start();
        _time.Advance(TimeSpan.FromMinutes(5));
        var deadline = timer.State.EndsAtUtc;
        timer.ApplySettings(new PomodoroSettings { FocusMinutes = 40 });
        Assert.Equal(deadline, timer.State.EndsAtUtc);
        Assert.Equal(TimeSpan.FromMinutes(20), timer.Remaining);
        Assert.Equal(0.2, timer.Progress, 6);

        timer.Pause();
        timer.ApplySettings(new PomodoroSettings { FocusMinutes = 37 });
        var json = System.Text.Json.JsonSerializer.Serialize(timer.State);
        var restored = NewTimer(System.Text.Json.JsonSerializer.Deserialize<PomodoroState>(json)!);
        Assert.Equal(TimeSpan.FromMinutes(20), restored.Remaining);
        Assert.Equal(0.2, restored.Progress, 6);
        restored.Start();
        Assert.Equal(TimeSpan.FromMinutes(20), restored.Remaining);
        restored.Reset();
        Assert.Equal(TimeSpan.FromMinutes(37), restored.Remaining);
        Assert.Equal(0, restored.Progress);
    }

    [Fact]
    public void Legacy_active_duration_is_captured_before_settings_change()
    {
        var timer = NewTimer(new PomodoroState
        {
            Status = PomodoroStatus.Running,
            EndsAtUtc = _time.GetUtcNow().AddMinutes(20),
        });
        timer.ApplySettings(new PomodoroSettings { FocusMinutes = 40 });
        Assert.Equal(1500, timer.State.ActiveDurationSeconds);
        Assert.Equal(0.2, timer.Progress, 6);
    }

}
