using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Core;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Threading;
using DynamicIsland.Modules.Tools.Services;
using DynamicIsland.UI.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace DynamicIsland.Modules.Tools;

/// <summary>
/// Technical page: settings and data folder shortcuts, the island's own FPS and total CPU load with a
/// green/yellow/red bar. (Notification history has its own page, «Уведомления».)
/// The CPU sampler (1 Hz) runs only while the island is expanded.
/// </summary>
public sealed partial class ToolsViewModel : ObservableObject, IDisposable
{
    private readonly IIslandHost _host;
    private readonly IslandStateMachine _island;
    private readonly IslandRenderStats _render;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly CpuLoad _cpu = new();
    private ITimer? _sampler;

    [ObservableProperty]
    private double _cpuPercent;

    [ObservableProperty]
    private string _cpuText = "—";

    [ObservableProperty]
    private string _cpuBrushKey = "Brush.Green";

    [ObservableProperty]
    private string _fpsText = "—";

    public ToolsViewModel(IIslandHost host, IslandStateMachine island, IslandRenderStats render, IUiDispatcher dispatcher, TimeProvider time)
    {
        _host = host;
        _island = island;
        _render = render;
        _dispatcher = dispatcher;
        _time = time;
        island.ModeChanged += (_, _) => UpdateSampler();
        render.Changed += (_, _) => UpdateFps();
        UpdateFps();
    }

    public void Dispose() => _sampler?.Dispose();

    [RelayCommand]
    private void OpenSettings() => _host.OpenSettings();

    [RelayCommand]
    private static void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataRoot);
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var _ = Process.Start(new ProcessStartInfo(explorer) { UseShellExecute = false, ArgumentList = { AppPaths.DataRoot } });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            // Nothing to show: the folder path is in Settings → About.
        }
    }

    private void UpdateSampler()
    {
        var run = IslandMetrics.IsExpanded(_island.Mode);
        if (run && _sampler is null)
        {
            _cpu.Sample();
            _sampler = _time.CreateTimer(_ =>
            {
                var percent = _cpu.Sample();
                if (percent is { } p)
                {
                    _dispatcher.Post(() => ShowCpu(p));
                }
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
        else if (!run && _sampler is not null)
        {
            _sampler.Dispose();
            _sampler = null;
        }
    }

    private void ShowCpu(double percent)
    {
        CpuPercent = percent;
        CpuText = string.Create(CultureInfo.InvariantCulture, $"{percent:0}%");
        CpuBrushKey = CpuLoad.Level(percent) switch
        {
            LoadLevel.High => "Brush.Red",
            LoadLevel.Medium => "Brush.Yellow",
            _ => "Brush.Green",
        };
    }

    private void UpdateFps() =>
        FpsText = _render.LastAnimationFps is { } fps
            ? string.Create(CultureInfo.InvariantCulture, $"{fps:0} fps · {_render.LongestFrameMs:0} ms")
            : "—";
}

/// <summary>Tools module.</summary>
public sealed class ToolsModule(ToolsViewModel viewModel) : IIslandModule
{
    private static readonly ResourceManager Strings = new("DynamicIsland.Modules.Tools.Resources.Strings", typeof(ToolsModule).Assembly);

    public string Id => "tools";

    public string TitleKey => "Tools.Title";

    public string IconKey => "Wrench";

    public int Order => 7;

    public object ExpandedViewModel => viewModel;

    public object? SettingsViewModel => null;

    public ILiveActivitySource? LiveActivities => null;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        Loc.Instance.Register(Strings);
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        viewModel.Dispose();
        return Task.CompletedTask;
    }
}

/// <summary>DI registration.</summary>
public static class ToolsRegistration
{
    public static IServiceCollection AddToolsModule(this IServiceCollection services)
    {
        services.AddSingleton<ToolsViewModel>();
        services.AddSingleton<ToolsModule>();
        services.AddSingleton<IIslandModule>(sp => sp.GetRequiredService<ToolsModule>());
        services.AddSingleton(new ModuleResources(new Uri("pack://application:,,,/DynamicIsland.Modules.Tools;component/Views/ToolsTemplates.xaml")));
        return services;
    }
}
