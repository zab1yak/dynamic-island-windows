using DynamicIsland.App.Island;
using DynamicIsland.App.Services;
using DynamicIsland.Core.Island;
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
        services.AddSingleton<GlobalExceptionHandler>();
        services.AddSingleton<IUiDispatcher>(_ => new WpfUiDispatcher(System.Windows.Application.Current.Dispatcher));
        services.AddSingleton<IslandStateMachine>();
        services.AddSingleton<IslandViewModel>();
        services.AddSingleton<IslandWindow>();

        return builder.Build();
    }
}
