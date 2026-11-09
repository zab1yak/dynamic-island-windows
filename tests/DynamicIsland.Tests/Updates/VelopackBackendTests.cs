using System.Security.Cryptography;
using DynamicIsland.App.Services;
using Velopack;
using Velopack.Exceptions;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

namespace DynamicIsland.Tests.Updates;

public sealed class VelopackBackendTests
{
    [Fact]
    public async Task Real_Velopack_manager_selects_semantically_newer_version_and_preserves_installed_channel()
    {
        using var folder = new TempFolder();
        var source = new FeedSource(Asset("0.3.9"), Asset("0.3.10"));
        var locator = Locator(folder.Path, "win-arm64");
        var manager = new VelopackUpdateBackend.VerifyingUpdateManager(source, locator);
        var result = await manager.CheckForUpdatesAsync();
        Assert.NotNull(result);
        Assert.Equal("0.3.10", result.TargetFullRelease.Version.ToString());
        Assert.Equal("win-arm64", source.RequestedChannel);
        Assert.Equal("0.3.9", manager.CurrentVersion?.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Already_cached_package_is_reverified_before_ready_and_corrupt_cache_does_not_destroy_installed_files(bool corrupt)
    {
        using var folder = new TempFolder();
        var asset = Asset("0.3.10");
        var source = new FeedSource(asset);
        var manager = new VelopackUpdateBackend.VerifyingUpdateManager(source, Locator(folder.Path, "win"));
        var backend = new VelopackUpdateBackend(manager);
        var path = Path.Combine(folder.Path, asset.FileName);
        var workingPath = Path.Combine(folder.Path, "working-installed-version.txt");
        await File.WriteAllTextAsync(workingPath, "0.3.9 untouched", TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(path, corrupt ? [9, 9, 9, 9] : PackageBytes, TestContext.Current.CancellationToken);
        var checkedCandidate = new UpdateCandidate("0.3.10", UpdateCoordinator.ReleasesUrl, new UpdateInfo(asset, false));
        if (corrupt)
        {
            var error = await Assert.ThrowsAsync<UpdateBackendException>(() => backend.DownloadAsync(checkedCandidate, _ => { }, TestContext.Current.CancellationToken));
            Assert.Equal(UpdateFailureReason.Integrity, error.Reason);
            Assert.IsType<ChecksumFailedException>(error.InnerException);
            Assert.False(File.Exists(path));
        }
        else await backend.DownloadAsync(checkedCandidate, _ => { }, TestContext.Current.CancellationToken);
        Assert.Equal(0, source.Downloads);
        Assert.Equal("0.3.9 untouched", await File.ReadAllTextAsync(workingPath, TestContext.Current.CancellationToken));
    }

    private static readonly byte[] PackageBytes = [1, 2, 3, 4];
    private static VelopackAsset Asset(string version) => new()
    {
        PackageId = "DynamicIslandForWindows", Version = SemanticVersion.Parse(version), Type = VelopackAssetType.Full,
        FileName = $"DynamicIslandForWindows-{version}-full.nupkg", Size = PackageBytes.Length,
        SHA1 = "12DADA1FFF4D4787ADE3333147202C3B443E376F", SHA256 = Convert.ToHexString(SHA256.HashData(PackageBytes)),
    };
    private static TestVelopackLocator Locator(string root, string channel) => new("DynamicIslandForWindows", "0.3.9", root,
        root, root, Path.Combine(root, "Update.exe"), channel);

    private sealed class FeedSource(params VelopackAsset[] assets) : IUpdateSource
    {
        public string? RequestedChannel { get; private set; }
        public int Downloads { get; private set; }
        public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
        { RequestedChannel = channel; return Task.FromResult(new VelopackAssetFeed { Assets = assets }); }
        public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken)
        { Downloads++; throw new InvalidOperationException("Cached-file verification must not issue a download."); }
    }
}
