using System.Globalization;
using System.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Storage;
using DynamicIsland.Core.Threading;
using DynamicIsland.Modules.Water.Services;
using DynamicIsland.UI.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace DynamicIsland.Modules.Water;

/// <summary>
/// Water page. Two one-shot timers only: the next reminder and local midnight. No polling.
/// </summary>
public sealed partial class WaterViewModel : ObservableObject, ILiveActivitySource, IDisposable
{
    /// <summary>File name relative to the data folder.</summary>
    public const string FileName = "water.json";

    private static readonly TimeSpan AlertDuration = TimeSpan.FromSeconds(6);

    private readonly IStorageService _storage;
    private readonly TimeProvider _time;
    private readonly IUiDispatcher _dispatcher;
    private readonly INotificationService _notifications;
    private WaterTracker _tracker;
    private ITimer? _reminder;
    private ITimer? _midnight;
    private ITimer? _alertTimer;
    private bool _alert;

    [ObservableProperty]
    private string _totalText = string.Empty;

    [ObservableProperty]
    private string _percentText = "0%";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _remindersEnabled;

    [ObservableProperty]
    private string _reminderText = string.Empty;

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private string _drinkText = string.Empty;

    public WaterViewModel(IStorageService storage, TimeProvider time, IUiDispatcher dispatcher, INotificationService notifications)
    {
        _storage = storage;
        _time = time;
        _dispatcher = dispatcher;
        _notifications = notifications;
        _tracker = new WaterTracker(new WaterState(), time);
        Live = new WaterLiveViewModel(this);
        Loc.Instance.CultureChanged += (_, _) => Refresh();
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>Live pill shown with a reminder.</summary>
    public WaterLiveViewModel Live { get; }

    /// <summary>Persisted state (settings live here too).</summary>
    public WaterState State => _tracker.State;

    /// <inheritdoc />
    public LiveActivity? Current => _alert
        ? new LiveActivity("water", ActivityKind.Reminder, LiveActivityPriority.TimerRunning, Live, _time.GetUtcNow(), IsAlert: true, Ttl: AlertDuration)
        : null;

    /// <summary>Loads the saved state and schedules timers.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var state = await _storage.LoadAsync<WaterState>(FileName, cancellationToken) ?? new WaterState();
        state.Normalize();
        _tracker = new WaterTracker(state, _time)
        {
            NotBefore = _time.GetUtcNow() + TimeSpan.FromMinutes(state.ReminderIntervalMinutes),
        };
        _tracker.RollDay();
        await AfterChangeAsync();
    }

    /// <summary>Re-checks from any thread (SystemEvents fire on their own thread).</summary>
    public void RecheckSoon() => _dispatcher.Post(() => _ = RecheckAsync());

    /// <summary>Re-checks the day and timers (after resume or a clock change). UI thread only.</summary>
    public Task RecheckAsync()
    {
        _tracker.RollDay();
        return AfterChangeAsync();
    }

    /// <summary>Saves the state.</summary>
    public Task SaveAsync() => _storage.SaveAsync(FileName, _tracker.State);

    /// <inheritdoc />
    public void Dispose()
    {
        _reminder?.Dispose();
        _midnight?.Dispose();
        _alertTimer?.Dispose();
    }

    [RelayCommand]
    private Task DrinkAsync()
    {
        _tracker.Drink();
        return AfterChangeAsync();
    }

    [RelayCommand]
    private Task IncreaseAsync()
    {
        _tracker.Adjust(_tracker.State.FineStepMl);
        return AfterChangeAsync();
    }

    [RelayCommand]
    private Task DecreaseAsync()
    {
        _tracker.Adjust(-_tracker.State.FineStepMl);
        return AfterChangeAsync();
    }

    /// <summary>Label of the fine-step buttons, e.g. "50 ml".</summary>
    public string FineStepText => Loc.Instance.Format("Water.FineStep", _tracker.State.FineStepMl);

    [RelayCommand]
    private Task UndoAsync()
    {
        _tracker.Undo();
        return AfterChangeAsync();
    }

    [RelayCommand]
    private Task ToggleRemindersAsync()
    {
        _tracker.State.RemindersEnabled = !_tracker.State.RemindersEnabled;
        return AfterChangeAsync();
    }

    private async Task AfterChangeAsync()
    {
        Refresh();
        Schedule();
        await SaveAsync();
    }

