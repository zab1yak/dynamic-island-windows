using DynamicIsland.Core.Threading;

namespace DynamicIsland.Tests;

/// <summary>Runs dispatched work synchronously on the calling thread.</summary>
internal sealed class InlineDispatcher : IUiDispatcher
{
    public bool CheckAccess() => true;

    public void Post(Action action) => action();

    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
