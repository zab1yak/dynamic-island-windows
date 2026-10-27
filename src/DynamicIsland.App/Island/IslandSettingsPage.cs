using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.App.Settings;
using DynamicIsland.Core.Input;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Platform;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Threading;
using DynamicIsland.UI.Localization;

namespace DynamicIsland.App.Island;

/// <summary>A module's settings shown as a group on the island settings page.</summary>
internal sealed record ModuleSettingsGroup(string TitleKey, string Icon, object Content)
{
    public string Title => Loc.Instance[TitleKey];
}

/// <summary>
/// «Настройки» — the last island page, which cannot be hidden. Appearance (background), lock, pages
/// (order and visibility), general and island behaviour, every module's settings and «Выйти из
/// программы» with a two-step confirmation. Every change is saved and applied at once.
/// </summary>
internal sealed partial class IslandSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IAppControl _app;
    private readonly IIslandHost _host;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly ConfirmGate _quitGate;
    private readonly IServiceProvider _services;
    private ITimer? _quitReset;
    private bool _loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBlack), nameof(IsTranslucent), nameof(IsLightGray))]
    private IslandBackground _background;

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private bool _quitArmed;

    /// <remarks>
    /// The page is itself a module, and ModuleHost (needed by the pages list) depends on all modules, so
    /// those parts are resolved lazily from <paramref name="services"/> after construction.
    /// </remarks>
    public IslandSettingsViewModel(ISettingsService settings, IServiceProvider services, IAppControl app, IIslandHost host,
        IslandStateMachine island, IUiDispatcher dispatcher, TimeProvider time)
    {
        _services = services;
        _settings = settings;
        _app = app;
        _host = host;
        _dispatcher = dispatcher;
        _time = time;
        _quitGate = new ConfirmGate(time);
        settings.Changed += (_, _) => _dispatcher.Post(Reload);
        var wasExpanded = false;
        island.ModeChanged += (_, mode) =>
        {
            // Pick up changes made in the settings window, only when the island opens (not on pin/drop).
            var expanded = IslandMetrics.IsExpanded(mode);
            if (expanded && !wasExpanded)
            {
                General.Reload();
                Pages.Reload();
                Reload();
            }

            wasExpanded = expanded;
        };
        Reload();
    }

    public GeneralSettingsViewModel General => _services.GetRequiredService<GeneralSettingsViewModel>();

    public ModulesSettingsViewModel Pages => _services.GetRequiredService<ModulesSettingsViewModel>();

    /// <summary>Settings of the modules that have any (water, Pomodoro, shelf, recorder…).</summary>
    public IReadOnlyList<ModuleSettingsGroup> ModuleGroups => _services.GetServices<IIslandModule>()
        .Where(m => m.Id != ModuleHost.SettingsPageId)
        .OrderBy(m => m.Order)
        .Select(m => m.SettingsViewModel is { } content ? new ModuleSettingsGroup(m.TitleKey, m.IconKey, content) : null)
        .OfType<ModuleSettingsGroup>()
        .ToList();

    public bool IsBlack => Background == IslandBackground.Black;

    public bool IsTranslucent => Background == IslandBackground.Translucent;

    public bool IsLightGray => Background == IslandBackground.LightGray;

    /// <summary>Re-reads the stored values.</summary>
    public void Reload()
    {
        _loading = true;
        Background = _settings.Current.Background;
        IsLocked = _settings.Current.IslandLocked;
        _loading = false;
    }

    [RelayCommand]
    private void SetBackground(IslandBackground value) => Background = value;

    /// <summary>Hotkeys are recorded in the settings window, which can take the keyboard normally.</summary>
    [RelayCommand]
    private void ChangeHotkey() => _host.OpenSettings("island");

    /// <summary>First press arms, a second press within 4 s quits.</summary>
    [RelayCommand]
    private void Quit()
    {
        if (_quitGate.Press())
        {
            _app.Quit();
            return;
        }

        QuitArmed = true;
        _quitReset?.Dispose();
        _quitReset = _time.CreateTimer(_ => _dispatcher.Post(() => QuitArmed = _quitGate.IsArmed), null, ConfirmGate.Window, Timeout.InfiniteTimeSpan);
    }

    partial void OnBackgroundChanged(IslandBackground value) => Save(s => s.Background = value);

    partial void OnIsLockedChanged(bool value) => Save(s => s.IslandLocked = value);

    private void Save(Action<AppSettings> change)
    {
        if (_loading)
        {
            return;
        }

        change(_settings.Current);
        _ = _settings.SaveAsync();
    }
}

/// <summary>The «Настройки» page module: always last, never hidden.</summary>
internal sealed class SettingsPageModule(IslandSettingsViewModel viewModel) : IIslandModule
{
    public string Id => ModuleHost.SettingsPageId;

    public string TitleKey => "Settings.PageTitle";

    public string IconKey => "Settings";

    public int Order => int.MaxValue;

    public object ExpandedViewModel => viewModel;

    public object? SettingsViewModel => null;

    public ILiveActivitySource? LiveActivities => null;

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ShutdownAsync() => Task.CompletedTask;
}
