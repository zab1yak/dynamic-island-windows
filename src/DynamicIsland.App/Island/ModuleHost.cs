using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Settings;
using DynamicIsland.UI.Localization;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App.Island;

/// <summary>
/// Owns the island pages: merges every module's DataTemplates into the application resources,
/// initializes modules (a failing module is skipped, not fatal), orders them by settings and keeps
/// the current page. The window only binds to <see cref="CurrentPage"/>.
/// </summary>
internal sealed partial class ModuleHost : ObservableObject
{
    private readonly IReadOnlyList<IIslandModule> _all;
    private readonly IEnumerable<ModuleResources> _resources;
    private readonly ISettingsService _settings;
    private readonly ILogger<ModuleHost> _logger;
    private readonly HashSet<string> _faulted = new(StringComparer.Ordinal);

    [ObservableProperty]
    private IReadOnlyList<IIslandModule> _pages = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage), nameof(CurrentTitle), nameof(CurrentTitleMain), nameof(CurrentTitleAccent),
        nameof(CurrentTitleAccentBrush), nameof(CurrentShowsTitle))]
    private int _currentIndex;

    public ModuleHost(IEnumerable<IIslandModule> modules, IEnumerable<ModuleResources> resources, ISettingsService settings, ILogger<ModuleHost> logger)
    {
        _all = modules.ToList();
        _resources = resources;
        _settings = settings;
        _logger = logger;
        settings.Changed += (_, _) => Rebuild();
        Loc.Instance.CultureChanged += (_, _) => NotifyTitle();
    }

    /// <summary>All registered modules, enabled or not (for the settings window).</summary>
    public IReadOnlyList<IIslandModule> AllModules => _all;

    /// <summary>The visible page's view model, or <c>null</c> when no module is enabled.</summary>
    public object? CurrentPage => CurrentModule?.ExpandedViewModel;

    /// <summary>The visible module.</summary>
    public IIslandModule? CurrentModule => CurrentIndex >= 0 && CurrentIndex < Pages.Count ? Pages[CurrentIndex] : null;

    /// <summary>Localized title of the visible page.</summary>
    public string CurrentTitle => CurrentModule is { } m ? Loc.Instance[m.TitleKey] : string.Empty;

    /// <summary>White part of the title («Water » in «Water Reminder»).</summary>
    public string CurrentTitleMain => SplitTitle(CurrentTitle, CurrentTitleAccent).Main;

    /// <summary>Coloured part of the title («Reminder»), or empty.</summary>
    public string CurrentTitleAccent => CurrentModule?.TitleAccentKey is { } key ? SplitTitle(CurrentTitle, Loc.Instance[key]).Accent : string.Empty;

    /// <summary>Brush of the coloured part.</summary>
    public object? CurrentTitleAccentBrush => CurrentModule is { } m ? System.Windows.Application.Current?.TryFindResource(m.TitleAccentBrushKey) : null;

    /// <summary>Whether the header shows the title (not on the music page).</summary>
    public bool CurrentShowsTitle => CurrentModule?.ShowsTitle ?? true;

    /// <summary>Splits a full title into its white and coloured parts; a mismatch keeps it single-colour.</summary>
    internal static (string Main, string Accent) SplitTitle(string full, string accent) =>
        accent.Length > 0 && full.Length > accent.Length && full.EndsWith(accent, StringComparison.Ordinal)
            ? (full[..^accent.Length], accent)
            : (full, string.Empty);

    private void NotifyTitle()
    {
        OnPropertyChanged(nameof(CurrentTitle));
        OnPropertyChanged(nameof(CurrentTitleMain));
        OnPropertyChanged(nameof(CurrentTitleAccent));
        OnPropertyChanged(nameof(CurrentTitleAccentBrush));
        OnPropertyChanged(nameof(CurrentShowsTitle));
    }

    /// <summary>Merges templates and initializes modules. Call once on the UI thread.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        foreach (var resource in _resources)
        {
            try
            {
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = resource.Dictionary });
            }
            catch (Exception ex) when (ex is IOException or System.Windows.Markup.XamlParseException)
            {
                _logger.LogError(ex, "Could not load module resources {Uri}", resource.Dictionary);
            }
        }

        foreach (var module in _all)
        {
            try
            {
                await module.InitializeAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _faulted.Add(module.Id);
                _logger.LogError(ex, "Module {Module} failed to start and is hidden", module.Id);
            }
        }

        Rebuild();
    }

    /// <summary>Asks every module to save now (Windows session ending); modules keep running.</summary>
    public async Task FlushAsync()
    {
        foreach (var module in _all.Where(m => !_faulted.Contains(m.Id)))
        {
            try
            {
                await module.FlushAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Module {Module} failed to flush", module.Id);
            }
        }
    }

    /// <summary>Lets every module save its state.</summary>
    public async Task ShutdownAsync()
    {
        foreach (var module in _all.Where(m => !_faulted.Contains(m.Id)))
        {
            try
            {
                await module.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Module {Module} failed to shut down cleanly", module.Id);
            }
        }
    }

    /// <summary>Shows the page of <paramref name="moduleId"/> if it is enabled.</summary>
    public void ShowModule(string moduleId)
    {
        var index = Pages.ToList().FindIndex(m => m.Id == moduleId);
        if (index >= 0)
        {
            CurrentIndex = index;
        }
    }

    /// <summary>Next page (wraps around).</summary>
    [RelayCommand]
    public void NextPage() => Step(+1);

    /// <summary>Previous page (wraps around).</summary>
    [RelayCommand]
    public void PreviousPage() => Step(-1);

    /// <summary>Moves by <paramref name="delta"/> pages.</summary>
    public void Step(int delta)
    {
        if (Pages.Count > 1)
        {
            CurrentIndex = ((CurrentIndex + delta) % Pages.Count + Pages.Count) % Pages.Count;
        }
    }

    /// <summary>Applies the order and enabled set from settings, keeping the current page if possible.</summary>
    internal void Rebuild()
    {
        var currentId = CurrentModule?.Id;
        Pages = Order(_all.Where(m => !_faulted.Contains(m.Id)), _settings.Current);
        var index = Pages.ToList().FindIndex(m => m.Id == currentId);
        CurrentIndex = index >= 0 ? index : 0;
        OnPropertyChanged(nameof(CurrentPage));
        NotifyTitle();
    }

    /// <summary>Saved order first, then new modules by their default order; disabled ones removed.</summary>
    /// <remarks>The «Настройки» page is always present and always last, whatever the settings say.</remarks>
    internal static IReadOnlyList<IIslandModule> Order(IEnumerable<IIslandModule> modules, AppSettings settings)
    {
        var order = settings.ModuleOrder;
        return modules
            .Where(m => m.Id == SettingsPageId || !settings.DisabledModules.Contains(m.Id))
            .OrderBy(m => m.Id == SettingsPageId ? 1 : 0)
            .ThenBy(m => order.IndexOf(m.Id) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(m => m.Order)
            .ToList();
    }

    /// <summary>Id of the settings page inside the island; it cannot be hidden or moved.</summary>
    public const string SettingsPageId = "settings";
}
