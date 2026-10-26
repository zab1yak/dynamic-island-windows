namespace DynamicIsland.Modules.Pomodoro.Services;

/// <summary>Pomodoro phase.</summary>
public enum PomodoroPhase
{
    /// <summary>Work interval.</summary>
    Focus,

    /// <summary>Short rest.</summary>
    ShortBreak,

    /// <summary>Long rest after several focus intervals.</summary>
    LongBreak,
}

/// <summary>Whether the clock runs.</summary>
public enum PomodoroStatus
{
    /// <summary>Not started (full duration shown).</summary>
    Idle,

    /// <summary>Counting down to <see cref="PomodoroState.EndsAtUtc"/>.</summary>
    Running,

    /// <summary>Stopped with <see cref="PomodoroState.RemainingSeconds"/> left.</summary>
    Paused,
}

/// <summary>Durations and cycle rules.</summary>
public sealed class PomodoroSettings
{
    public int FocusMinutes { get; set; } = 25;

    public int ShortBreakMinutes { get; set; } = 5;

    public int LongBreakMinutes { get; set; } = 15;

    /// <summary>A long break follows every N-th focus interval.</summary>
    public int LongBreakEvery { get; set; } = 4;

    /// <summary>Play a sound with the end-of-phase notification.</summary>
    public bool Sound { get; set; } = true;

    /// <summary>Clamps values to sane ranges.</summary>
    public void Normalize()
    {
        FocusMinutes = Math.Clamp(FocusMinutes, 1, 180);
        ShortBreakMinutes = Math.Clamp(ShortBreakMinutes, 1, 60);
        LongBreakMinutes = Math.Clamp(LongBreakMinutes, 1, 120);
        LongBreakEvery = Math.Clamp(LongBreakEvery, 2, 12);
    }

    /// <summary>Length of <paramref name="phase"/>.</summary>
    public TimeSpan Duration(PomodoroPhase phase) => TimeSpan.FromMinutes(phase switch
    {
        PomodoroPhase.ShortBreak => ShortBreakMinutes,
        PomodoroPhase.LongBreak => LongBreakMinutes,
        _ => FocusMinutes,
    });
}

/// <summary>Persisted timer state (<c>pomodoro.json</c>). The end time is stored, not the remaining seconds.</summary>
public sealed class PomodoroState
{
    public int SchemaVersion { get; set; } = 1;

    public PomodoroPhase Phase { get; set; } = PomodoroPhase.Focus;

    public PomodoroStatus Status { get; set; } = PomodoroStatus.Idle;

    /// <summary>When the running phase ends (UTC).</summary>
    public DateTimeOffset? EndsAtUtc { get; set; }

    /// <summary>Seconds left while paused.</summary>
    public double RemainingSeconds { get; set; }

    /// <summary>Focus intervals finished in the current set (resets after a long break).</summary>
    public int CompletedFocus { get; set; }

    /// <summary>Focus intervals finished today (shown as the cycle counter).</summary>
    public int CompletedToday { get; set; }

    /// <summary>Local date of <see cref="CompletedToday"/>.</summary>
    public DateOnly? CounterDate { get; set; }

    public PomodoroSettings Settings { get; set; } = new();
}

/// <summary>A phase that just ended.</summary>
public sealed record PhaseCompleted(PomodoroPhase Finished, PomodoroPhase Next);

/// <summary>
/// Pomodoro rules on top of <see cref="PomodoroState"/>. Because only the end time is stored, the
/// timer survives restarts, sleep and clock-driven UI pauses: the remaining time is always
/// computed from "now".
/// </summary>
public sealed class PomodoroTimer(PomodoroState state, TimeProvider time)
{
    /// <summary>The state being driven (persist it after every change).</summary>
    public PomodoroState State { get; } = state;

    /// <summary>Time left in the current phase.</summary>
    public TimeSpan Remaining
    {
        get
        {
            var remaining = State.Status switch
            {
                PomodoroStatus.Running when State.EndsAtUtc is { } end => end - time.GetUtcNow(),
                PomodoroStatus.Paused => TimeSpan.FromSeconds(State.RemainingSeconds),
                _ => State.Settings.Duration(State.Phase),
            };
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }

    /// <summary>Elapsed share of the phase, 0–1.</summary>
    public double Progress
    {
        get
        {
            var total = State.Settings.Duration(State.Phase);
            return total > TimeSpan.Zero ? Math.Clamp(1 - (Remaining / total), 0, 1) : 0;
        }
    }

    /// <summary>Starts or resumes the countdown.</summary>
    public void Start()
    {
        if (State.Status == PomodoroStatus.Running)
        {
            return;
        }

        var left = State.Status == PomodoroStatus.Paused ? TimeSpan.FromSeconds(State.RemainingSeconds) : State.Settings.Duration(State.Phase);
        State.EndsAtUtc = time.GetUtcNow() + left;
        State.Status = PomodoroStatus.Running;
    }

    /// <summary>Pauses, remembering the time left.</summary>
    public void Pause()
    {
        if (State.Status != PomodoroStatus.Running)
        {
            return;
        }

        State.RemainingSeconds = Remaining.TotalSeconds;
        State.EndsAtUtc = null;
        State.Status = PomodoroStatus.Paused;
    }

    /// <summary>Back to the full duration of the current phase, stopped.</summary>
    public void Reset()
    {
        State.Status = PomodoroStatus.Idle;
        State.EndsAtUtc = null;
        State.RemainingSeconds = 0;
    }

    /// <summary>Switches to <paramref name="phase"/> (stopped, full duration).</summary>
    public void Select(PomodoroPhase phase)
    {
        State.Phase = phase;
        Reset();
    }

    /// <summary>
    /// Completes the phase if its end time has passed (call on every tick and after start-up).
    /// Returns what finished, or <c>null</c>.
    /// </summary>
    public PhaseCompleted? CheckCompletion()
    {
        RollCounterDate();
        if (State.Status != PomodoroStatus.Running || State.EndsAtUtc is not { } end || time.GetUtcNow() < end)
        {
            return null;
        }

        var finished = State.Phase;
        PomodoroPhase next;
        if (finished == PomodoroPhase.Focus)
        {
            State.CompletedFocus++;
            State.CompletedToday++;
            next = State.CompletedFocus % State.Settings.LongBreakEvery == 0 ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;
        }
        else
        {
            if (finished == PomodoroPhase.LongBreak)
            {
                State.CompletedFocus = 0;
            }

            next = PomodoroPhase.Focus;
        }

        State.Phase = next;
        Reset();
        return new PhaseCompleted(finished, next);
    }

    private void RollCounterDate()
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        if (State.CounterDate != today)
        {
            State.CounterDate = today;
            State.CompletedToday = 0;
        }
    }
}
