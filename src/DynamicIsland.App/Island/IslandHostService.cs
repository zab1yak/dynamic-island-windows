using DynamicIsland.Core.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace DynamicIsland.App.Island;

/// <summary><see cref="IIslandHost"/> backed by the island window (resolved lazily to avoid a DI cycle).</summary>
internal sealed class IslandHostService(IServiceProvider services) : IIslandHost
{
    /// <summary>Raised when a module asks for the settings window.</summary>
    public event EventHandler<string?>? SettingsRequested;

    /// <inheritdoc />
    public void RequestKeyboard() => services.GetRequiredService<IslandWindow>().BeginTextInput();

    /// <inheritdoc />
    public void ReleaseKeyboard() => services.GetRequiredService<IslandWindow>().EndTextInput();

    /// <inheritdoc />
    public void OpenSettings(string? moduleId = null) => SettingsRequested?.Invoke(this, moduleId);
}
