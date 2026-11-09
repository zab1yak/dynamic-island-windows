using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.App.Island;
using DynamicIsland.App.Services;
using DynamicIsland.Core.Input;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Threading;
using DynamicIsland.Platform.Windows.Display;
using DynamicIsland.Platform.Windows.Startup;
using DynamicIsland.UI.Localization;

namespace DynamicIsland.App.Settings;

/// <summary>A page in the settings sidebar.</summary>
/// <param name="Id">Stable id (module id for module pages).</param>
/// <param name="TitleKey">Localization key of the title.</param>
/// <param name="Icon">Icon glyph.</param>
/// <param name="Content">View model rendered on the right.</param>
internal sealed record SettingsSection(string Id, string TitleKey, string Icon, object Content)
{
    public string Title => Loc.Instance[TitleKey];
}

/// <summary>A display in the monitor picker.</summary>
internal sealed record MonitorChoice(string? DeviceName, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A language in the picker (<c>null</c> follows Windows).</summary>
internal sealed record LanguageChoice(string? Code, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A horizontal anchor in the island placement picker.</summary>
internal sealed record PositionChoice(IslandPosition Value, string Label);

/// <summary>General and island behaviour settings; every change is saved at once.</summary>
internal sealed partial class GeneralSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly AutostartService _autostart;
    private bool _loading;

    [ObservableProperty]
    private bool _launchAtStartup;

    [ObservableProperty]
    private LanguageChoice? _language;

    [ObservableProperty]
    private bool _hideInFullscreen;

    [ObservableProperty]
    private bool _detectScreenRecording;

    [ObservableProperty]
    private bool _checkForUpdates;

    [ObservableProperty]
    private bool _islandLocked;

    /// <summary>Island background: translucent, black or light grey (applied at once, animated).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBlack), nameof(IsTranslucent), nameof(IsLightGray))]
    private IslandBackground _background;

    public bool IsBlack => Background == IslandBackground.Black;

    public bool IsTranslucent => Background == IslandBackground.Translucent;

    public bool IsLightGray => Background == IslandBackground.LightGray;

    [ObservableProperty]
    private MonitorChoice? _monitor;

    [ObservableProperty]
    private PositionChoice? _position;

    [ObservableProperty]
    private int _islandScalePercent;

    [ObservableProperty]
    private int _expandDelayMs;

    [ObservableProperty]
    private int _collapseDelayMs;

    [ObservableProperty]
    private string _hotkey = string.Empty;

    [ObservableProperty]
    private string? _hotkeyError;

    [ObservableProperty]
    private string _visibilityHotkey = string.Empty;

    [ObservableProperty]
    private string? _visibilityHotkeyError;

    public GeneralSettingsViewModel(ISettingsService settings, AutostartService autostart)
    {
        _settings = settings;
        _autostart = autostart;
        Reload();
    }

    public ObservableCollection<MonitorChoice> Monitors { get; } = [];

    public IReadOnlyList<PositionChoice> Positions { get; private set; } = [];

    public IReadOnlyList<LanguageChoice> Languages { get; } =
    [
        new(null, Loc.Instance["Settings.LanguageSystem"]),
        new("ru", "Русский"),
        new("en", "English"),
    ];

    /// <summary>Re-reads settings and the monitor list.</summary>
    public void Reload()
    {
        _loading = true;
        var s = _settings.Current;
        LaunchAtStartup = Environment.ProcessPath is { } exe && AutostartService.IsEnabled(exe);
        Language = Languages.FirstOrDefault(l => l.Code == s.Language) ?? Languages[0];
        HideInFullscreen = s.HideInFullscreen;
        DetectScreenRecording = s.DetectScreenRecording;
        CheckForUpdates = s.CheckForUpdates;
        IslandLocked = s.IslandLocked;
        Background = s.Background;
        IslandScalePercent = (int)Math.Round(s.IslandScale * 100);
        ExpandDelayMs = s.HoverExpandDelayMs;
        CollapseDelayMs = s.HoverCollapseDelayMs;
        Hotkey = s.ToggleHotkey;
        HotkeyError = null;
        VisibilityHotkey = s.VisibilityHotkey;
        VisibilityHotkeyError = null;
        Positions =
        [
            new(IslandPosition.Left, Loc.Instance["Settings.PositionLeft"]),
            new(IslandPosition.Center, Loc.Instance["Settings.PositionCenter"]),
            new(IslandPosition.Right, Loc.Instance["Settings.PositionRight"]),
        ];
        OnPropertyChanged(nameof(Positions));
        Position = Positions.First(p => p.Value == s.Position);

        Monitors.Clear();
        Monitors.Add(new MonitorChoice(null, Loc.Instance["Settings.MonitorPrimary"]));
        var index = 1;
        foreach (var m in MonitorService.GetMonitors())
        {
            var label = string.Create(CultureInfo.CurrentCulture, $"{Loc.Instance["Settings.Display"]} {index++} · {m.Bounds.Width}×{m.Bounds.Height}{(m.IsPrimary ? " · " + Loc.Instance["Settings.Primary"] : string.Empty)}");
            Monitors.Add(new MonitorChoice(m.DeviceName, label));
        }

        Monitor = Monitors.FirstOrDefault(m => m.DeviceName == s.MonitorDeviceName) ?? Monitors[0];
        _loading = false;
    }

    /// <summary>Validates and applies a hotkey typed in the hotkey box.</summary>
    public void TrySetHotkey(string text)
    {
        if (HotkeyGesture.TryParse(text, out var gesture))
        {
            if (HotkeyGesture.TryParse(VisibilityHotkey, out var visibility) && gesture == visibility)
            {
                HotkeyError = Loc.Instance["Settings.HotkeyDuplicate"];
                return;
            }

            HotkeyError = null;
            Hotkey = gesture.Value.ToString();
        }
        else
        {
            HotkeyError = Loc.Instance["Settings.HotkeyInvalid"];
        }
    }

    /// <summary>Validates the independent visibility hotkey without changing the expansion shortcut.</summary>
    public void TrySetVisibilityHotkey(string text)
    {
        if (!HotkeyGesture.TryParse(text, out var gesture))
        {
            VisibilityHotkeyError = Loc.Instance["Settings.HotkeyInvalid"];
            return;
        }

        if (HotkeyGesture.TryParse(Hotkey, out var expansion) && gesture == expansion)
        {
            VisibilityHotkeyError = Loc.Instance["Settings.HotkeyDuplicate"];
            return;
        }

        VisibilityHotkeyError = null;
        VisibilityHotkey = gesture.Value.ToString();
    }

    partial void OnLaunchAtStartupChanged(bool value)
    {
        if (_loading || Environment.ProcessPath is not { } exe)
        {
            return;
        }

        if (!_autostart.Set(value, exe))
        {
            // The registry refused: show the real state.
            _loading = true;
            LaunchAtStartup = AutostartService.IsEnabled(exe);
            _loading = false;
            return;
        }

        _settings.Current.LaunchAtStartup = value;
        Save();
    }

    partial void OnLanguageChanged(LanguageChoice? value) => Save(s => s.Language = value?.Code);

    partial void OnHideInFullscreenChanged(bool value) => Save(s => s.HideInFullscreen = value);

    partial void OnDetectScreenRecordingChanged(bool value) => Save(s => s.DetectScreenRecording = value);

    partial void OnCheckForUpdatesChanged(bool value) => Save(s => s.CheckForUpdates = value);

    partial void OnIslandLockedChanged(bool value) => Save(s => s.IslandLocked = value);

    partial void OnBackgroundChanged(IslandBackground value) => Save(s => s.Background = value);

    [RelayCommand]
    private void SetBackground(IslandBackground value) => Background = value;

    partial void OnMonitorChanged(MonitorChoice? value) => Save(s => s.MonitorDeviceName = value?.DeviceName);

    partial void OnPositionChanged(PositionChoice? value) => Save(s => s.Position = value?.Value ?? IslandPosition.Center);

    partial void OnIslandScalePercentChanged(int value) => Save(s => s.IslandScale = value / 100.0);

    partial void OnExpandDelayMsChanged(int value) => Save(s => s.HoverExpandDelayMs = value);

    partial void OnCollapseDelayMsChanged(int value) => Save(s => s.HoverCollapseDelayMs = value);

    partial void OnHotkeyChanged(string value) => Save(s => s.ToggleHotkey = value);

    partial void OnVisibilityHotkeyChanged(string value) => Save(s => s.VisibilityHotkey = value);

    private void Save(Action<AppSettings>? change = null)
    {
        if (_loading)
        {
            return;
        }

        change?.Invoke(_settings.Current);
        _ = _settings.SaveAsync();
    }
}

/// <summary>A module row in the "Modules" page.</summary>
internal sealed partial class ModuleRow(string id, string titleKey, string icon, bool enabled) : ObservableObject
{
    [ObservableProperty]
    private bool _enabled = enabled;

    public string Id { get; } = id;

    public string Icon { get; } = icon;

    public string Title => Loc.Instance[titleKey];
}

/// <summary>Enable, disable and reorder island pages.</summary>
internal sealed partial class ModulesSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ModuleHost _modules;
    private bool _loading;

    public ModulesSettingsViewModel(ISettingsService settings, ModuleHost modules)
    {
        _settings = settings;
        _modules = modules;
        Reload();
    }

    public ObservableCollection<ModuleRow> Rows { get; } = [];

    public void Reload()
    {
        _loading = true;
        Rows.Clear();
        var s = _settings.Current;
        var ordered = _modules.AllModules
            .OrderBy(m => s.ModuleOrder.IndexOf(m.Id) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(m => m.Order);
        foreach (var module in ordered)
        {
            var row = new ModuleRow(module.Id, module.TitleKey, module.IconKey, !s.DisabledModules.Contains(module.Id));
            row.PropertyChanged += (_, _) => Save();
            Rows.Add(row);
        }

        _loading = false;
    }

    [RelayCommand]
    private void MoveUp(ModuleRow? row) => Move(row, -1);

    [RelayCommand]
    private void MoveDown(ModuleRow? row) => Move(row, 1);

    private void Move(ModuleRow? row, int delta)
    {
        if (row is null)
        {
            return;
        }

        var index = Rows.IndexOf(row);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Rows.Count)
        {
            return;
        }

        Rows.Move(index, target);
        Save();
    }

    private void Save()
    {
        if (_loading)
        {
            return;
        }

        var s = _settings.Current;
        s.ModuleOrder = Rows.Select(r => r.Id).ToList();
        s.DisabledModules = Rows.Where(r => !r.Enabled).Select(r => r.Id).ToList();
        _ = _settings.SaveAsync();
    }
}

