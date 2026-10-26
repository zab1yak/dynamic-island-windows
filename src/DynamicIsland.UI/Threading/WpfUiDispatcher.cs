using System.Windows.Threading;
using DynamicIsland.Core.Threading;

namespace DynamicIsland.UI.Threading;

/// <summary><see cref="IUiDispatcher"/> over the WPF <see cref="Dispatcher"/> of the UI thread.</summary>
public sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    /// <inheritdoc />
    public bool CheckAccess() => dispatcher.CheckAccess();

    /// <inheritdoc />
    public void Post(Action action) => dispatcher.BeginInvoke(action, DispatcherPriority.Background);

    /// <inheritdoc />
    public Task InvokeAsync(Action action) => dispatcher.InvokeAsync(action).Task;
}
