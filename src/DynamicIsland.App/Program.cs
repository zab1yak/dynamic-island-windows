using System.Diagnostics;
using DynamicIsland.App.Hosting;
using DynamicIsland.Platform.Windows;
using Serilog;
using Serilog.Extensions.Logging;
using Velopack;

namespace DynamicIsland.App;

/// <summary>Process entry point: single-instance check, host, then the WPF application.</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Velopack install/update/uninstall hooks run here and exit before anything else starts.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => RemoveAutostart())
            .Run();

        var commandLine = CommandLine.Parse(args);
        Log.Logger = Logging.CreateLogger();

        if (commandLine.RestartFromPid is { } pid)
        {
            WaitForPreviousProcess(pid);
        }

        using var instance = SingleInstance.Acquire(commandLine.RestartFromPid is null ? TimeSpan.Zero : TimeSpan.FromSeconds(5));
        if (!instance.IsPrimary)
        {
            // Another island is running: hand it our arguments (a plain second launch means "show").
            var forwarded = args.Length > 0 ? args : [CommandLine.ShowSwitch];
            if (!SingleInstance.TrySendToPrimary(forwarded, TimeSpan.FromSeconds(5)))
            {
                Log.Warning("Another instance is running but did not accept the arguments");
            }

            Log.CloseAndFlush();
            return 0;
        }

        try
        {
            using var loggerFactory = new SerilogLoggerFactory(Log.Logger);
            instance.Listen(loggerFactory.CreateLogger(nameof(SingleInstance)));

            Log.Information("Starting Dynamic Island {Version}", typeof(Program).Assembly.GetName().Version);
            // Modules declare pack:// URIs while the host is built, before WPF registers the scheme itself.
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            using var host = AppHostBuilder.Build(commandLine, instance);
            var app = new App(host, commandLine, instance);
            app.InitializeComponent();
            return app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Dynamic Island failed to start");
            return 1;
        }
        finally
        {
            Log.Information("Dynamic Island stopped");
            Log.CloseAndFlush();
        }
    }

    /// <summary>Uninstall must not leave a Run entry pointing at a deleted exe.</summary>
    private static void RemoveAutostart()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        key?.DeleteValue(DynamicIsland.Platform.Windows.Startup.AutostartService.ValueName, throwOnMissingValue: false);
    }

    private static void WaitForPreviousProcess(int pid)
    {
        try
        {
            using var previous = Process.GetProcessById(pid);
            if (!previous.WaitForExit(TimeSpan.FromSeconds(15)))
            {
                Log.Warning("Previous process {Pid} did not exit in time", pid);
            }
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }
}