/// <summary>About page and the user-visible state of the existing application updater.</summary>
internal sealed partial class AboutViewModel : ObservableObject, IDisposable
{
    public const string RepositoryUrl = "https://github.com/zab1yak/dynamic-island-windows";
    private IApplicationUpdateController? _updates;
    private IUiDispatcher? _dispatcher;
    private UpdateSnapshot? _snapshot;
    private CancellationTokenSource? _manualOperation;
    private bool _disposed;

    public string Version { get; } = typeof(AboutViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AboutViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public AboutViewModel() => Loc.Instance.CultureChanged += OnCultureChanged;

    public Func<Task>? ExitForUpdate { get; set; }
    public bool IsUpdateBusy => _updates?.IsBusy ?? false;
    public bool ShowDownloadUpdate => _snapshot?.State == UpdateState.Available;
    public bool ShowApplyUpdate => _snapshot?.State == UpdateState.Ready;
    public bool ShowCancelUpdate => IsUpdateBusy && _manualOperation is not null;
    public bool ShowReleaseLink => _snapshot?.State is UpdateState.Available or UpdateState.Ready or UpdateState.Unsupported or UpdateState.Error;
    public bool IsUpdateProgressIndeterminate => _snapshot?.State == UpdateState.Checking || _snapshot?.ProgressPercent is null;
    public double UpdateProgress => _snapshot?.ProgressPercent ?? 0;
    public string CheckUpdatesLabel => Loc.Instance[_snapshot?.State is UpdateState.Error or UpdateState.Cancelled ? "Update.Retry" : "Settings.CheckNow"];
    public string InstalledVersion => _snapshot?.InstalledVersion is { Length: > 0 } version ? version : Version;
    public string ReleasePageUrl => IsProjectReleaseUrl(_snapshot?.ReleaseUrl) ? _snapshot!.ReleaseUrl : RepositoryUrl + "/releases/latest";

    public string UpdateStatus
    {
        get
        {
            if (IsUpdateBusy && _manualOperation?.IsCancellationRequested == true) return Loc.Instance["Update.Cancelling"];
            var available = _snapshot?.AvailableVersion ?? string.Empty;
            return _snapshot?.State switch
            {
                UpdateState.Checking => Loc.Instance["Update.Checking"],
                UpdateState.Latest => Loc.Instance.Format("Update.Latest", InstalledVersion),
                UpdateState.Available => Loc.Instance.Format("Update.Available", available),
                UpdateState.Downloading when _snapshot.ProgressPercent is { } percent => Loc.Instance.Format("Update.DownloadingPercent", percent),
                UpdateState.Downloading => Loc.Instance["Update.Downloading"],
                UpdateState.Ready => Loc.Instance.Format("Update.Downloaded", available),
                UpdateState.Unsupported => Loc.Instance["Update.Unsupported"],
                UpdateState.Cancelled => Loc.Instance["Update.Cancelled"],
                UpdateState.Error => ErrorMessage(),
                _ => Loc.Instance["Update.ManualHint"],
            };
        }
    }

    public string UpdateDetail
    {
        get
        {
            var installed = _snapshot?.State is UpdateState.Available or UpdateState.Downloading or UpdateState.Ready
                ? Loc.Instance.Format("Update.Installed", InstalledVersion) : string.Empty;
            var checkedAt = _snapshot?.CheckedAt is { } time
                ? Loc.Instance.Format("Update.CheckedAt", time.ToLocalTime().ToString("g", Loc.Instance.Culture)) : string.Empty;
            return string.Join(Environment.NewLine, new[] { installed, checkedAt }.Where(value => value.Length > 0));
        }
    }

    public string ReadyInstruction => ShowApplyUpdate ? Loc.Instance["Update.InstallOnExit"] : string.Empty;

    internal void AttachUpdates(IApplicationUpdateController updates, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(dispatcher);
        if (_updates is not null) _updates.Changed -= OnUpdatesChanged;
        _updates = updates;
        _dispatcher = dispatcher;
        updates.Changed += OnUpdatesChanged;
        RefreshUpdates();
    }

    private string ErrorMessage()
    {
        var stage = _snapshot?.Failure?.Stage switch
        {
            UpdateStage.Downloading => "Update.DownloadFailed",
            UpdateStage.Preparing => "Update.PrepareFailed",
            _ => "Update.CheckFailed",
        };
        var reason = _snapshot?.Failure?.Reason switch
        {
            UpdateFailureReason.Network => "Update.ErrorNetwork",
            UpdateFailureReason.Timeout => "Update.ErrorTimeout",
            UpdateFailureReason.InvalidFeed => "Update.ErrorFeed",
            UpdateFailureReason.Integrity => "Update.ErrorIntegrity",
            _ => "Update.ErrorUnavailable",
        };
        return Loc.Instance[stage] + " " + Loc.Instance[reason];
    }

    private void OnUpdatesChanged(object? sender, EventArgs e)
    {
        if (_dispatcher is { } dispatcher && !dispatcher.CheckAccess()) dispatcher.Post(RefreshUpdates);
        else RefreshUpdates();
    }

    private void OnCultureChanged(object? sender, EventArgs e) => OnUpdatesChanged(sender, e);

    private void RefreshUpdates()
    {
        if (_disposed) return;
        _snapshot = _updates?.Current;
        OnPropertyChanged(nameof(UpdateStatus));
        OnPropertyChanged(nameof(UpdateDetail));
        OnPropertyChanged(nameof(IsUpdateBusy));
        OnPropertyChanged(nameof(ShowDownloadUpdate));
        OnPropertyChanged(nameof(ShowApplyUpdate));
        OnPropertyChanged(nameof(ShowCancelUpdate));
        OnPropertyChanged(nameof(ShowReleaseLink));
        OnPropertyChanged(nameof(IsUpdateProgressIndeterminate));
        OnPropertyChanged(nameof(UpdateProgress));
        OnPropertyChanged(nameof(CheckUpdatesLabel));
        OnPropertyChanged(nameof(ReadyInstruction));
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        DownloadUpdateCommand.NotifyCanExecuteChanged();
        CancelUpdateCommand.NotifyCanExecuteChanged();
        ExitAndInstallCommand.NotifyCanExecuteChanged();
        OpenReleaseCommand.NotifyCanExecuteChanged();
    }

    private bool CanCheck() => !_disposed && _updates is not null && !IsUpdateBusy && _manualOperation is null;
    private bool CanDownload() => CanCheck() && ShowDownloadUpdate;
    private bool CanCancel() => _manualOperation is { IsCancellationRequested: false } && IsUpdateBusy;
    private bool CanApply() => CanCheck() && ShowApplyUpdate && ExitForUpdate is not null;
    private bool CanOpenRelease() => ShowReleaseLink;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private Task CheckForUpdatesAsync() => RunManualAsync(_snapshot?.State == UpdateState.Error && _snapshot.Failure?.Stage == UpdateStage.Downloading);

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task DownloadUpdateAsync() => RunManualAsync(download: true);

    private async Task RunManualAsync(bool download)
    {
        if (!CanCheck() || _updates is null) return;
        using var cancellation = new CancellationTokenSource();
        _manualOperation = cancellation;
        RefreshUpdates();
        try
        {
            if (download) await _updates.DownloadAsync(cancellation.Token);
            else await _updates.CheckAsync(notify: false, cancellationToken: cancellation.Token);
        }
        catch (OperationCanceledException) { /* The shared updater publishes its cancelled state. */ }
        finally { _manualOperation = null; RefreshUpdates(); }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelUpdate() => CancelManualUpdate();

    internal void CancelManualUpdate()
    {
        _manualOperation?.Cancel();
        RefreshUpdates();
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task ExitAndInstallAsync() => ExitForUpdate?.Invoke() ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanOpenRelease))]
    private void OpenRelease() => OpenUrl(ReleasePageUrl);

