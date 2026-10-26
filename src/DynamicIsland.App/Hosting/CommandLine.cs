using System.Globalization;

namespace DynamicIsland.App.Hosting;

/// <summary>Parsed command-line switches.</summary>
/// <param name="Background">Started by Windows autostart: no greeting, no windows.</param>
/// <param name="OpenSettings">Open the settings window.</param>
/// <param name="Show">Expand the island once.</param>
/// <param name="RestartFromPid">Started by "Restart": the id of the process to wait for, or <c>null</c>.</param>
internal sealed record CommandLine(bool Background, bool OpenSettings, bool Show, int? RestartFromPid)
{
    public const string BackgroundSwitch = "--background";
    public const string SettingsSwitch = "--settings";
    public const string ShowSwitch = "--show";
    public const string RestartSwitch = "--restart=";

    /// <summary>Parses known switches case-insensitively; unknown arguments are ignored.</summary>
    public static CommandLine Parse(IEnumerable<string> args)
    {
        var background = false;
        var settings = false;
        var show = false;
        int? restart = null;
        foreach (var arg in args)
        {
            if (arg.Equals(BackgroundSwitch, StringComparison.OrdinalIgnoreCase))
            {
                background = true;
            }
            else if (arg.Equals(SettingsSwitch, StringComparison.OrdinalIgnoreCase))
            {
                settings = true;
            }
            else if (arg.Equals(ShowSwitch, StringComparison.OrdinalIgnoreCase))
            {
                show = true;
            }
            else if (arg.StartsWith(RestartSwitch, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(arg.AsSpan(RestartSwitch.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
            {
                restart = pid;
            }
        }

        return new CommandLine(background, settings, show, restart);
    }
}
