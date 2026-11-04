using System.Net.Http;
using System.Resources;
using DynamicIsland.Core.Modules;
using DynamicIsland.Modules.Translator.Interop;
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
        settings.Load();
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        viewModel.Dispose();
        settings.Dispose();
        return Task.CompletedTask;
    }
}

public static class TranslatorRegistration
{
    public static IServiceCollection AddTranslatorModule(this IServiceCollection services)
    {
        services.AddSingleton<ITranslationCredentialStore, WindowsTranslationCredentialStore>();
        services.AddSingleton<ITranslationService>(sp => new YandexTranslationService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<ITranslationCredentialStore>()));
        services.AddSingleton(_ => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            MaxResponseContentBufferSize = 1024 * 1024,
            Timeout = TimeSpan.FromSeconds(20),
        });
        services.AddSingleton<TranslatorViewModel>();
        services.AddSingleton<TranslatorSettingsViewModel>();
        services.AddSingleton<TranslatorModule>();
        services.AddSingleton<IIslandModule>(sp => sp.GetRequiredService<TranslatorModule>());
        services.AddSingleton(new ModuleResources(new Uri("pack://application:,,,/DynamicIsland.Modules.Translator;component/Views/TranslatorTemplates.xaml")));
        return services;
    }
}