    internal static bool IsProjectReleaseUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "github.com" && uri.UserInfo.Length == 0
        && uri.IsDefaultPort && uri.AbsolutePath.StartsWith("/zab1yak/dynamic-island-windows/releases/", StringComparison.Ordinal)
        && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    [RelayCommand]
    private static void OpenRepository() => OpenUrl(RepositoryUrl);

    [RelayCommand]
    private static void OpenSupportEmail() => OpenUrl("mailto:denic8871@gmail.com");

    [RelayCommand]
    private static void OpenSupportTelegram() => OpenUrl("https://t.me/incdenis");

    [RelayCommand]
    private static void OpenNotices() => OpenUrl(RepositoryUrl + "/blob/main/THIRD_PARTY_NOTICES.md");

    private static void OpenUrl(string url)
    {
        try { using var _ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { /* No default browser: keep the settings window open. */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        CancelManualUpdate();
        _disposed = true;
        if (_updates is not null) _updates.Changed -= OnUpdatesChanged;
        Loc.Instance.CultureChanged -= OnCultureChanged;
        GC.SuppressFinalize(this);
    }
}

/// <summary>The settings window: sidebar of sections, content on the right.</summary>
internal sealed partial class SettingsViewModel : ObservableObject
{
    private readonly GeneralSettingsViewModel _general;
    private readonly ModulesSettingsViewModel _modulesPage;
    private readonly ModuleHost _modules;

    [ObservableProperty]
    private SettingsSection? _selected;

    public SettingsViewModel(GeneralSettingsViewModel general, ModulesSettingsViewModel modulesPage, AboutViewModel about, ModuleHost modules)
    {
        _general = general;
        _modulesPage = modulesPage;
        _modules = modules;
        About = about;
        Sections.Add(new SettingsSection("general", "Settings.General", "Settings", general));
        Sections.Add(new SettingsSection("island", "Settings.Island", "Monitor", new IslandSettingsPage(general)));
        Sections.Add(new SettingsSection("modules", "Settings.Modules", "LayoutGrid", modulesPage));
        foreach (var module in modules.AllModules.OrderBy(m => m.Order))
        {
            if (module.SettingsViewModel is { } content)
            {
                Sections.Add(new SettingsSection(module.Id, module.TitleKey, module.IconKey, content));
            }
        }

        Sections.Add(new SettingsSection("about", "Settings.About", "Info", about));
        Selected = Sections[0];
    }

    public ObservableCollection<SettingsSection> Sections { get; } = [];

    public AboutViewModel About { get; }

    public GeneralSettingsViewModel General => _general;

    /// <summary>Re-reads every page (when the window opens) and selects <paramref name="sectionId"/>.</summary>
    public void Open(string? sectionId)
    {
        _general.Reload();
        _modulesPage.Reload();
        foreach (var module in _modules.AllModules)
        {
            // Module settings view models re-read their stored values when requested.
            _ = module.SettingsViewModel;
        }

        Selected = Sections.FirstOrDefault(s => s.Id == sectionId) ?? Selected ?? Sections[0];
    }
}

/// <summary>Wrapper so the "Island" page has its own template while sharing <see cref="GeneralSettingsViewModel"/>.</summary>
internal sealed record IslandSettingsPage(GeneralSettingsViewModel Settings);
