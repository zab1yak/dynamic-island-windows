using DynamicIsland.App.Settings;
using DynamicIsland.Core.Settings;
using DynamicIsland.UI.Localization;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace DynamicIsland.App.Services;

/// <summary>
/// Updates from GitHub Releases through Velopack. Only an installed copy updates; a development
/// build reports that updates are unavailable. A found update is downloaded in the background and
/// applied when the app exits (no forced restart).
/// </summary>
internal sealed class UpdateService(ISettingsService settings, TrayIconService tray, ILogger<UpdateService> logger) : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly UpdateManager _manager = new(new GithubSource(AboutViewModel.RepositoryUrl, accessToken: null, prerelease: false));
    private Timer? _timer;
    private bool _pending;

    /// <summary>Whether this copy was installed by Velopack (and can update itself).</summary>
    public bool IsInstalled => _manager.IsInstalled;

    /// <summary>Starts the daily background check when enabled in settings.</summary>
    public void Start()
    {
        if (!IsInstalled || !settings.Current.CheckForUpdates)
        {
            return;
        }

        _timer = new Timer(_ => _ = CheckAsync(notify: true), null, TimeSpan.FromMinutes(1), CheckInterval);
    }

    /// <summary>Checks now; returns a short status for the About page.</summary>
    public async Task<string> CheckAsync(bool notify)
    {
        if (!IsInstalled)
        {
            return Loc.Instance["Settings.UpdatesUnavailable"];
        }

        if (_pending)
        {
            return Loc.Instance["Update.Ready"];
        }

        try
        {
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null)
            {
                return Loc.Instance.Format("Update.UpToDate", _manager.CurrentVersion?.ToString() ?? string.Empty);
            }

            await _manager.DownloadUpdatesAsync(info);
            _manager.WaitExitThenApplyUpdates(info.TargetFullRelease, silent: true, restart: false);
            _pending = true;
            var version = info.TargetFullRelease.Version.ToString();
            logger.LogInformation("Update {Version} downloaded; it will be applied on exit", version);
            if (notify)
            {
                tray.Notify(Loc.Instance["Update.Title"], Loc.Instance.Format("Update.Body", version));
            }

            return Loc.Instance["Update.Ready"];
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Update check failed");
            return Loc.Instance["Update.Failed"];
        }
    }

    /// <inheritdoc />
    public void Dispose() => _timer?.Dispose();
}
