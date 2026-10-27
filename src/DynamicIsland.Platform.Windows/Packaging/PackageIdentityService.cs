using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DynamicIsland.Core.Platform;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.Platform.Windows.Packaging;

/// <summary>
/// Registers the identity-only package (<c>DynamicIslandIdentity.msix</c>, shipped next to the exe) with
/// the install folder as its external location (D-038). The package is signed with a certificate whose
/// private key no longer exists; only the public certificate is trusted, so nothing else can be signed
/// with it.
/// </summary>
public sealed partial class PackageIdentityService(ILogger<PackageIdentityService> logger) : IPackageIdentity
{
    /// <summary>Package name from packaging/identity/AppxManifest.xml.</summary>
    public const string PackageName = "DynamicIslandForWindows.Identity";

    /// <summary>Package file next to the exe.</summary>
    public const string PackageFile = "DynamicIslandIdentity.msix";

    /// <summary>Public certificate next to the exe.</summary>
    public const string CertificateFile = "DynamicIslandIdentity.cer";

    /// <summary>SHA-256 of the shipped public certificate (made by tools/make_identity_package.ps1).</summary>
    public const string CertificateSha256 = "F0E2BD14E3A3649E37EECDF13D308ACD6EE8DF6517C1D29440E98DF83DABEC1B";

    /// <summary>SHA-256 of the shipped signed identity package.</summary>
    public const string PackageSha256 = "C53E538724253D55B1D65DC55711A1A5DD227CD4B0E19BA97327DF90D103F20C";

    private const int AppModelErrorNoPackage = 15700;
    private const int ErrorCancelled = 1223;

    /// <inheritdoc />
    public bool HasIdentity
    {
        get
        {
            var length = 0u;
            return GetCurrentPackageFullName(ref length, IntPtr.Zero) != AppModelErrorNoPackage;
        }
    }

    /// <inheritdoc />
    public bool IsRegistered => FindPackage() is not null;

