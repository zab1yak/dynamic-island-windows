namespace DynamicIsland.Core.Threading;

/// <summary>Marshals work to the UI thread without Core depending on WPF.</summary>
public interface IUiDispatcher
{
    /// <summary>Whether the caller is already on the UI thread.</summary>
    bool CheckAccess();

    /// <summary>Queues <paramref name="action"/> on the UI thread at background priority.</summary>
    void Post(Action action);

    /// <summary>Runs <paramref name="action"/> on the UI thread and awaits it.</summary>
    Task InvokeAsync(Action action);
}
