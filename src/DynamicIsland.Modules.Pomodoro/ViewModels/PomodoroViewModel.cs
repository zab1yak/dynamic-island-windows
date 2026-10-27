using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Storage;
using DynamicIsland.Core.Threading;
using DynamicIsland.Modules.Pomodoro.Services;
using DynamicIsland.UI.Localization;

namespace DynamicIsland.Modules.Pomodoro.ViewModels;

/// <summary>
/// Pomodoro page and live pill. A one-shot timer fires exactly at the phase end (even when the
/// island is hidden); the 1 Hz display tick runs only while the island is visible and the clock runs.
/// </summary>
public sealed partial class PomodoroViewModel : ObservableObject, ILiveActivitySource, IDisposable
{
    /// <summary>File name relative to the data folder.</summary>
    public const string FileName = "pomodoro.json";

    private static readonly TimeSpan AlertDuration = TimeSpan.FromSeconds(5);

    private readonly IStorageService _storage;
    private readonly TimeProvider _time;
    private readonly IUiDispatcher _dispatcher;
    private readonly IslandStateMachine _island;
    private readonly INotificationService _notifications;
    private PomodoroTimer _timer;
    private ITimer? _tick;
    private ITimer? _completion;
    private ITimer? _alertTimer;
    private DateTimeOffset _startedAt;
    private PhaseCompleted? _alert;

    [ObservableProperty]
    private string _remainingText = "25:00";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private PomodoroPhase _phase;

    [ObservableProperty]
    private string _phaseText = string.Empty;

    [ObservableProperty]
    private int _completedToday;

    [ObservableProperty]
    private int _cyclePosition;

    [ObservableProperty]
    private int _longBreakEvery = 4;

    public PomodoroViewModel(IStorageService storage, TimeProvider time, IUiDispatcher dispatcher, IslandStateMachine island, INotificationService notifications)
    {
        _storage = storage;
        _time = time;
        _dispatcher = dispatcher;
        _island = island;
        _notifications = notifications;
        _timer = new PomodoroTimer(new PomodoroState(), time);
        Live = new PomodoroLiveViewModel(this);
        island.ModeChanged += (_, _) => UpdateTick();
        Loc.Instance.CultureChanged += (_, _) => Refresh();
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>View model of the compact live pill.</summary>
    public PomodoroLiveViewModel Live { get; }

    /// <summary>Persisted settings (durations, sound).</summary>
    public PomodoroSettings Settings => _timer.State.Settings;

    /// <inheritdoc />
    public LiveActivity? Current
    {
        get
        {
            if (_alert is not null)
            {
                return new LiveActivity("pomodoro", ActivityKind.Timer, LiveActivityPriority.TimerEnding, Live, _time.GetUtcNow(), IsAlert: true, Ttl: AlertDuration);
            }

            if (!IsRunning)
            {
                return null;
            }

            var priority = _timer.Remaining <= TimeSpan.FromMinutes(1) ? LiveActivityPriority.TimerEnding : LiveActivityPriority.TimerRunning;
            return new LiveActivity("pomodoro", ActivityKind.Timer, priority, Live, _startedAt);
        }
    }

    /// <summary>Loads the saved state; a phase that ended while the app was closed completes quietly.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var state = await _storage.LoadAsync<PomodoroState>(FileName, cancellationToken) ?? new PomodoroState();
        state.Settings ??= new PomodoroSettings();
        state.Settings.Normalize();
        Sanitize(state, _time.GetUtcNow());
        _timer = new PomodoroTimer(state, _time);
        _timer.CheckCompletion();
        _startedAt = _time.GetUtcNow();
        Refresh();
        ScheduleCompletion();
        UpdateTick();
    }

    /// <summary>Drops impossible values a damaged or hand-edited file could contain.</summary>
    internal static void Sanitize(PomodoroState state, DateTimeOffset now)
    {
        state.RemainingSeconds = double.IsFinite(state.RemainingSeconds) ? Math.Clamp(state.RemainingSeconds, 0, 3 * 3600) : 0;
        state.CompletedFocus = Math.Clamp(state.CompletedFocus, 0, 1000);
        state.CompletedToday = Math.Clamp(state.CompletedToday, 0, 1000);
        if (state.Status == PomodoroStatus.Running && (state.EndsAtUtc is not { } end || end > now + TimeSpan.FromHours(3)))
        {
            state.Status = PomodoroStatus.Idle;
            state.EndsAtUtc = null;
        }
    }

