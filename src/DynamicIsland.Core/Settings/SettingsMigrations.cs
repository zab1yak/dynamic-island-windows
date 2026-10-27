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

        // v0.2.0 made 83 % the default; that is undone in v0.2.1, so there is nothing to change from 1 to 2.
        [1] = _ => { },

        // v0.2.1: back to the v0.1.0 size. Users still on the v0.2.0 default (0.83) return to 100 %;
        // anyone who chose another size keeps it.
        [2] = json =>
        {
            if (json["islandScale"] is JsonValue v && v.TryGetValue<double>(out var scale) && Math.Abs(scale - 0.83) < 0.001)
            {
                json["islandScale"] = AppSettings.DefaultIslandScale;
            }
        },
    };
}
