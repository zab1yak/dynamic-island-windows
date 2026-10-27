using System.Windows;
using DynamicIsland.App.Hosting;
using DynamicIsland.App.Island;
using DynamicIsland.App.Services;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Settings;
using DynamicIsland.Platform.Windows;
using DynamicIsland.Platform.Windows.Status;
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
    private IslandWindow? _island;
    private bool _settingsCreated;
    private bool _modulesReady;

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
            _island?.CloseForExit();
            if (_settingsCreated)
            {
                _host.Services.GetRequiredService<Settings.SettingsWindow>().CloseForExit();
            }
            Task.Run(() => _host.Services.GetRequiredService<ModuleHost>().ShutdownAsync()).GetAwaiter().GetResult();
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

    /// <inheritdoc />
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // WM_QUERYENDSESSION: save the last track and other state before Windows logs off.
        _logger.LogInformation("Windows session ending ({Reason})", e.ReasonSessionEnding);
        try
        {
            Task.Run(() => _host.Services.GetRequiredService<ModuleHost>().FlushAsync()).Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException ex)
        {
            _logger.LogError(ex, "Flushing state at session end failed");
        }

        base.OnSessionEnding(e);
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

            settings.Changed += (_, _) => Loc.Instance.SetLanguage(settings.Current.Language);
            var tray = services.GetRequiredService<TrayIconService>();
            tray.SettingsRequested += (_, _) => OpenSettings(null);
            services.GetRequiredService<IslandHostService>().SettingsRequested += (_, section) => OpenSettings(section);
            tray.Show();

            await services.GetRequiredService<ModuleHost>().InitializeAsync(CancellationToken.None);
            _modulesReady = true;
            if (settings.IsFirstRun && Environment.ProcessPath is { } exe && DynamicIsland.Platform.Windows.Startup.AutostartService.IsInstalledBuild(exe))
            {
                // On by default after installation; the user can switch it off in Settings.
                services.GetRequiredService<DynamicIsland.Platform.Windows.Startup.AutostartService>().Set(settings.Current.LaunchAtStartup, exe);
                await settings.SaveAsync();
            }

            if (_commandLine.Background)
            {
                // Started at sign-in: let the shell and the taskbar settle first.
                await Task.Delay(TimeSpan.FromSeconds(3));
            }

            var island = _island = services.GetRequiredService<IslandWindow>();
            island.HotkeyUnavailable += (_, hotkey) =>
                tray.Notify(Loc.Instance["Hotkey.TakenTitle"], Loc.Instance.Format("Hotkey.Taken", hotkey));
            island.Show();
            island.WarmUpPages(services.GetRequiredService<ModuleHost>().AllModules.Select(m => m.ExpandedViewModel));
            var modules = services.GetRequiredService<ModuleHost>();
            void UpdateDrop() => island.DropEnabled = modules.Pages.Any(m => m.AcceptsDrop);
            UpdateDrop();
            modules.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ModuleHost.Pages))
                {
                    UpdateDrop();
                }
            };
            island.DropTargetShown += (_, _) => modules.ShowModule(modules.Pages.FirstOrDefault(m => m.AcceptsDrop)?.Id ?? string.Empty);
            island.Dropped += async (_, payload) =>
            {
                try
                {
                    if (modules.Pages.OfType<IDropHandler>().FirstOrDefault() is { } handler)
                    {
                        await handler.HandleDropAsync(payload, CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Handling a drop failed");
                }
            };

            var status = services.GetRequiredService<StatusViewModel>();
            status.Start();
            var privacy = services.GetRequiredService<PrivacyMonitor>();
            privacy.SetRecordingDetection(settings.Current.DetectScreenRecording);
            settings.Changed += (_, _) => privacy.SetRecordingDetection(settings.Current.DetectScreenRecording);
            services.GetRequiredService<LiveActivityHost>().Start();

            var updates = services.GetRequiredService<UpdateService>();
            services.GetRequiredService<Settings.AboutViewModel>().CheckUpdates = () => updates.CheckAsync(notify: false);
            updates.Start();

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

    private void OpenSettings(string? section)
    {
        if (!_modulesReady)
        {
            // Pages are built from initialized modules; the tray click before that is ignored.
            return;
        }

        // Created on first use only: modules must be initialized before their settings pages exist.
        _settingsCreated = true;
        _host.Services.GetRequiredService<Settings.SettingsWindow>().ShowSection(section);
    }

    private void HandleArguments(CommandLine commandLine)
    {
        _logger.LogDebug("Arguments: {CommandLine}", commandLine);
        if (commandLine.OpenSettings && _island is not null)
        {
            OpenSettings(null);
        }

#if DEBUG
        if (commandLine.Showcase)
        {
            new Debug.ShowcaseWindow().Show();
        }
#endif
        if (commandLine.Show)
        {
            var stateMachine = _host.Services.GetRequiredService<IslandStateMachine>();
            if (!IslandMetrics.IsExpanded(stateMachine.Mode))
            {
                stateMachine.HotkeyPressed();
            }
        }
    }
}