    /// <summary>After sleep or a clock change: complete what ended and re-arm the end timer. Any thread.</summary>
    public void RecheckSoon() => _dispatcher.Post(() =>
    {
        if (_timer.CheckCompletion() is not null)
        {
            _ = AfterChangeAsync();
            return;
        }

        Refresh();
        ScheduleCompletion();
    });

    /// <summary>Saves the state.</summary>
    public Task SaveAsync() => _storage.SaveAsync(FileName, _timer.State);

    /// <summary>Applies new settings (from the settings window) and saves them.</summary>
    public async Task ApplySettingsAsync(PomodoroSettings settings)
    {
        settings.Normalize();
        _timer.State.Settings = settings;
        Refresh();
        ScheduleCompletion();
        await SaveAsync();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _tick?.Dispose();
        _completion?.Dispose();
        _alertTimer?.Dispose();
    }

    [RelayCommand]
    private Task StartPauseAsync()
    {
        if (_timer.State.Status == PomodoroStatus.Running)
        {
            _timer.Pause();
        }
        else
        {
            _timer.Start();
            _startedAt = _time.GetUtcNow();
        }

        return AfterChangeAsync();
    }

    [RelayCommand]
    private Task ResetAsync()
    {
        _timer.Reset();
        return AfterChangeAsync();
    }

    [RelayCommand]
    private Task SelectPhaseAsync(PomodoroPhase phase)
    {
        _timer.Select(phase);
        return AfterChangeAsync();
    }

    private async Task AfterChangeAsync()
    {
        Refresh();
        ScheduleCompletion();
        UpdateTick();
        await SaveAsync();
    }

    private void Refresh()
    {
        var state = _timer.State;
        var remaining = _timer.Remaining;
        RemainingText = Format(remaining);
        Progress = _timer.Progress;
        IsRunning = state.Status == PomodoroStatus.Running;
        Phase = state.Phase;
        PhaseText = Loc.Instance[state.Phase switch
        {
            PomodoroPhase.ShortBreak => "Pomodoro.ShortBreak",
            PomodoroPhase.LongBreak => "Pomodoro.LongBreak",
            _ => "Pomodoro.FocusTime",
        }];
        CompletedToday = state.CompletedToday;
        LongBreakEvery = state.Settings.LongBreakEvery;
        CyclePosition = state.CompletedFocus % state.Settings.LongBreakEvery;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary><c>mm:ss</c>, or <c>h:mm:ss</c> for long phases; seconds are rounded up like a countdown.</summary>
    internal static string Format(TimeSpan remaining)
    {
        var seconds = (int)Math.Ceiling(Math.Max(0, remaining.TotalSeconds));
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalMinutes:00}:{t.Seconds:00}");
    }

    private void ScheduleCompletion()
    {
        _completion?.Dispose();
        _completion = null;
        if (_timer.State.Status == PomodoroStatus.Running)
        {
            var due = _timer.Remaining;
            _completion = _time.CreateTimer(_ => _dispatcher.Post(OnCompletionDue), null, due, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnCompletionDue()
    {
        if (_timer.CheckCompletion() is not { } done)
        {
            ScheduleCompletion();
            return;
        }

        var title = Loc.Instance[done.Finished == PomodoroPhase.Focus ? "Pomodoro.FocusDone" : "Pomodoro.BreakDone"];
        var body = Loc.Instance[done.Next == PomodoroPhase.Focus ? "Pomodoro.NextFocus" : done.Next == PomodoroPhase.LongBreak ? "Pomodoro.NextLongBreak" : "Pomodoro.NextShortBreak"];
        _notifications.Show(title, body, _timer.State.Settings.Sound);

        _alert = done;
        _alertTimer?.Dispose();
        _alertTimer = _time.CreateTimer(_ => _dispatcher.Post(() =>
        {
            _alert = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }), null, AlertDuration, Timeout.InfiniteTimeSpan);
        _ = AfterChangeAsync();
    }

    private void UpdateTick()
    {
        var run = IsRunning && _island.Mode != IslandMode.Hidden;
        if (run && _tick is null)
        {
            Refresh();
            _tick = _time.CreateTimer(_ => _dispatcher.Post(Refresh), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
        else if (!run && _tick is not null)
        {
            _tick.Dispose();
            _tick = null;
        }
    }
}

/// <summary>Compact live pill: mini ring and countdown.</summary>
public sealed class PomodoroLiveViewModel(PomodoroViewModel timer)
{
    /// <summary>The timer the pill binds to.</summary>
    public PomodoroViewModel Timer { get; } = timer;
}
