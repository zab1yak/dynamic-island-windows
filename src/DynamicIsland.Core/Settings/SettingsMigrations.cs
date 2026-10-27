using System.Text.Json.Nodes;

namespace DynamicIsland.Core.Settings;

/// <summary>
/// Schema migrations for <c>settings.json</c>. Key = version the migration upgrades from.
/// Version 0 means "file written before versioning existed"; it already matches schema 1.
/// </summary>
internal static class SettingsMigrations
{
    public static IReadOnlyDictionary<int, Action<JsonObject>> All { get; } = new Dictionary<int, Action<JsonObject>>
    {
        [0] = _ => { },

        // v0.2.0: the island became smaller. Users who kept the old default (1.0) get the new one;
        // anyone who chose another size keeps it.
        [1] = json =>
        {
            if (json["islandScale"] is JsonValue v && v.TryGetValue<double>(out var scale) && Math.Abs(scale - 1.0) < 0.001)
            {
                json["islandScale"] = AppSettings.DefaultIslandScale;
            }
        },
    };
}
