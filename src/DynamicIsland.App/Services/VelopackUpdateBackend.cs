using System.Reflection;
using System.Runtime.InteropServices;
using Velopack;
using Velopack.Exceptions;
using Velopack.Logging;
using Velopack.Locators;
using Velopack.Sources;

namespace DynamicIsland.App.Services;

/// <summary>Uses the installed Velopack locator/channel and its existing GitHub source and verifier.</summary>
internal sealed class VelopackUpdateBackend : IUpdateBackend
{
    internal const string RepositoryUrl = "https://github.com/zab1yak/dynamic-island-windows";
    private readonly ProjectGithubSource _source = new();
    private readonly VerifyingUpdateManager _manager;

    public VelopackUpdateBackend() => _manager = new(_source);
    internal VelopackUpdateBackend(VerifyingUpdateManager manager) => _manager = manager;
    public bool IsInstalled => _manager.IsInstalled && !_manager.IsPortable &&
        RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;
    public string InstalledVersion => _manager.CurrentVersion?.ToString()
        ?? typeof(VelopackUpdateBackend).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(VelopackUpdateBackend).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public async Task<UpdateCandidate?> CheckAsync()
    {
        var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        if (update is null) return null;
        if (update.TargetFullRelease.Version.IsPrerelease)
            throw new UpdateBackendException(UpdateFailureReason.InvalidFeed, "Stable channel returned a prerelease package.");
        return new(update.TargetFullRelease.Version.ToString(), _source.GetReleaseUrl(update.TargetFullRelease), update);
    }

    public async Task DownloadAsync(UpdateCandidate candidate, Action<int> progress, CancellationToken cancellationToken)
    {
        var update = candidate.Handle as UpdateInfo ?? throw new InvalidOperationException("No checked update is available.");
        try
        {
            await _manager.DownloadUpdatesAsync(update, progress, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // Velopack skips downloading an existing cache file; explicitly use its
            // same verifier before reporting Ready even in that cached-file case.
            await _manager.VerifyTargetAsync(update.TargetFullRelease).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (ChecksumFailedException exception)
        {
            try { _manager.RemoveRejectedTarget(update.TargetFullRelease); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { /* Preserve the original integrity failure. */ }
            throw new UpdateBackendException(UpdateFailureReason.Integrity, "Downloaded update failed integrity verification.", exception);
        }
    }

    public void PrepareForExit(UpdateCandidate candidate)
    {
        var update = candidate.Handle as UpdateInfo ?? throw new InvalidOperationException("No downloaded update is available.");
        // This helper waits only 60 seconds, so the application calls it only
        // after graceful saves/drain and immediately before actual process exit.
        _manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: false);
    }

    internal sealed class VerifyingUpdateManager(IUpdateSource source, IVelopackLocator? locator = null) : UpdateManager(source, locator: locator)
    {
        public Task VerifyTargetAsync(VelopackAsset asset) => VerifyPackageChecksumAsync(asset);
        public void RemoveRejectedTarget(VelopackAsset asset)
        {
            // Only a newer rejected cache package is removed, never installed application files.
            if (CurrentVersion is null || asset.Version <= CurrentVersion) return;
            var path = Path.Combine(Locator.PackagesDir!, asset.FileName);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

/// <summary>Validates Velopack's parsed feed; an empty/malformed feed is never reported as Latest.</summary>
internal sealed class ProjectGithubSource : GithubSource
{
    private const string DownloadPrefix = "/zab1yak/dynamic-island-windows/releases/download/";
    public ProjectGithubSource() : base(VelopackUpdateBackend.RepositoryUrl, accessToken: null, prerelease: false) { }

    public override async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel,
        Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
    {
        var feed = await base.GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease).ConfigureAwait(false);
        var assets = feed.Assets?.Where(asset => asset.PackageId == appId && (asset.Version?.IsPrerelease ?? false) == false).ToArray() ?? [];
        ValidateAssets(assets, appId);
        return new VelopackAssetFeed { Assets = assets };
    }

    internal static void ValidateAssets(IReadOnlyList<VelopackAsset> assets, string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId) || !assets.Any(asset => asset.Type == VelopackAssetType.Full))
            throw new UpdateBackendException(UpdateFailureReason.InvalidFeed, "The channel has no compatible full release.");
        foreach (var asset in assets)
        {
            if (asset.Version is null || asset.PackageId != appId || asset.Size <= 0 ||
                string.IsNullOrWhiteSpace(asset.FileName) || asset.FileName.IndexOfAny(['/', '\\']) >= 0 ||
                Path.GetFileName(asset.FileName) != asset.FileName ||
                !ValidHash(asset.SHA1, 40) || (!string.IsNullOrEmpty(asset.SHA256) && !ValidHash(asset.SHA256, 64)))
                throw new UpdateBackendException(UpdateFailureReason.InvalidFeed, "The channel contains an invalid release entry.");
        }
    }

    private static bool ValidHash(string? hash, int length) => hash is not null && hash.Length == length && hash.All(char.IsAsciiHexDigit);

    internal string GetReleaseUrl(VelopackAsset asset)
    {
        if (asset is not GitBaseAsset checkedAsset)
            throw new UpdateBackendException(UpdateFailureReason.InvalidFeed, "Update is not associated with a checked GitHub release.");
        return ReleaseUrlFromAsset(GetAssetUrlFromName(checkedAsset.Release, asset.FileName));
    }

    internal static string ReleaseUrlFromAsset(string downloadUrl)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || uri.UserInfo.Length != 0 ||
            !uri.IsDefaultPort || !uri.AbsolutePath.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase))
            throw new UpdateBackendException(UpdateFailureReason.InvalidFeed, "Unexpected release asset origin.");
        var suffix = uri.AbsolutePath[DownloadPrefix.Length..];
        var separator = suffix.IndexOf('/');
        if (separator <= 0 || separator == suffix.Length - 1)
            throw new UpdateBackendException(UpdateFailureReason.InvalidFeed, "The checked release has no tag or asset.");
        var tag = Uri.UnescapeDataString(suffix[..separator]);
        if (tag is "." or ".." || tag.Contains('/') || tag.Contains('\\'))
            throw new UpdateBackendException(UpdateFailureReason.InvalidFeed, "Invalid release tag.");
        return VelopackUpdateBackend.RepositoryUrl + "/releases/tag/" + Uri.EscapeDataString(tag);
    }
}
