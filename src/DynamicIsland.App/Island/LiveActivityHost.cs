using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Modules;

namespace DynamicIsland.App.Island;

/// <summary>
/// Collects the live activities of all modules, runs them through <see cref="LiveActivityArbiter"/>
/// and exposes the winner (shown in the live pill) and a secondary hint.
/// </summary>
internal sealed partial class LiveActivityHost : ObservableObject
{
    private readonly IReadOnlyList<IIslandModule> _modules;
    private readonly LiveActivityArbiter _arbiter;
    private readonly IslandStateMachine _stateMachine;
    private readonly DispatcherTimer _retry;

    [ObservableProperty]
    private object? _primary;

    [ObservableProperty]
    private object? _secondary;

    [ObservableProperty]
    private string? _secondaryIcon;

    private readonly ModuleHost _host;

    public LiveActivityHost(IEnumerable<IIslandModule> modules, TimeProvider time, IslandStateMachine stateMachine, ModuleHost host)
    {
        _modules = modules.ToList();
        _host = host;
        host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ModuleHost.Pages))
            {
                Refresh();
            }
        };
        _arbiter = new LiveActivityArbiter(time);
        _stateMachine = stateMachine;
        _retry = new DispatcherTimer { Interval = LiveActivityArbiter.Hysteresis };
        _retry.Tick += (_, _) => Refresh();
    }

    /// <summary>Subscribes to every module's activity source.</summary>
    public void Start()
    {
        foreach (var source in _modules.Select(m => m.LiveActivities).OfType<ILiveActivitySource>())
        {
            source.Changed += (_, _) => Refresh();
        }

        Refresh();
    }

    private void Refresh()
    {
        _retry.Stop();
        // Disabled or failed modules never reach the pill.
        _arbiter.Update(_host.Pages.Select(m => m.LiveActivities?.Current));
        var result = _arbiter.Current;
        Primary = result.Primary?.ViewModel;
        Secondary = result.Secondary?.ViewModel;
        SecondaryIcon = result.Secondary is { } s ? _modules.FirstOrDefault(m => m.Id == s.ModuleId)?.IconKey : null;
        _stateMachine.SetLiveActivity(result.Primary is not null);
        if (_arbiter.IsSwitchPending)
        {
            _retry.Start();
        }
    }
}
