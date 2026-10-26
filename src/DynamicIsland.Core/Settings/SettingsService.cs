using System.Text.Json;
using System.Text.Json.Nodes;
using DynamicIsland.Core.Storage;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.Core.Settings;

/// <summary>
/// <see cref="ISettingsService"/> stored in <c>settings.json</c>. Migrations run on the raw JSON
/// tree, so a field can be renamed or reshaped without keeping legacy properties on the model.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    /// <summary>File name relative to the data folder.</summary>
    public const string FileName = "settings.json";

    private readonly IStorageService _storage;
    private readonly ILogger<SettingsService> _logger;
    private readonly IReadOnlyDictionary<int, Action<JsonObject>> _migrations;

    /// <summary>Creates the service with the built-in migrations.</summary>
    public SettingsService(IStorageService storage, ILogger<SettingsService> logger)
        : this(storage, logger, SettingsMigrations.All)
    {
    }

    /// <summary>Creates the service with custom migrations (used by tests).</summary>
    /// <param name="migrations">Key = version the migration upgrades <em>from</em>.</param>
    public SettingsService(IStorageService storage, ILogger<SettingsService> logger, IReadOnlyDictionary<int, Action<JsonObject>> migrations)
    {
        _storage = storage;
        _logger = logger;
        _migrations = migrations;
    }

    /// <inheritdoc />
    public AppSettings Current { get; private set; } = new();

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var json = await _storage.LoadAsync<JsonObject>(FileName, cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            Current = new AppSettings();
            return;
        }

        var version = json["schemaVersion"]?.GetValue<int>() ?? 0;
        var migrated = false;
        while (version < AppSettings.CurrentSchemaVersion)
        {
            if (_migrations.TryGetValue(version, out var migrate))
            {
                migrate(json);
            }

            version++;
            json["schemaVersion"] = version;
            migrated = true;
        }

        try
        {
            Current = json.Deserialize<AppSettings>(JsonDefaults.Options) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Settings could not be read; defaults are used");
            Current = new AppSettings();
        }

        Normalize(Current);
        if (migrated)
        {
            _logger.LogInformation("Settings migrated to schema {Version}", AppSettings.CurrentSchemaVersion);
            await _storage.SaveAsync(FileName, Current, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Normalize(Current);
        await _storage.SaveAsync(FileName, Current, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clamps values the UI or a hand-edited file could have set out of range.</summary>
    internal static void Normalize(AppSettings s)
    {
        s.SchemaVersion = AppSettings.CurrentSchemaVersion;
        s.IslandScale = Math.Clamp(double.IsFinite(s.IslandScale) ? s.IslandScale : 1.0, 0.8, 1.3);
        s.HoverExpandDelayMs = Math.Clamp(s.HoverExpandDelayMs, 0, 2000);
        s.HoverCollapseDelayMs = Math.Clamp(s.HoverCollapseDelayMs, 100, 5000);
        s.ModuleOrder = s.ModuleOrder?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList() ?? [];
        s.DisabledModules = s.DisabledModules?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList() ?? [];
        if (string.IsNullOrWhiteSpace(s.ToggleHotkey))
        {
            s.ToggleHotkey = "Ctrl+Alt+Space";
        }

        if (s.Language is not null and not "ru" and not "en")
        {
            s.Language = null;
        }
    }
}
