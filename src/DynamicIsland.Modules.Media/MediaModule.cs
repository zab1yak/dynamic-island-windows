using System.Resources;
using DynamicIsland.Core.Modules;
using DynamicIsland.Modules.Media.Services;
using DynamicIsland.Modules.Media.ViewModels;
using DynamicIsland.UI.Localization;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.Modules.Media;

/// <summary>Localized strings of the Media module.</summary>
internal static class MediaStrings
{
    public static ResourceManager Manager { get; } = new("DynamicIsland.Modules.Media.Resources.Strings", typeof(MediaStrings).Assembly);

    public static string Get(string key) => Loc.Instance[key];
}

/// <summary>
/// Music page: controls whatever plays in Windows (GSMTC) and remembers the last track across
/// reboots (shown paused; resumed only when the user presses Play).
/// </summary>
public sealed class MediaModule(MediaViewModel viewModel, IMediaSessionSource source, LastTrackStore store, RestoreFlow restore, ILogger<MediaModule> logger)
    : IIslandModule
{
    /// <summary>Module id.</summary>
    public const string ModuleId = "media";

    private bool _restoring;

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
        viewModel.CommandFallback = ResumeSavedTrackAsync;
        viewModel.SnapshotChanged += (_, _) => _ = SaveAsync(force: false);
        await source.StartAsync(cancellationToken);
        viewModel.Refresh();

        if (source.Sessions.Count == 0)
        {
            try
            {
                if (await store.LoadAsync(cancellationToken) is { } saved)
                {
                    viewModel.ShowRestored(LastTrackStore.ToSnapshot(saved.Track, saved.Artwork));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Last track could not be restored");
            }
        }
    }

    /// <inheritdoc />
    public Task FlushAsync() => SaveAsync(force: true);

    /// <inheritdoc />
    public async Task ShutdownAsync()
    {
        await FlushAsync();
        viewModel.Dispose();
    }

    /// <summary>Periodic saves come from the progress of a playing track.</summary>
    internal async Task SaveAsync(bool force)
    {
        if (viewModel.RestoredAppId is not null)
        {
            // Still showing the saved track; nothing new to write.
            return;
        }

        try
        {
            await store.OnSnapshotAsync(viewModel.CurrentSnapshot, force);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Saving the last track failed");
        }
    }

    private async Task<bool> ResumeSavedTrackAsync()
    {
        if (viewModel.RestoredAppId is null || viewModel.CurrentSnapshot is not { } saved || _restoring)
        {
            return false;
        }

        _restoring = true;
        try
        {
            viewModel.ShowNotice(MediaStrings.Get("Media.Restore.Starting"));
            var outcome = await restore.ResumeAsync(saved);
            logger.LogInformation("Resume of the saved track: {Outcome}", outcome);
            var notice = outcome switch
            {
                RestoreOutcome.Resumed => null,
                RestoreOutcome.ResumedWithoutPosition => "Media.Restore.NoPosition",
                RestoreOutcome.Browser => "Media.Restore.Browser",
                RestoreOutcome.TimedOut => "Media.Restore.TimedOut",
                _ => "Media.Restore.CannotLaunch",
            };
            if (notice is not null)
            {
                viewModel.ShowNotice(MediaStrings.Get(notice));
            }
            else
            {
                viewModel.Notice = null;
            }

            return true;
        }
        finally
        {
            _restoring = false;
        }
    }
}
