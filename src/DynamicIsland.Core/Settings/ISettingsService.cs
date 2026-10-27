namespace DynamicIsland.Core.Settings;

/// <summary>Loads, migrates and saves <see cref="AppSettings"/>.</summary>
public interface ISettingsService
{
    /// <summary>The live settings instance. Valid after <see cref="LoadAsync"/>.</summary>
    AppSettings Current { get; }

    /// <summary>True when no settings file existed (first start after installation).</summary>
    bool IsFirstRun { get; }

    /// <summary>Raised in the caller's synchronization context after <see cref="SaveAsync"/> succeeds.</summary>
    event EventHandler? Changed;

    /// <summary>Loads settings from disk, applying migrations and clamping invalid values.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists <see cref="Current"/> and raises <see cref="Changed"/>.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
