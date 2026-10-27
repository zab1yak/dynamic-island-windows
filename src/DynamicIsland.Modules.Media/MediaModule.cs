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
public sealed class MediaModule(MediaViewModel viewModel, IMediaSessionSource source, LastTrackStore store, RestoreFlow restore, TimeProvider time, ILogger<MediaModule> logger)
    : IIslandModule
{
    /// <summary>Module id.</summary>
    public const string ModuleId = "media";

    private bool _restoring;
    private ITimer? _saveTimer;

    /// <inheritdoc />
    public string Id => ModuleId;

    /// <inheritdoc />
    public string TitleKey => "Media.Title";

    /// <inheritdoc />
    public bool ShowsTitle => false;

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
        viewModel.SnapshotChanged += (_, _) =>
        {
            _ = SaveAsync(force: false);
            UpdateSaveTimer();
        };
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        await source.StartAsync(cancellationToken);
        viewModel.Refresh();

        // Loading also protects the saved position from the 0:00 the player reports when it comes back.
        try
        {
            if (await store.LoadAsync(cancellationToken) is { } saved && source.Sessions.Count == 0)
            {
                viewModel.ShowRestored(LastTrackStore.ToSnapshot(saved.Track, saved.Artwork));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Last track could not be restored");
        }
    }

    /// <inheritdoc />
    public Task FlushAsync() => SaveAsync(force: true);

    /// <inheritdoc />
    public async Task ShutdownAsync()
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _saveTimer?.Dispose();
        await FlushAsync();
        viewModel.Dispose();
    }

    /// <summary>Saves the position every 5 s while playing, whatever the island shows.</summary>
    private void UpdateSaveTimer()
    {
        var playing = viewModel.CurrentSnapshot?.IsPlaying == true && viewModel.RestoredAppId is null;
        if (playing && _saveTimer is null)
        {
            _saveTimer = time.CreateTimer(_ => _ = SaveAsync(force: false), null, LastTrackStore.PeriodicInterval, LastTrackStore.PeriodicInterval);
        }
        else if (!playing && _saveTimer is not null)
        {
            _saveTimer.Dispose();
            _saveTimer = null;
        }
    }

    /// <summary>After sleep the media session manager may stop sending events: reconnect.</summary>
    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            _ = ReconnectAsync();
        }
    }

    private async Task ReconnectAsync()
    {
        try
        {
            await source.StartAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Reconnecting to media sessions after resume failed");
        }
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
            if (await store.OnSnapshotAsync(viewModel.CurrentSnapshot, force) && viewModel.CurrentSnapshot is { } saved)
            {
                logger.LogDebug("Last track saved ({Reason}, {State}, {Position:0} s)", force ? "flush" : "change or timer", saved.State, saved.Position.TotalSeconds);
            }
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
