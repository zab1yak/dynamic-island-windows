using System.Dynamic;
using System.Reflection;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Status;
using DynamicIsland.Core.Threading;
using DynamicIsland.UI.Localization;

namespace DynamicIsland.Visual.QA;

internal sealed class MemorySettings : ISettingsService
{
    public AppSettings Current { get; } = new() { HideInFullscreen = false, IslandScale = 1, HoverExpandDelayMs = 0 };
    public bool IsFirstRun => false;
    public event EventHandler? Changed;
    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}

internal sealed class QaDispatcher : IUiDispatcher
{
    public bool CheckAccess() => Application.Current.Dispatcher.CheckAccess();
    public void Post(Action action) => Application.Current.Dispatcher.BeginInvoke(action);
    public Task InvokeAsync(Action action) => Application.Current.Dispatcher.InvokeAsync(action).Task;
}

internal sealed class InertBattery : IBatteryService
{
    public BatteryStatus Current => new(true, 76, false, false);
    public event EventHandler? Changed { add { } remove { } }
    public void Start() { }
}

internal sealed class InertPrivacy : IPrivacyMonitor
{
    public PrivacyStatus Current => PrivacyStatus.Idle;
    public event EventHandler? Changed { add { } remove { } }
    public void Start() { }
}

internal sealed class NoOpCommand : ICommand
{
    public static NoOpCommand Instance { get; } = new();
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) { }
}

internal sealed class FixtureModule(string id, int order, ContentPresenter page) : IIslandModule
{
    public string Id => id;
    public string TitleKey => id switch
    {
        "todo" => "Todo.Title", "water" => "Water.Title", "pomodoro" => "Pomodoro.Title",
        "translator" => "Translator.Title", "tools" => "Tools.Title", "notifications" => "Notifications.Title", _ => "Media.Title",
    };
    public string IconKey => "Circle";
    public bool ShowsTitle => id != "media";
    public int Order => order;
    public object ExpandedViewModel => page;
    public object? SettingsViewModel => null;
    public ILiveActivitySource? LiveActivities => null;
    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
}

internal static class Fixtures
{
    internal static readonly string[] Names = ["Todo", "Water", "Pomodoro", "Translator", "Tools", "Notifications", "Media"];
    private static readonly string[] Commands = ["BeginAdd", "ToggleEditMode", "Toggle", "BeginRename", "Delete", "Decrease", "Drink", "Increase", "OpenSettings", "ToggleReminders", "Undo", "Reset", "SelectPhase", "StartPause", "Cancel", "Clear", "Download", "Swap", "Translate", "OpenExplorer", "Allow", "Enable", "OpenWindowsSettings", "Refresh", "Restart", "Next", "NextSession", "PlayPause", "Previous", "Seek", "ToggleRepeat", "ToggleShuffle"];
    private static readonly string[] FalseProperties = ["IsAdding", "IsEditing", "IsEditMode", "GoalReached", "IsRunning", "IsBusy", "NeedsPackage", "ShowDenied", "ShowPermission", "ShowRestart", "ShowSetup", "ShowUnavailable", "IsEmpty", "IsScrubbing", "IsRepeatActive", "IsShuffleActive", "HasMultipleSessions"];
    private static readonly string[] TrueProperties = ["CanDecreasePortion", "CanIncreasePortion", "CanUndo", "RemindersEnabled", "ShowList", "HasMedia", "IsPlaying", "CanNext", "CanPrevious", "CanRepeat", "CanSeek", "CanShuffle"];

