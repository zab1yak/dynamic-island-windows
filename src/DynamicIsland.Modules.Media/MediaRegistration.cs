using DynamicIsland.Core.Island;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Threading;
using DynamicIsland.Modules.Media.Interop;
using DynamicIsland.Modules.Media.Services;
using DynamicIsland.Modules.Media.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DynamicIsland.Modules.Media;

/// <summary>DI registration of the Media module.</summary>
public static class MediaRegistration
{
    /// <summary>Registers the module, its services and its view templates.</summary>
    public static IServiceCollection AddMediaModule(this IServiceCollection services)
    {
        services.AddSingleton<IMediaSessionSource, GsmtcSessionSource>();
        services.AddSingleton<IAudioLevelSource, AudioPeakMeter>();
        services.AddSingleton(sp => new MediaViewModel(
            sp.GetRequiredService<IMediaSessionSource>(),
            sp.GetRequiredService<IAudioLevelSource>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<IslandStateMachine>(),
            ArtworkPalette.AccentAsync));
        services.AddSingleton<IAppLauncher, AppLauncher>();
        services.AddSingleton<LastTrackStore>();
        services.AddSingleton<RestoreFlow>();
        services.AddSingleton<MediaModule>();
        services.AddSingleton<IIslandModule>(sp => sp.GetRequiredService<MediaModule>());
        services.AddSingleton(new ModuleResources(new Uri("pack://application:,,,/DynamicIsland.Modules.Media;component/Views/MediaTemplates.xaml")));
        return services;
    }
}
