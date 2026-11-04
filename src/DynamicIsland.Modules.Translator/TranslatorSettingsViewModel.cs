using CommunityToolkit.Mvvm.ComponentModel;

namespace DynamicIsland.Modules.Translator;

/// <summary>Local packages and local history, without paid accounts or API-key entry.</summary>
public sealed class TranslatorSettingsViewModel(TranslatorViewModel translator, TranslationPackagesViewModel packages) : ObservableObject
{
    public TranslatorViewModel Translator { get; } = translator;
    public TranslationPackagesViewModel Packages { get; } = packages;
}
