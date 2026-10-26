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
    };
}
