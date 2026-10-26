namespace DynamicIsland.App.Hosting;

/// <summary>Parsed command-line switches.</summary>
/// <param name="Background">Started by Windows autostart: no greeting, no windows.</param>
/// <param name="OpenSettings">Open the settings window.</param>
/// <param name="Show">Expand the island once.</param>
/// <param name="Restart">Started by "Restart": wait for the previous process to exit.</param>
internal sealed record CommandLine(bool Background, bool OpenSettings, bool Show, bool Restart)
{
    public const string BackgroundSwitch = "--background";
    public const string SettingsSwitch = "--settings";
    public const string ShowSwitch = "--show";
    public const string RestartSwitch = "--restart";

    /// <summary>Parses known switches case-insensitively; unknown arguments are ignored.</summary>
    public static CommandLine Parse(IEnumerable<string> args)
    {
        var set = new HashSet<string>(args, StringComparer.OrdinalIgnoreCase);
        return new CommandLine(
            set.Contains(BackgroundSwitch),
            set.Contains(SettingsSwitch),
            set.Contains(ShowSwitch),
            set.Contains(RestartSwitch));
    }
}
