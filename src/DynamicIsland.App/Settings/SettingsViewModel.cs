using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.App.Island;
using DynamicIsland.Core.Input;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Settings;
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

    [ObservableProperty]
    private MonitorChoice? _monitor;

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

    public GeneralSettingsViewModel(ISettingsService settings, AutostartService autostart)
    {
        _settings = settings;
        _autostart = autostart;
        Reload();
    }

    public ObservableCollection<MonitorChoice> Monitors { get; } = [];

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
        IslandScalePercent = (int)Math.Round(s.IslandScale * 100);
        ExpandDelayMs = s.HoverExpandDelayMs;
        CollapseDelayMs = s.HoverCollapseDelayMs;
        Hotkey = s.ToggleHotkey;
        HotkeyError = null;

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
            HotkeyError = null;
            Hotkey = gesture.Value.ToString();
        }
        else
        {
            HotkeyError = Loc.Instance["Settings.HotkeyInvalid"];
        }
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

    partial void OnMonitorChanged(MonitorChoice? value) => Save(s => s.MonitorDeviceName = value?.DeviceName);

    partial void OnIslandScalePercentChanged(int value) => Save(s => s.IslandScale = value / 100.0);

    partial void OnExpandDelayMsChanged(int value) => Save(s => s.HoverExpandDelayMs = value);

    partial void OnCollapseDelayMsChanged(int value) => Save(s => s.HoverCollapseDelayMs = value);

    partial void OnHotkeyChanged(string value) => Save(s => s.ToggleHotkey = value);

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
        foreach (var module in ordered.Where(m => m.Id != ModuleHost.SettingsPageId))
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

/// <summary>About page.</summary>
internal sealed partial class AboutViewModel : ObservableObject
{
    public const string RepositoryUrl = "https://github.com/zab1yak/dynamic-island-windows";

    public string Version { get; } = typeof(AboutViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Set by the update service (task: installer and updates).</summary>
    [ObservableProperty]
    private string? _updateStatus;

    /// <summary>Hook for the update check; null when updates are unavailable (development build).</summary>
    public Func<Task<string>>? CheckUpdates { get; set; }

    [RelayCommand]
    private static void OpenRepository() => OpenUrl(RepositoryUrl);

    [RelayCommand]
    private static void OpenNotices() => OpenUrl(RepositoryUrl + "/blob/main/THIRD_PARTY_NOTICES.md");

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        UpdateStatus = Loc.Instance["Settings.UpdatesChecking"];
        UpdateStatus = CheckUpdates is null ? Loc.Instance["Settings.UpdatesUnavailable"] : await CheckUpdates();
    }

    private static void OpenUrl(string url)
    {
        // Only our own constant https URLs are opened here.
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No default browser: nothing else to do.
        }
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
