using System.ComponentModel;
using System.Diagnostics;
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
    /// Starts a fresh process and exits. The new process waits (up to 10 s) for this one to release
    /// the single-instance mutex, which happens when <c>Main</c> disposes it.
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
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                ArgumentList = { CommandLine.RestartSwitch, CommandLine.BackgroundSwitch },
            });
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
