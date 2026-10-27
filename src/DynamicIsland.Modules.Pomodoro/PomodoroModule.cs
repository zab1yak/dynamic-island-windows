using System.Resources;
using DynamicIsland.Core.Modules;
using DynamicIsland.Modules.Pomodoro.ViewModels;
using DynamicIsland.UI.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace DynamicIsland.Modules.Pomodoro;

/// <summary>Pomodoro focus timer page.</summary>
public sealed class PomodoroModule(PomodoroViewModel viewModel) : IIslandModule
{
    private static readonly ResourceManager Strings = new("DynamicIsland.Modules.Pomodoro.Resources.Strings", typeof(PomodoroModule).Assembly);

    /// <inheritdoc />
    public string Id => "pomodoro";

    /// <inheritdoc />
    public string TitleKey => "Pomodoro.Title";

    /// <inheritdoc />
    public string IconKey => "Timer";

    /// <inheritdoc />
    public int Order => 1;

    /// <inheritdoc />
    public object ExpandedViewModel => viewModel;

    /// <inheritdoc />
    public object? SettingsViewModel
    {
        get
        {
            _settings ??= new PomodoroSettingsViewModel(viewModel);
            _settings.Reload();
            return _settings;
        }
    }

    private PomodoroSettingsViewModel? _settings;

    /// <inheritdoc />
    public ILiveActivitySource? LiveActivities => viewModel;

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        Loc.Instance.Register(Strings);
        return viewModel.LoadAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task FlushAsync() => viewModel.SaveAsync();

    /// <inheritdoc />
    public async Task ShutdownAsync()
    {
        await viewModel.SaveAsync();
        viewModel.Dispose();
    }
}

/// <summary>DI registration of the Pomodoro module.</summary>
public static class PomodoroRegistration
{
    /// <summary>Registers the module and its templates.</summary>
    public static IServiceCollection AddPomodoroModule(this IServiceCollection services)
    {
        services.AddSingleton<PomodoroViewModel>();
        services.AddSingleton<PomodoroModule>();
        services.AddSingleton<IIslandModule>(sp => sp.GetRequiredService<PomodoroModule>());
        services.AddSingleton(new ModuleResources(new Uri("pack://application:,,,/DynamicIsland.Modules.Pomodoro;component/Views/PomodoroTemplates.xaml")));
        return services;
    }
}
