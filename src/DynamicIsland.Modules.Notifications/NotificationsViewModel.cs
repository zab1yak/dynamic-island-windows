using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Platform;
using DynamicIsland.Core.Storage;
using DynamicIsland.Modules.Notifications.Services;
using DynamicIsland.UI.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.Modules.Notifications;

/// <summary>What the page shows.</summary>
public enum NotificationsPageState
{
    /// <summary>No identity yet: explanation and «Включить».</summary>
    NeedsSetup,

    /// <summary>The package is registered; identity comes with a restart.</summary>
    NeedsRestart,

    /// <summary>Identity present, Windows has not been asked yet: «Разрешить доступ».</summary>
    NeedsPermission,

    /// <summary>The user turned access off in Windows settings.</summary>
    Denied,

    /// <summary>The list.</summary>
    Ready,
}

/// <summary>Persisted page state (<c>notifications.json</c>): only when the list was last cleared, no texts.</summary>
public sealed class NotificationsState
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Notifications up to this moment are hidden by «Очистить».</summary>
    public DateTimeOffset? ClearedAt { get; set; }
}

/// <summary>A notification row.</summary>
public sealed record NotificationRow(string App, string Title, string Body, string Time);

/// <summary>
/// «Уведомления» page: Windows notifications (Telegram and others) read through
/// <c>UserNotificationListener</c>. Requires package identity, set up on the user's click (D-038).
/// Texts stay in memory only; nothing is logged or written to disk except the «cleared at» time.
/// </summary>
public sealed partial class NotificationsViewModel : ObservableObject
{
    /// <summary>File name relative to the data folder.</summary>
    public const string FileName = "notifications.json";

    private static readonly TimeSpan RefreshThrottle = TimeSpan.FromSeconds(2);

