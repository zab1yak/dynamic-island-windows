using System.Collections.ObjectModel;
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
using Microsoft.Extensions.Logging;

namespace DynamicIsland.Modules.Tools;

/// <summary>
/// Technical page: settings and data folder shortcuts, the island's own FPS, total CPU load with a
/// green/yellow/red bar, and the Windows notification history when Windows allows reading it.
/// The CPU sampler (1 Hz) runs only while the island is expanded.
/// </summary>
public sealed partial class ToolsViewModel : ObservableObject, IDisposable
{
    private readonly IIslandHost _host;
    private readonly IslandStateMachine _island;
    private readonly IslandRenderStats _render;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly ILogger<ToolsViewModel> _logger;
    private readonly CpuLoad _cpu = new();
    private readonly NotificationHistory _notifications = new();
    private ITimer? _sampler;

    [ObservableProperty]
    private double _cpuPercent;

    [ObservableProperty]
    private string _cpuText = "—";

    [ObservableProperty]
    private string _cpuBrushKey = "Brush.Green";

    [ObservableProperty]
    private string _fpsText = "—";

    [ObservableProperty]
    private string _notificationStatus = string.Empty;

    [ObservableProperty]
    private bool _canRequestNotifications = true;

    public ToolsViewModel(IIslandHost host, IslandStateMachine island, IslandRenderStats render, IUiDispatcher dispatcher, TimeProvider time, ILogger<ToolsViewModel> logger)
    {
        _logger = logger;
        _host = host;
        _island = island;
        _render = render;
        _dispatcher = dispatcher;
        _time = time;
        island.ModeChanged += (_, _) => UpdateSampler();
        render.Changed += (_, _) => UpdateFps();
        UpdateFps();
    }

    /// <summary>Latest notifications.</summary>
    public ObservableCollection<NotificationEntry> Notifications { get; } = [];

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

    [RelayCommand]
    private async Task LoadNotificationsAsync()
    {
        var access = _notifications.Access == NotificationAccess.Allowed ? NotificationAccess.Allowed : await _notifications.RequestAccessAsync();
        NotificationStatus = access switch
        {
            NotificationAccess.Allowed => string.Empty,
            NotificationAccess.Denied => Loc.Instance["Tools.NotificationsDenied"],
            _ => Loc.Instance["Tools.NotificationsUnavailable"],
        };
        CanRequestNotifications = access != NotificationAccess.Unavailable;
        _logger.LogInformation("Notification history access: {Access}", access);
        Notifications.Clear();
        foreach (var entry in await _notifications.ReadAsync())
        {
            Notifications.Add(entry);
        }
    }

    /// <summary>Clears the list on this page (the Windows notification center is not touched).</summary>
    [RelayCommand]
    private void ClearNotifications() => Notifications.Clear();

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

    public int Order => 6;

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
