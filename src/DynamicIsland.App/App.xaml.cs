using System.Windows;
using DynamicIsland.App.Hosting;
using DynamicIsland.App.Services;
using DynamicIsland.Core.Settings;
using DynamicIsland.Platform.Windows;
using DynamicIsland.UI.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App;

/// <summary>WPF application: wires the host services to the UI thread.</summary>
public partial class App : Application
{
    private readonly IHost _host;
    private readonly CommandLine _commandLine;
    private readonly SingleInstance _instance;
    private readonly ILogger<App> _logger;

    internal App(IHost host, CommandLine commandLine, SingleInstance instance)
    {
        _host = host;
        _commandLine = commandLine;
        _instance = instance;
        _logger = host.Services.GetRequiredService<ILogger<App>>();
        // Attach before anything else runs on the dispatcher, so start-up errors are logged too.
        host.Services.GetRequiredService<GlobalExceptionHandler>().Attach(this);
    }

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _ = StartAsync();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host.Services.GetRequiredService<TrayIconService>().Dispose();
            // Off the UI thread: a hosted service waiting for the dispatcher must not deadlock us.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            Task.Run(() => _host.StopAsync(timeout.Token)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during shutdown");
        }

        base.OnExit(e);
    }

    private async Task StartAsync()
    {
        try
        {
            var services = _host.Services;
            await _host.StartAsync();
            var settings = services.GetRequiredService<ISettingsService>();
            await settings.LoadAsync();
            Loc.Instance.SetLanguage(settings.Current.Language);

            services.GetRequiredService<TrayIconService>().Show();

            _instance.SetHandler(args => Dispatcher.BeginInvoke(() => HandleArguments(CommandLine.Parse(args))));
            HandleArguments(_commandLine);
            _logger.LogInformation("Started (background: {Background})", _commandLine.Background);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Start-up failed");
            Shutdown(1);
        }
    }

    private void HandleArguments(CommandLine commandLine)
    {
        _logger.LogDebug("Arguments: {CommandLine}", commandLine);
    }
}
