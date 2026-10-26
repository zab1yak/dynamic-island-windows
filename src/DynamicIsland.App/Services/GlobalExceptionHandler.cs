using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.UI.Localization;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App.Services;

/// <summary>
/// Last line of defence: logs every unhandled exception and keeps the island alive. If errors keep
/// coming (more than <see cref="BurstLimit"/> within <see cref="BurstWindow"/>) the app exits
/// instead of looping on a broken state.
/// </summary>
internal sealed class GlobalExceptionHandler(TrayIconService tray, TimeProvider time, ILogger<GlobalExceptionHandler> logger)
{
    internal const int BurstLimit = 5;
    internal static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(10);

    private readonly Queue<DateTimeOffset> _recent = new();
    private DateTimeOffset _lastNotification = DateTimeOffset.MinValue;
    private bool _shuttingDown;

    /// <summary>Hooks the dispatcher, AppDomain and TaskScheduler handlers.</summary>
    public void Attach(Application app)
    {
        app.DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", e.IsTerminating);
            if (e.IsTerminating)
            {
                Serilog.Log.CloseAndFlush();
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogError(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        logger.LogError(e.Exception, "Unhandled UI exception");
        e.Handled = true;
        if (_shuttingDown)
        {
            // The fatal dialog pumps messages; errors raised meanwhile are only logged.
            return;
        }

        var now = time.GetUtcNow();
        if (RegisterAndCheckBurst(_recent, now))
        {
            _shuttingDown = true;
            logger.LogCritical("Too many errors in a row; shutting down");
            MessageBox.Show(Loc.Instance.Format("Error.Fatal", AppPaths.Logs), Loc.Instance["App.Name"],
                MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown(2);
            return;
        }

        // One quiet notification per minute is enough; the details are in the log.
        if (now - _lastNotification > TimeSpan.FromMinutes(1))
        {
            _lastNotification = now;
            tray.Notify(Loc.Instance["Error.Title"], Loc.Instance["Error.Recovered"]);
        }
    }

    /// <summary>Records an error at <paramref name="now"/>; returns <c>true</c> when the burst limit is exceeded.</summary>
    internal static bool RegisterAndCheckBurst(Queue<DateTimeOffset> recent, DateTimeOffset now)
    {
        recent.Enqueue(now);
        while (recent.Count > 0 && now - recent.Peek() > BurstWindow)
        {
            recent.Dequeue();
        }

        return recent.Count > BurstLimit;
    }
}
