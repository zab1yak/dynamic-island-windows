using DynamicIsland.App.Hosting;
using DynamicIsland.Core;
using DynamicIsland.Platform.Windows;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace DynamicIsland.App;

/// <summary>Process entry point: single-instance check, host, then the WPF application.</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var commandLine = CommandLine.Parse(args);
        Log.Logger = Logging.CreateLogger();

        using var instance = SingleInstance.Acquire(commandLine.Restart ? TimeSpan.FromSeconds(10) : TimeSpan.Zero);
        if (!instance.IsPrimary)
        {
            // Another island is running: hand it our arguments (a plain second launch means "show").
            var forwarded = args.Length > 0 ? args : [CommandLine.ShowSwitch];
            SingleInstance.TrySendToPrimary(forwarded, TimeSpan.FromSeconds(2));
            Log.CloseAndFlush();
            return 0;
        }

        try
        {
            Log.Information("Starting Dynamic Island {Version}", typeof(Program).Assembly.GetName().Version);
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
}
