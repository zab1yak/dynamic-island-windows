using CommunityToolkit.Mvvm.ComponentModel;

namespace DynamicIsland.Modules.Water;

/// <summary>Water section of the settings window; every change is applied and saved at once.</summary>
public sealed partial class WaterSettingsViewModel : ObservableObject
{
    private readonly WaterViewModel _water;
    private bool _loading;

    [ObservableProperty]
    private int _goalMl;

    [ObservableProperty]
    private int _stepMl;

    [ObservableProperty]
    private bool _remindersEnabled;

    [ObservableProperty]
    private int _intervalMinutes;

    [ObservableProperty]
    private int _activeFromHour;

    [ObservableProperty]
    private int _activeToHour;

    public WaterSettingsViewModel(WaterViewModel water)
    {
        _water = water;
        Reload();
    }

    /// <summary>Re-reads the stored settings.</summary>
    public void Reload()
    {
        _loading = true;
        var s = _water.State;
        GoalMl = s.GoalMl;
        StepMl = s.StepMl;
        RemindersEnabled = s.RemindersEnabled;
        IntervalMinutes = s.ReminderIntervalMinutes;
        ActiveFromHour = s.ActiveFromHour;
        ActiveToHour = s.ActiveToHour;
        _loading = false;
    }

    partial void OnGoalMlChanged(int value) => Apply();

    partial void OnStepMlChanged(int value) => Apply();

    partial void OnRemindersEnabledChanged(bool value) => Apply();

    partial void OnIntervalMinutesChanged(int value) => Apply();

    partial void OnActiveFromHourChanged(int value) => Apply();

    partial void OnActiveToHourChanged(int value) => Apply();

    private void Apply()
    {
        if (_loading)
        {
            return;
        }

        var s = _water.State;
        s.GoalMl = GoalMl;
        s.StepMl = StepMl;
        s.RemindersEnabled = RemindersEnabled;
        s.ReminderIntervalMinutes = IntervalMinutes;
        s.ActiveFromHour = ActiveFromHour;
        s.ActiveToHour = ActiveToHour;
        s.Normalize();
        _ = _water.RecheckAsync();
        // Show what was actually stored (for example "until" is kept after "from").
        Reload();
    }
}