    public static IReadOnlyList<FixtureModule> CreateModules()
    {
        var modules = new List<FixtureModule>();
        foreach (var name in Names)
        {
            var assemblyName = "DynamicIsland.Modules." + name;
            var assembly = Assembly.Load(assemblyName);
            Loc.Instance.Register(new ResourceManager(assemblyName + ".Resources.Strings", assembly));
            var dictionary = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/{assemblyName};component/Views/{name}Templates.xaml"),
            };
            Application.Current.Resources.MergedDictionaries.Add(dictionary);
            var viewModelName = name switch
            {
                "Media" => assemblyName + ".ViewModels.MediaViewModel",
                "Pomodoro" => assemblyName + ".ViewModels.PomodoroViewModel",
                _ => assemblyName + "." + name + "ViewModel",
            };
            var modelType = assembly.GetType(viewModelName, throwOnError: true)!;
            var template = (DataTemplate)dictionary[new DataTemplateKey(modelType)];
            modules.Add(new FixtureModule(name.ToLowerInvariant(), modules.Count,
                new ContentPresenter { Content = CreateModel(name), ContentTemplate = template }));
        }
        return modules;
    }

    public static void RefreshLanguage(IEnumerable<FixtureModule> modules)
    {
        foreach (var module in modules)
        {
            ((ContentPresenter)module.ExpandedViewModel).Content = CreateModel(Names[module.Order]);
        }
    }

    private static object CreateModel(string name)
    {
        IDictionary<string, object?> model = new ExpandoObject();
        var ru = Loc.Instance.Culture.TwoLetterISOLanguageName == "ru";
        foreach (var command in Commands) model[command + "Command"] = NoOpCommand.Instance;
        foreach (var key in FalseProperties) model[key] = false;
        foreach (var key in TrueProperties) model[key] = true;
        model["Counter"] = "2 / 5";
        model["Summary"] = ru ? "Выполнено 2 задачи из 5" : "2 of 5 tasks completed";
        model["NewTitle"] = string.Empty;
        model["TotalText"] = "0.3 / 2.0 L";
        model["StatusText"] = ru ? "Ещё 1.7 л до цели" : "1.7 L left to your goal";
        model["DrinkText"] = ru ? "Выпить" : "Drink";
        model["ReminderText"] = ru ? "Напоминания включены" : "Reminders enabled";
        model["FineStepText"] = "50 ml";
        model["PercentNumber"] = "15";
        model["PortionText"] = "250 ml";
        model["Progress"] = name == "Water" ? 0.15 : 0.35;
        model["DurationMinutes"] = 25;
        model["MaximumDurationMinutes"] = 180;
        model["RemainingText"] = name == "Media" ? "−2:42" : "25:00";
        model["PhaseText"] = ru ? "Время фокуса" : "Focus time";
        model["CompletedToday"] = 2;
        model["Input"] = ru ? "Съешь ещё этих мягких булок.\nЦена: 25 ₽ · 23°" : "The quick brown fox.\nPrice: €25 · 23°";
        model["Output"] = ru ? "Eat some more soft buns.\nPrice: €25 · 23°" : "Быстрая бурая лиса.\nЦена: 25 ₽ · 23°";
        model["Status"] = ru ? "Перевод готов · локально" : "Translation ready · local";
        model["SelectedPackageDetails"] = "Synthetic QA fixture";
        model["SourceLanguages"] = new[] { new Language("ru", ru ? "Русский" : "Russian"), new Language("en", "English") };
        model["TargetLanguages"] = model["SourceLanguages"];
        model["SourceLanguage"] = ru ? "ru" : "en";
        model["TargetLanguage"] = ru ? "en" : "ru";
        model["Packages"] = new { IsBusy = false, Status = string.Empty, Progress = 0d, CancelCommand = NoOpCommand.Instance };
        model["CpuBrushKey"] = "Brush.Green";
        model["CpuPercent"] = 7d;
        model["CpuText"] = "7%";
        model["FpsText"] = "QA sample";
        model["Message"] = null;
        model["Title"] = ru ? "Съешь ещё булок · 2026" : "Soft light · 2026";
        model["Artist"] = ru ? "Тестовый исполнитель" : "Synthetic artist";
        model["Artwork"] = null;
        model["Notice"] = null;
        model["Accent"] = 0xFF608CE1u;
        model["AppName"] = "Synthetic player";
        model["PositionText"] = "1:18";
        model["PlayPauseLabel"] = ru ? "Пауза" : "Pause";
        model["RepeatGlyph"] = "Repeat";
        model["RepeatLabel"] = ru ? "Повтор" : "Repeat";
        model["ShuffleLabel"] = ru ? "Перемешать" : "Shuffle";
        model["Items"] = name == "Notifications"
            ? new[] { new { App = "QA Calendar", Title = ru ? "Встреча через 15 минут" : "Meeting in 15 minutes", Body = ru ? "Обсудить дизайн острова" : "Review island design", Time = "09:45" } }
            : (object)new[]
            {
                new { Title = ru ? "Проверить кириллицу ё Ё" : "Review English typography", Draft = string.Empty, Done = false, IsEditing = false },
                new { Title = ru ? "Длинная задача с ₽ € % °" : "A longer task with ₽ € % °", Draft = string.Empty, Done = true, IsEditing = false },
            };
        return model;
    }

    private sealed record Language(string Code, string Name);
}
