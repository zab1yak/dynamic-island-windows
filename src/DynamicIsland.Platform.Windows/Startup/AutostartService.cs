using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DynamicIsland.Platform.Windows.Startup;

/// <summary>
/// Start with Windows through <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>
/// (per user, no admin rights). The command is the current executable with <c>--background</c>.
/// </summary>
public sealed class AutostartService(ILogger<AutostartService> logger)
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Name of the Run value.</summary>
    public const string ValueName = "DynamicIsland";

    /// <summary>Command line written to the Run key.</summary>
    public static string Command(string exePath) => $"\"{exePath}\" --background";

    /// <summary>Whether the Run value exists and points at <paramref name="exePath"/>.</summary>
    public static bool IsEnabled(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string value && string.Equals(value, Command(exePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Adds or removes the Run value. Returns <c>false</c> when the registry refused.</summary>
    public bool Set(bool enabled, string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                key.SetValue(ValueName, Command(exePath), RegistryValueKind.String);
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            logger.LogWarning(ex, "Could not change autostart");
            return false;
        }
    }
}
