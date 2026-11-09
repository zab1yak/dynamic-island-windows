using System.Windows;
using System.Windows.Threading;

namespace DynamicIsland.Tests.UI;

/// <summary>One WPF application/STA for resource-dependent layout tests, regardless of test order.</summary>
internal static class LayoutTestApplication
{
    private static readonly Lazy<Dispatcher> Host = new(CreateHost);

    internal static void Run(Action action) => Host.Value.Invoke(action, DispatcherPriority.Send,
        CancellationToken.None, TimeSpan.FromSeconds(90));

    private static Dispatcher CreateHost()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
                    new Uri("/DynamicIsland.UI;component/Themes/Theme.xaml", UriKind.Relative)));
                ready.SetResult(app.Dispatcher);
                app.Run();
            }
            catch (Exception exception) { ready.TrySetException(exception); }
        }) { IsBackground = true, Name = "WPF layout test application" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
    }
}
