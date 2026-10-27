namespace DynamicIsland.Core.Platform;

/// <summary>Outcome of setting up package identity.</summary>
public enum IdentitySetupResult
{
    /// <summary>The package is registered; identity applies after a restart.</summary>
    Registered,

    /// <summary>The user declined the administrator prompt.</summary>
    Cancelled,

    /// <summary>The identity package is missing from the install folder.</summary>
    PackageMissing,

    /// <summary>Windows refused the package (see the log).</summary>
    Failed,
}

/// <summary>
/// Package identity for the unpackaged app: an identity-only MSIX registered with the install folder
/// as its external location. Windows grants the notification history only to apps with identity.
/// Nothing happens until the user presses «Включить».
/// </summary>
public interface IPackageIdentity
{
    /// <summary>Whether this process runs with package identity.</summary>
    bool HasIdentity { get; }

    /// <summary>Whether the identity package is registered for the current user (identity after restart).</summary>
    bool IsRegistered { get; }

    /// <summary>
    /// Trusts the shipped public certificate (for the current user; if Windows also needs it for the
    /// machine, one administrator prompt) and registers the identity package.
    /// </summary>
    Task<IdentitySetupResult> SetUpAsync(CancellationToken cancellationToken = default);

    /// <summary>Unregisters the identity package and removes the certificate from the current user.</summary>
    Task RemoveAsync();
}

/// <summary>Exit and restart of the application, for pages inside the island.</summary>
public interface IAppControl
{
    /// <summary>Shuts the application down gracefully.</summary>
    void Quit();

    /// <summary>Starts a fresh process and exits.</summary>
    void Restart();
}
