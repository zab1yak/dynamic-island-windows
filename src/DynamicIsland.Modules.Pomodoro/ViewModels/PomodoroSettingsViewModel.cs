using CommunityToolkit.Mvvm.ComponentModel;
using DynamicIsland.Modules.Pomodoro.Services;

namespace DynamicIsland.Modules.Pomodoro.ViewModels;

/// <summary>Pomodoro section of the settings window; every change is applied and saved at once.</summary>
public sealed partial class PomodoroSettingsViewModel : ObservableObject
{
    private readonly PomodoroViewModel _timer;
    private bool _loading;

    [ObservableProperty]
    private int _focusMinutes;

    [ObservableProperty]
    private int _shortBreakMinutes;

    [ObservableProperty]
    private int _longBreakMinutes;

    [ObservableProperty]
    private int _longBreakEvery;

    [ObservableProperty]
    private bool _sound;

    public PomodoroSettingsViewModel(PomodoroViewModel timer)
    {
        _timer = timer;
        Reload();
    }

    /// <summary>Re-reads the stored settings.</summary>
    public void Reload()
    {
        _loading = true;
        var s = _timer.Settings;
        FocusMinutes = s.FocusMinutes;
        ShortBreakMinutes = s.ShortBreakMinutes;
        LongBreakMinutes = s.LongBreakMinutes;
        LongBreakEvery = s.LongBreakEvery;
        Sound = s.Sound;
        _loading = false;
    }

    partial void OnFocusMinutesChanged(int value) => Apply();

    partial void OnShortBreakMinutesChanged(int value) => Apply();

    partial void OnLongBreakMinutesChanged(int value) => Apply();

    partial void OnLongBreakEveryChanged(int value) => Apply();

    partial void OnSoundChanged(bool value) => Apply();

    private void Apply()
    {
        if (_loading)
        {
            return;
        }

        _ = _timer.ApplySettingsAsync(new PomodoroSettings
        {
            FocusMinutes = FocusMinutes,
            ShortBreakMinutes = ShortBreakMinutes,
            LongBreakMinutes = LongBreakMinutes,
            LongBreakEvery = LongBreakEvery,
            Sound = Sound,
        });
    }
}