    private readonly IPackageIdentity _identity;
    private readonly IAppControl _app;
    private readonly IStorageService _storage;
    private readonly TimeProvider _time;
    private readonly ILogger<NotificationsViewModel> _logger;
    private readonly NotificationHistory _history = new();
    private NotificationsState _state = new();
    private DateTimeOffset _lastLoad = DateTimeOffset.MinValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSetup), nameof(ShowRestart), nameof(ShowPermission), nameof(ShowDenied), nameof(ShowList))]
    private NotificationsPageState _pageState;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isEmpty = true;

    public NotificationsViewModel(IPackageIdentity identity, IAppControl app, IStorageService storage, IslandStateMachine island,
        TimeProvider time, ILogger<NotificationsViewModel> logger)
    {
        _identity = identity;
        _app = app;
        _storage = storage;
        _time = time;
        _logger = logger;
        island.ModeChanged += (_, mode) =>
        {
            if (IslandMetrics.IsExpanded(mode) && PageState == NotificationsPageState.Ready)
            {
                _ = LoadAsync();
            }
        };
    }

    /// <summary>Latest notifications, newest first.</summary>
    public ObservableCollection<NotificationRow> Items { get; } = [];

    public bool ShowSetup => PageState == NotificationsPageState.NeedsSetup;

    public bool ShowRestart => PageState == NotificationsPageState.NeedsRestart;

    public bool ShowPermission => PageState == NotificationsPageState.NeedsPermission;

    public bool ShowDenied => PageState == NotificationsPageState.Denied;

    public bool ShowList => PageState == NotificationsPageState.Ready;

    /// <summary>Decides the page state without any prompt. Called once on start.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _state = await _storage.LoadAsync<NotificationsState>(FileName, cancellationToken) ?? new NotificationsState();
        PageState = DecideState(_identity.HasIdentity, _identity.HasIdentity ? _history.CheckAccess() : NotificationAccess.Unavailable, _identity.IsRegistered);
        if (PageState == NotificationsPageState.Ready)
        {
            await LoadAsync();
        }
    }

    /// <summary>The page state for the given facts.</summary>
    internal static NotificationsPageState DecideState(bool hasIdentity, NotificationAccess access, bool registered) =>
        (hasIdentity, access) switch
        {
            (false, _) when registered => NotificationsPageState.NeedsRestart,
            (false, _) => NotificationsPageState.NeedsSetup,
            (true, NotificationAccess.Allowed) => NotificationsPageState.Ready,
            (true, NotificationAccess.Denied) => NotificationsPageState.Denied,
            (true, NotificationAccess.Unavailable) => NotificationsPageState.NeedsSetup,
            _ => NotificationsPageState.NeedsPermission,
        };

    [RelayCommand]
    private async Task EnableAsync()
    {
        IsBusy = true;
        Message = Loc.Instance["Notifications.Working"];
        try
        {
            var result = await _identity.SetUpAsync();
            Message = result switch
            {
                IdentitySetupResult.Registered => string.Empty,
                IdentitySetupResult.Cancelled => Loc.Instance["Notifications.Cancelled"],
                IdentitySetupResult.PackageMissing => Loc.Instance["Notifications.Missing"],
                _ => Loc.Instance["Notifications.Failed"],
            };
            if (result == IdentitySetupResult.Registered)
            {
                PageState = NotificationsPageState.NeedsRestart;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Restart() => _app.Restart();

    [RelayCommand]
    private async Task AllowAsync()
    {
        var access = await _history.RequestAccessAsync();
        PageState = DecideState(true, access, true);
        _logger.LogInformation("Notification history access: {Access}", access);
        if (PageState == NotificationsPageState.Ready)
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private void OpenWindowsSettings()
    {
        try
        {
            // Fixed system URI: Settings → Privacy → Notifications.
            using var _ = Process.Start(new ProcessStartInfo("ms-settings:privacy-notifications") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Opening Windows settings failed");
        }
    }

    [RelayCommand]
    private Task RefreshAsync()
    {
        _lastLoad = DateTimeOffset.MinValue;
        return LoadAsync();
    }

    /// <summary>Hides everything shown now; newer notifications appear again. The Windows notification center is not touched.</summary>
    [RelayCommand]
    private async Task ClearAsync()
    {
        _state.ClearedAt = _time.GetUtcNow();
        Items.Clear();
        IsEmpty = true;
        await _storage.SaveAsync(FileName, _state);
    }

    private async Task LoadAsync()
    {
        var now = _time.GetUtcNow();
        if (now - _lastLoad < RefreshThrottle)
        {
            return;
        }

        _lastLoad = now;
        var entries = NotificationHistory.Visible(await _history.ReadAsync(), _state.ClearedAt);
        if (_history.Access == NotificationAccess.Unavailable)
        {
            PageState = NotificationsPageState.NeedsSetup;
            return;
        }

        Items.Clear();
        var today = _time.GetLocalNow().Date;
        foreach (var e in entries)
        {
            var local = e.Time.ToLocalTime();
            var time = local.Date == today
                ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
                : local.ToString("dd.MM HH:mm", CultureInfo.InvariantCulture);
            Items.Add(new NotificationRow(e.App, e.Title, e.Body, time));
        }

        IsEmpty = Items.Count == 0;
    }
}

/// <summary>«Уведомления» page.</summary>
public sealed class NotificationsModule(NotificationsViewModel viewModel) : IIslandModule
{
    private static readonly ResourceManager Strings = new("DynamicIsland.Modules.Notifications.Resources.Strings", typeof(NotificationsModule).Assembly);

    public string Id => "notifications";

    public string TitleKey => "Notifications.Title";

    public string IconKey => "BellRing";

    public int Order => 6;

    public object ExpandedViewModel => viewModel;

    public object? SettingsViewModel => null;

    public ILiveActivitySource? LiveActivities => null;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        Loc.Instance.Register(Strings);
        return viewModel.InitializeAsync(cancellationToken);
    }

    public Task ShutdownAsync() => Task.CompletedTask;
}

/// <summary>DI registration.</summary>
public static class NotificationsRegistration
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddSingleton<NotificationsViewModel>();
        services.AddSingleton<NotificationsModule>();
        services.AddSingleton<IIslandModule>(sp => sp.GetRequiredService<NotificationsModule>());
        services.AddSingleton(new ModuleResources(new Uri("pack://application:,,,/DynamicIsland.Modules.Notifications;component/Views/NotificationsTemplates.xaml")));
        return services;
    }
}
