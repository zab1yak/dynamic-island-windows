using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Threading;
using DynamicIsland.UI.Localization;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App.Services;

/// <summary>One updater shared by the About page and the existing daily automatic check.</summary>
internal sealed class UpdateService : IApplicationUpdateController, IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private readonly ISettingsService _settings;
    private readonly UpdateCoordinator _coordinator;
    private Timer? _timer;
    private int _stopping;

    public UpdateService(ISettingsService settings, TrayIconService tray, ILogger<UpdateService> logger, IUiDispatcher dispatcher)
    {
        _settings = settings;
        _coordinator = new(new VelopackUpdateBackend(), TimeProvider.System,
            snapshot =>
            {
                if (Volatile.Read(ref _stopping) != 0) return;
                dispatcher.Post(() =>
                {
                    if (Volatile.Read(ref _stopping) == 0)
                        tray.Notify(Loc.Instance["Update.Title"], Loc.Instance.Format("Update.Body", snapshot.AvailableVersion));
                });
            },
            (exception, stage) => logger.LogWarning(exception, "Update operation {Stage} failed", stage));
        _coordinator.Changed += OnChanged;
    }

    public UpdateSnapshot Current => _coordinator.Current;
    public bool IsInstalled => _coordinator.IsInstalled;
    public bool IsBusy => _coordinator.IsBusy;
    public event EventHandler? Changed;
    private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    public void Start()
    {
        if (Volatile.Read(ref _stopping) != 0 || !IsInstalled || !_settings.Current.CheckForUpdates || _timer is not null) return;
        _timer = new Timer(_ =>
        {
            if (_settings.Current.CheckForUpdates) _ = CheckAsync(notify: true);
        }, null, TimeSpan.FromMinutes(1), CheckInterval);
    }

    public Task CheckAsync(bool notify = false, CancellationToken cancellationToken = default) => Volatile.Read(ref _stopping) != 0 ? Task.CompletedTask : _coordinator.CheckAsync(notify, cancellationToken);
    public Task DownloadAsync(CancellationToken cancellationToken = default) => Volatile.Read(ref _stopping) != 0 ? Task.CompletedTask : _coordinator.DownloadAsync(cancellationToken);
    public void Cancel() => _coordinator.Cancel();
    public bool PrepareForExit() => _coordinator.PrepareForExit();

    public void Stop()
    {
        Interlocked.Exchange(ref _stopping, 1);
        Interlocked.Exchange(ref _timer, null)?.Dispose();
        _coordinator.Cancel();
    }

    public void Dispose()
    {
        Stop();
        _coordinator.Changed -= OnChanged;
        _coordinator.Dispose();
        GC.SuppressFinalize(this);
    }
}
