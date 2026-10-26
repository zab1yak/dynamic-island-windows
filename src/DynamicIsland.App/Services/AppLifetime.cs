using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using DynamicIsland.App.Hosting;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App.Services;

/// <summary>Exit and restart of the application.</summary>
internal sealed class AppLifetime(ILogger<AppLifetime> logger)
{
    /// <summary>Shuts the application down gracefully (modules save their state).</summary>
    public void Exit()
    {
        logger.LogInformation("Exit requested");
        Application.Current.Shutdown();
    }

    /// <summary>
    /// Starts a fresh process and exits. The new process waits for this process id to exit before
    /// it takes the single-instance mutex.
    /// </summary>
    public void Restart()
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            logger.LogError("Cannot restart: process path is unknown");
            return;
        }

        try
        {
            using var started = Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                ArgumentList =
                {
                    CommandLine.RestartSwitch + Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                    CommandLine.BackgroundSwitch,
                },
            });
            if (started is null)
            {
                logger.LogError("Restart failed: the new process did not start");
                return;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            logger.LogError(ex, "Restart failed");
            return;
        }

        logger.LogInformation("Restarting");
        Application.Current.Shutdown();
    }
}
