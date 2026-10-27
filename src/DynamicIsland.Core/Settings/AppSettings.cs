namespace DynamicIsland.Core.Settings;

/// <summary>
/// User preferences persisted to <c>settings.json</c>. Mutable by design: the settings UI binds
/// to it and <see cref="ISettingsService.SaveAsync"/> persists the current state.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Current schema version; bump it and add a migration when the shape changes.</summary>
    public const int CurrentSchemaVersion = 3;

    /// <summary>Schema version the document was written with.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Start with Windows (HKCU Run key).</summary>
    public bool LaunchAtStartup { get; set; } = true;

    /// <summary>Device name of the monitor that hosts the island; <c>null</c> means the primary monitor.</summary>
    public string? MonitorDeviceName { get; set; }

    /// <summary>Default island scale: the v0.1.0 design size.</summary>
    public const double DefaultIslandScale = 1.0;

    /// <summary>Scale applied to the whole island — shape, radii, text (0.7–1.3).</summary>
    public double IslandScale { get; set; } = DefaultIslandScale;

    /// <summary>Delay before the island expands on hover, in milliseconds.</summary>
    public int HoverExpandDelayMs { get; set; } = 150;

    /// <summary>Delay before the island collapses after the cursor leaves, in milliseconds.</summary>
    public int HoverCollapseDelayMs { get; set; } = 450;

    /// <summary>Global hotkey that toggles the island, for example <c>Ctrl+Alt+Space</c>.</summary>
    public string ToggleHotkey { get; set; } = "Ctrl+Alt+Space";

    /// <summary>Hide the island while a fullscreen app, game or presentation is in the foreground.</summary>
    public bool HideInFullscreen { get; set; } = true;

    /// <summary>UI language: <c>null</c> follows Windows, otherwise <c>ru</c> or <c>en</c>.</summary>
    public string? Language { get; set; }

    /// <summary>Module ids in page order. Unknown ids are ignored, missing modules are appended.</summary>
    public List<string> ModuleOrder { get; set; } = [];

    /// <summary>Ids of modules the user switched off.</summary>
    public List<string> DisabledModules { get; set; } = [];

    /// <summary>Show a red dot while a known screen recorder runs (heuristic, off by default).</summary>
    public bool DetectScreenRecording { get; set; }

    /// <summary>Check GitHub Releases for updates on start.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Island background: black (default), translucent black or light grey with dark text.</summary>
    public IslandBackground Background { get; set; } = IslandBackground.Black;

    /// <summary>Locked island: compact only, no hover or clicks (unlock in settings or with the hotkey).</summary>
    public bool IslandLocked { get; set; }
}

/// <summary>Island background colour (the settings page «Внешний вид»).</summary>
public enum IslandBackground
{
    /// <summary>Pure black, as the reference.</summary>
    Black,

    /// <summary>Black at about 75 % opacity.</summary>
    Translucent,

    /// <summary>Light grey with dark text.</summary>
    LightGray,
}
