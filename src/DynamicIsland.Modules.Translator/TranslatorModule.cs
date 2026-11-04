using System.Net.Http;
using System.Resources;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Storage;
using DynamicIsland.Modules.Translator.Services;
using DynamicIsland.UI.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace DynamicIsland.Modules.Translator;

public sealed class TranslatorModule(TranslatorViewModel viewModel, TranslatorSettingsViewModel settings) : IIslandModule
{
    private static readonly ResourceManager Strings = new("DynamicIsland.Modules.Translator.Resources.Strings", typeof(TranslatorModule).Assembly);

    public string Id => "translator";

    public string TitleKey => "Translator.Title";

    public string IconKey => "Languages";

    // Tools remains the final page and settings entry.
    public int Order => 6;

    public object ExpandedViewModel => viewModel;

    public object SettingsViewModel => settings;

    public ILiveActivitySource? LiveActivities => null;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        Loc.Instance.Register(Strings);
        return viewModel.InitializeAsync(cancellationToken);
    }

    public Task ShutdownAsync() => viewModel.ShutdownAsync();
}

public static class TranslatorRegistration
{
    public static IServiceCollection AddTranslatorModule(this IServiceCollection services)
    {
        // The package client downloads only user-requested model files; text never goes through HTTP.
        services.AddSingleton<ITranslationPackageManager>(sp => new TranslationPackageManager(
            new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(30) },
            Path.Combine(sp.GetRequiredService<IStorageService>().RootPath, "translator-models")));
        services.AddSingleton<ITranslationService, OfflineTranslationService>();
        services.AddSingleton<TranslationHistoryService>();
        services.AddSingleton<TranslationPackagesViewModel>();
        services.AddSingleton<TranslatorViewModel>();
        services.AddSingleton<TranslatorSettingsViewModel>();
        services.AddSingleton<TranslatorModule>();
        services.AddSingleton<IIslandModule>(sp => sp.GetRequiredService<TranslatorModule>());
        services.AddSingleton(new ModuleResources(new Uri("pack://application:,,,/DynamicIsland.Modules.Translator;component/Views/TranslatorTemplates.xaml")));
        return services;
    }
}