    private void Refresh()
    {
        var culture = Loc.Instance.Culture;
        var liters = Loc.Instance["Water.Liters"];
        TotalText = string.Create(culture, $"{_tracker.TotalMl / 1000.0:0.0} / {_tracker.State.GoalMl / 1000.0:0.0} {liters}");
        Progress = _tracker.Progress;
        PercentText = string.Create(culture, $"{Math.Round(_tracker.TotalMl * 100.0 / _tracker.State.GoalMl):0}%");
        RemindersEnabled = _tracker.State.RemindersEnabled;
        ReminderText = Loc.Instance.Format("Water.Every", _tracker.State.ReminderIntervalMinutes);
        CanUndo = _tracker.State.Today.Count > 0;
        DrinkText = Loc.Instance.Format("Water.Drink", _tracker.State.StepMl);
        OnPropertyChanged(nameof(FineStepText));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Schedule()
    {
        _reminder?.Dispose();
        _reminder = null;
        if (_tracker.NextReminderAt() is { } due)
        {
            var delay = due - _time.GetUtcNow();
            _reminder = _time.CreateTimer(_ => _dispatcher.Post(OnReminder), null, delay > TimeSpan.Zero ? delay : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }

        _midnight?.Dispose();
        _midnight = _time.CreateTimer(_ => _dispatcher.Post(() => _ = RecheckAsync()), null, _tracker.UntilMidnight() + TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
    }

    private void OnReminder()
    {
        // Re-validate: the user may have drunk, or the day may have changed since scheduling.
        if (_tracker.NextReminderAt() is not { } due || due > _time.GetUtcNow() + TimeSpan.FromSeconds(1))
        {
            Schedule();
            return;
        }

        _tracker.MarkReminded();
        _notifications.Show(Loc.Instance["Water.ReminderTitle"], Loc.Instance.Format("Water.ReminderBody", TotalText), sound: true);
        _alert = true;
        _alertTimer?.Dispose();
        _alertTimer = _time.CreateTimer(_ => _dispatcher.Post(() =>
        {
            _alert = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }), null, AlertDuration, Timeout.InfiniteTimeSpan);
        _ = AfterChangeAsync();
    }
}

/// <summary>Compact live pill for a water reminder.</summary>
public sealed class WaterLiveViewModel(WaterViewModel water)
{
    /// <summary>The water page the pill binds to.</summary>
    public WaterViewModel Water { get; } = water;
}

/// <summary>Water reminder page.</summary>
public sealed class WaterModule(WaterViewModel viewModel) : IIslandModule
{
    private static readonly ResourceManager Strings = new("DynamicIsland.Modules.Water.Resources.Strings", typeof(WaterModule).Assembly);

    /// <inheritdoc />
    public string Id => "water";

    /// <inheritdoc />
    public string TitleKey => "Water.Title";

    /// <inheritdoc />
    public string IconKey => "Droplet";

    /// <inheritdoc />
    public int Order => 2;

    /// <inheritdoc />
    public object ExpandedViewModel => viewModel;

    private WaterSettingsViewModel? _settings;

    /// <inheritdoc />
    public object? SettingsViewModel
    {
        get
        {
            _settings ??= new WaterSettingsViewModel(viewModel);
            _settings.Reload();
            return _settings;
        }
    }

    /// <inheritdoc />
    public ILiveActivitySource? LiveActivities => viewModel;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Loc.Instance.Register(Strings);
        await viewModel.LoadAsync(cancellationToken);
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Microsoft.Win32.SystemEvents.TimeChanged += OnTimeChanged;
    }

    /// <inheritdoc />
    public Task FlushAsync() => viewModel.SaveAsync();

    /// <inheritdoc />
    public async Task ShutdownAsync()
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        Microsoft.Win32.SystemEvents.TimeChanged -= OnTimeChanged;
        await viewModel.SaveAsync();
        viewModel.Dispose();
    }

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            viewModel.RecheckSoon();
        }
    }

    private void OnTimeChanged(object? sender, EventArgs e) => viewModel.RecheckSoon();
}

/// <summary>DI registration of the Water module.</summary>
public static class WaterRegistration
{
    /// <summary>Registers the module and its templates.</summary>
    public static IServiceCollection AddWaterModule(this IServiceCollection services)
    {
        services.AddSingleton<WaterViewModel>();
        services.AddSingleton<WaterModule>();
        services.AddSingleton<IIslandModule>(sp => sp.GetRequiredService<WaterModule>());
        services.AddSingleton(new ModuleResources(new Uri("pack://application:,,,/DynamicIsland.Modules.Water;component/Views/WaterTemplates.xaml")));
        return services;
    }
}