    /// <inheritdoc />
    public async Task<IdentitySetupResult> SetUpAsync(CancellationToken cancellationToken = default)
    {
        var folder = AppContext.BaseDirectory;
        var package = Path.Combine(folder, PackageFile);
        var certificate = Path.Combine(folder, CertificateFile);
        if (!File.Exists(package) || !File.Exists(certificate))
        {
            logger.LogWarning("Identity package files are missing in the install folder");
            return IdentitySetupResult.PackageMissing;
        }

        // The install folder is writable by any program running as the user. Lock both files against
        // changes (read sharing only) and compare them with the pinned hashes before trusting anything;
        // the handles stay open until certutil and AddPackage have read the very same bytes.
        using var certificateLock = new FileStream(certificate, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var packageLock = new FileStream(package, FileMode.Open, FileAccess.Read, FileShare.Read);
        var certificateBytes = new byte[certificateLock.Length];
        certificateLock.ReadExactly(certificateBytes);
        var packageHash = await SHA256.HashDataAsync(packageLock, cancellationToken);
        if (!MatchesPinned(certificateBytes, packageHash))
        {
            logger.LogWarning("Identity package files do not match the pinned hashes; refusing to trust them");
            return IdentitySetupResult.Failed;
        }

        using var cert = X509CertificateLoader.LoadCertificate(certificateBytes);
        var hresult = await RegisterAsync(package, folder, cancellationToken);
        if (hresult is not null && IsUntrustedCertificate(hresult.Value))
        {
            // Trust for the current user first (no prompt) — only once Windows says it is needed.
            AddToStore(cert, StoreLocation.CurrentUser);
            hresult = await RegisterAsync(package, folder, cancellationToken);
        }

        if (hresult is not null && IsUntrustedCertificate(hresult.Value) && !IsInStore(cert, StoreLocation.LocalMachine))
        {
            // Windows checks sideloaded packages against the machine store: one administrator prompt.
            logger.LogInformation("Identity certificate needs machine trust; asking for administrator rights");
            if (!await TrustForMachineAsync(certificate, cancellationToken))
            {
                return IdentitySetupResult.Cancelled;
            }

            hresult = await RegisterAsync(package, folder, cancellationToken);
        }

        if (hresult is null)
        {
            logger.LogInformation("Identity package registered; identity applies after a restart");
            return IdentitySetupResult.Registered;
        }

        logger.LogWarning("Identity package registration failed: 0x{HResult:X8}", hresult.Value);
        return IdentitySetupResult.Failed;
    }

    /// <inheritdoc />
    public async Task RemoveAsync()
    {
        try
        {
            if (FindPackage() is { } fullName)
            {
                await new global::Windows.Management.Deployment.PackageManager().RemovePackageAsync(fullName).AsTask();
            }

            // Remove exactly the pinned certificate, never whatever file now sits in the install folder.
            // The machine-store copy (if the user granted it) needs administrator rights; see README.
            using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            foreach (var cert in store.Certificates)
            {
                if (string.Equals(cert.GetCertHashString(HashAlgorithmName.SHA256), CertificateSha256, StringComparison.OrdinalIgnoreCase))
                {
                    store.Remove(cert);
                }

                cert.Dispose();
            }
        }
        catch (Exception ex) when (ex is COMException or CryptographicException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Removing the identity package failed");
        }
    }

    /// <summary>
    /// Certificate-chain errors only (CERT_E_UNTRUSTEDROOT, CERT_E_CHAINING, TRUST_E_NOSIGNATURE). Generic
    /// AppX policy errors — for example a tampered package — never lead to an elevation prompt.
    /// </summary>
    internal static bool IsUntrustedCertificate(int hresult) => unchecked((uint)hresult) is 0x800B0109 or 0x800B010A or 0x800B0100;

    /// <summary>Whether the certificate bytes and the package hash are exactly the shipped ones.</summary>
    internal static bool MatchesPinned(byte[] certificate, byte[] packageHash) =>
        string.Equals(Convert.ToHexString(SHA256.HashData(certificate)), CertificateSha256, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Convert.ToHexString(packageHash), PackageSha256, StringComparison.OrdinalIgnoreCase);

    private static string? FindPackage()
    {
        try
        {
            return new global::Windows.Management.Deployment.PackageManager()
                .FindPackagesForUser(string.Empty)
                .FirstOrDefault(p => p.Id.Name == PackageName)?.Id.FullName;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<int?> RegisterAsync(string package, string folder, CancellationToken cancellationToken)
    {
        try
        {
            var options = new global::Windows.Management.Deployment.AddPackageOptions
            {
                ExternalLocationUri = new Uri(folder),
                ForceUpdateFromAnyVersion = true,
            };
            var result = await new global::Windows.Management.Deployment.PackageManager()
                .AddPackageByUriAsync(new Uri(package), options).AsTask(cancellationToken);
            if (result.ExtendedErrorCode is { } error)
            {
                logger.LogDebug("AddPackage: {Error}", result.ErrorText);
                return error.HResult;
            }

            return null;
        }
        catch (COMException ex)
        {
            return ex.HResult;
        }
    }

    private static void AddToStore(X509Certificate2 cert, StoreLocation location)
    {
        using var store = new X509Store(StoreName.TrustedPeople, location);
        store.Open(OpenFlags.ReadWrite);
        store.Add(cert);
    }

    private static bool IsInStore(X509Certificate2 cert, StoreLocation location)
    {
        using var store = new X509Store(StoreName.TrustedPeople, location);
        store.Open(OpenFlags.ReadOnly);
        return store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, validOnly: false).Count > 0;
    }

    private async Task<bool> TrustForMachineAsync(string certificate, CancellationToken cancellationToken)
    {
        var certutil = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "certutil.exe");
        try
        {
            using var process = Process.Start(new ProcessStartInfo(certutil)
            {
                // Fixed tool, fixed store; the only variable is our own certificate path, quoted.
                Arguments = $"-addstore TrustedPeople \"{certificate}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            logger.LogInformation("Administrator prompt declined");
            return false;
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);
}
