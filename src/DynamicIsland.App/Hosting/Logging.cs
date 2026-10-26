using DynamicIsland.Core;
using Serilog;
using Serilog.Events;

namespace DynamicIsland.App.Hosting;

/// <summary>Serilog configuration: rolling files in <c>%LOCALAPPDATA%\DynamicIsland\logs</c>.</summary>
internal static class Logging
{
    public static ILogger CreateLogger()
    {
        var config = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                System.IO.Path.Combine(AppPaths.Logs, "island-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
#if DEBUG
        config = config.MinimumLevel.Debug();
#endif
        return config.CreateLogger();
    }
}
