using System.Text.Json;
using System.Text.Json.Nodes;
using DynamicIsland.Core.Storage;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.Core.Settings;

/// <summary>
/// <see cref="ISettingsService"/> stored in <c>settings.json</c>. Migrations run on the raw JSON
/// tree, so a field can be renamed or reshaped without keeping legacy properties on the model.
/// A file that cannot be read or migrated is backed up before defaults are used, so the user's
/// data is never silently overwritten.
/// </summary>
public sealed class SettingsService : ISettingsService, IDisposable
{
    /// <summary>File name relative to the data folder.</summary>
    public const string FileName = "settings.json";

    private readonly IStorageService _storage;
    private readonly ILogger<SettingsService> _logger;
    private readonly IReadOnlyDictionary<int, Action<JsonObject>> _migrations;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

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
    public bool IsFirstRun { get; private set; }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var json = await _storage.LoadAsync<JsonObject>(FileName, cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            Current = new AppSettings();
            // A damaged file was moved aside as "*.corrupt": that is not a fresh installation.
            var folder = Path.GetDirectoryName(_storage.ResolvePath(FileName))!;
            IsFirstRun = !Directory.Exists(folder) || Directory.GetFiles(folder, FileName + ".*.corrupt").Length == 0;
            return;
        }

        var version = json["schemaVersion"] is JsonValue v && v.TryGetValue<int>(out var parsed) ? parsed : 0;
        if (version > AppSettings.CurrentSchemaVersion)
        {
            // Written by a newer build (the user downgraded): keep a copy before we save our shape over it.
            BackUp($"v{version}.bak");
        }

        AppSettings? loaded;
        var migrated = false;
        try
        {
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

            loaded = json.Deserialize<AppSettings>(JsonDefaults.Options);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            _logger.LogError(ex, "Settings could not be read; a backup was kept and defaults are used");
            BackUp("unreadable.bak");
            loaded = null;
            migrated = false;
        }

        Current = loaded ?? new AppSettings();
        Normalize(Current);
        if (migrated)
        {
            _logger.LogInformation("Settings migrated to schema {Version}", AppSettings.CurrentSchemaVersion);
            await _storage.SaveAsync(FileName, Snapshot(Current), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The settings are snapshotted on the caller's thread and the continuation returns to the
    /// caller's context, so <see cref="Changed"/> is raised on the UI thread when called from it.
    /// </remarks>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Normalize(Current);
        var snapshot = Snapshot(Current);
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            await _storage.SaveAsync(FileName, snapshot, cancellationToken);
        }
        finally
        {
            _saveGate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose() => _saveGate.Dispose();

    /// <summary>Clamps values the UI or a hand-edited file could have set out of range.</summary>
    internal static void Normalize(AppSettings s)
    {
        s.SchemaVersion = AppSettings.CurrentSchemaVersion;
        s.IslandScale = Math.Clamp(double.IsFinite(s.IslandScale) ? s.IslandScale : AppSettings.DefaultIslandScale, 0.7, 1.3);
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

    private static JsonObject Snapshot(AppSettings settings) =>
        JsonSerializer.SerializeToNode(settings, JsonDefaults.Options)!.AsObject();

    private void BackUp(string suffix)
    {
        try
        {
            var path = _storage.ResolvePath(FileName);
            File.Copy(path, $"{path}.{suffix}", overwrite: true);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not back up settings");
        }
    }
}
