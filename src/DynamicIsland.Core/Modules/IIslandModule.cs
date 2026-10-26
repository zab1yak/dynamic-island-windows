namespace DynamicIsland.Core.Modules;

/// <summary>
/// A page of the island (Media, Pomodoro, Water, Shelf, Todo…). The island window knows only this
/// contract: views are resolved by WPF from the view-model type through the DataTemplates the
/// module ships in its <see cref="ModuleResources"/> dictionary.
/// </summary>
public interface IIslandModule
{
    /// <summary>Stable id, also used as the key for settings and data files (for example <c>media</c>).</summary>
    string Id { get; }

    /// <summary>Localization key of the page title.</summary>
    string TitleKey { get; }

    /// <summary>Icon key resolved by the UI icon provider.</summary>
    string IconKey { get; }

    /// <summary>Default page order; lower comes first. The user can override it in settings.</summary>
    int Order { get; }

    /// <summary>View model shown on the expanded island page.</summary>
    object ExpandedViewModel { get; }

    /// <summary>View model for the module's section in the settings window, or <c>null</c>.</summary>
    object? SettingsViewModel { get; }

    /// <summary>Live activities the module can raise (track playing, timer running), or <c>null</c>.</summary>
    ILiveActivitySource? LiveActivities { get; }

    /// <summary>Whether files can be dropped onto this module's page.</summary>
    bool AcceptsDrop => false;

    /// <summary>Loads persisted state and starts listeners. Called once on the UI thread.</summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>Persists state and stops listeners. Must finish within ~2 seconds.</summary>
    Task ShutdownAsync();
}
