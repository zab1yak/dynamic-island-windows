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
    private ILogger<App>? _logger;

    internal App(IHost host, CommandLine commandLine, SingleInstance instance)
    {
        _host = host;
        _commandLine = commandLine;
        _instance = instance;
    }

    /// <inheritdoc />
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var services = _host.Services;
        _logger = services.GetRequiredService<ILogger<App>>();
        services.GetRequiredService<GlobalExceptionHandler>().Attach(this);

        try
        {
            await _host.StartAsync();
            var settings = services.GetRequiredService<ISettingsService>();
            await settings.LoadAsync();
            Loc.Instance.SetLanguage(settings.Current.Language);

            services.GetRequiredService<TrayIconService>().Show();

            _instance.ArgumentsReceived += (_, args) => Dispatcher.BeginInvoke(() => HandleArguments(CommandLine.Parse(args)));
            _instance.StartServer(services.GetRequiredService<ILogger<SingleInstance>>());
            HandleArguments(_commandLine);
            _logger.LogInformation("Started (background: {Background})", _commandLine.Background);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Start-up failed");
            Shutdown(1);
        }
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host.Services.GetRequiredService<TrayIconService>().Dispose();
            _host.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during shutdown");
        }

        base.OnExit(e);
    }

    private void HandleArguments(CommandLine commandLine)
    {
        _logger?.LogDebug("Arguments: {CommandLine}", commandLine);
    }
}
