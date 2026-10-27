using DynamicIsland.App.Island;
using DynamicIsland.App.Services;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Status;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Modules.Media;
using DynamicIsland.Modules.Pomodoro;
using DynamicIsland.Modules.Water;
using DynamicIsland.Modules.Shelf;
using DynamicIsland.Modules.Todo;
using DynamicIsland.Core.Modules;
using DynamicIsland.Platform.Windows.Status;
using DynamicIsland.Core.Threading;
using DynamicIsland.UI.Threading;
using DynamicIsland.Core;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Storage;
using DynamicIsland.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DynamicIsland.App.Hosting;

/// <summary>Composition root: every service and module is registered here.</summary>
internal static class AppHostBuilder
{
    public static IHost Build(CommandLine commandLine, SingleInstance instance)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
            DisableDefaults = true,
        });
        builder.Services.AddSerilog(dispose: false);

        var services = builder.Services;
        services.AddSingleton(commandLine);
        services.AddSingleton(instance);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IStorageService>(sp =>
            new JsonStorageService(AppPaths.DataRoot, sp.GetRequiredService<ILogger<JsonStorageService>>()));
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<AppLifetime>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<INotificationService, TrayNotificationService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<GlobalExceptionHandler>();
        services.AddSingleton<IUiDispatcher>(_ => new WpfUiDispatcher(System.Windows.Application.Current.Dispatcher));
        services.AddSingleton<IslandStateMachine>();
        services.AddSingleton<IBatteryService, BatteryService>();
        services.AddSingleton<PrivacyMonitor>();
        services.AddSingleton<IPrivacyMonitor>(sp => sp.GetRequiredService<PrivacyMonitor>());
        services.AddSingleton<StatusViewModel>();
        services.AddSingleton<LiveActivityHost>();
        services.AddSingleton<ModuleHost>();
        services.AddSingleton<DynamicIsland.Platform.Windows.Startup.AutostartService>();
        services.AddSingleton<DynamicIsland.App.Settings.GeneralSettingsViewModel>();
        services.AddSingleton<DynamicIsland.App.Settings.ModulesSettingsViewModel>();
        services.AddSingleton<DynamicIsland.App.Settings.AboutViewModel>();
        services.AddSingleton<DynamicIsland.App.Settings.SettingsViewModel>();
        services.AddSingleton<DynamicIsland.App.Settings.SettingsWindow>();
        services.AddSingleton<IslandHostService>();
        services.AddSingleton<IIslandHost>(sp => sp.GetRequiredService<IslandHostService>());
        services.AddSingleton<IslandViewModel>();

        // Island modules: one line each; the window never changes when a module is added.
        services.AddMediaModule();
        services.AddPomodoroModule();
        services.AddWaterModule();
        services.AddShelfModule();
        services.AddTodoModule();
        services.AddSingleton<IslandWindow>();

        return builder.Build();
    }
}
