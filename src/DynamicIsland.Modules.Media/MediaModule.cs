using System.Resources;
using DynamicIsland.Core.Modules;
using DynamicIsland.Modules.Media.Services;
using DynamicIsland.Modules.Media.ViewModels;
using DynamicIsland.UI.Localization;

namespace DynamicIsland.Modules.Media;

/// <summary>Localized strings of the Media module.</summary>
internal static class MediaStrings
{
    public static ResourceManager Manager { get; } = new("DynamicIsland.Modules.Media.Resources.Strings", typeof(MediaStrings).Assembly);

    public static string Get(string key) => Loc.Instance[key];
}

/// <summary>Music page: controls whatever plays in Windows (GSMTC).</summary>
public sealed class MediaModule(MediaViewModel viewModel, IMediaSessionSource source) : IIslandModule
{
    /// <summary>Module id.</summary>
    public const string ModuleId = "media";

    /// <inheritdoc />
    public string Id => ModuleId;

    /// <inheritdoc />
    public string TitleKey => "Media.Title";

    /// <inheritdoc />
    public string IconKey => "Music";

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public object ExpandedViewModel => viewModel;

    /// <inheritdoc />
    public object? SettingsViewModel => null;

    /// <inheritdoc />
    public ILiveActivitySource? LiveActivities => viewModel;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Loc.Instance.Register(MediaStrings.Manager);
        await source.StartAsync(cancellationToken);
        viewModel.Refresh();
    }

    /// <inheritdoc />
    public Task ShutdownAsync()
    {
        viewModel.Dispose();
        return Task.CompletedTask;
    }
}
