using DynamicIsland.App.Services;
using Velopack;

namespace DynamicIsland.Tests.Updates;

public sealed class UpdateFeedTests
{
    [Fact]
    public void Versions_use_Velopack_semantics_and_empty_or_invalid_feeds_are_not_latest()
    {
        Assert.True(SemanticVersion.Parse("0.3.10") > SemanticVersion.Parse("0.3.9"));
        Assert.Throws<UpdateBackendException>(() => ProjectGithubSource.ValidateAssets([], "DynamicIslandForWindows"));
        var asset = Asset();
        ProjectGithubSource.ValidateAssets([asset], "DynamicIslandForWindows");
        Assert.Throws<UpdateBackendException>(() => ProjectGithubSource.ValidateAssets([asset with { Size = 0 }], "DynamicIslandForWindows"));
        Assert.Throws<UpdateBackendException>(() => ProjectGithubSource.ValidateAssets([asset with { SHA256 = "bad" }], "DynamicIslandForWindows"));
        Assert.Throws<UpdateBackendException>(() => ProjectGithubSource.ValidateAssets([asset with { FileName = "../app.nupkg" }], "DynamicIslandForWindows"));
    }

    [Fact]
    public void Release_page_comes_from_checked_asset_tag_not_assumed_version_or_unknown_site()
    {
        Assert.Equal("https://github.com/zab1yak/dynamic-island-windows/releases/tag/custom-0.3.10",
            ProjectGithubSource.ReleaseUrlFromAsset("https://github.com/zab1yak/dynamic-island-windows/releases/download/custom-0.3.10/app.nupkg"));
        Assert.Throws<UpdateBackendException>(() => ProjectGithubSource.ReleaseUrlFromAsset("https://example.com/releases/download/v0.3.10/app.nupkg"));
        Assert.Throws<UpdateBackendException>(() => ProjectGithubSource.ReleaseUrlFromAsset("https://github.com/another/repo/releases/download/v0.3.10/app.nupkg"));
        Assert.Throws<UpdateBackendException>(() => ProjectGithubSource.ReleaseUrlFromAsset("http://github.com/zab1yak/dynamic-island-windows/releases/download/v0.3.10/app.nupkg"));
    }

    private static VelopackAsset Asset() => new()
    {
        PackageId = "DynamicIslandForWindows", Version = SemanticVersion.Parse("0.3.10"), Type = VelopackAssetType.Full,
        FileName = "DynamicIslandForWindows-0.3.10-full.nupkg", Size = 100, SHA1 = new string('a', 40), SHA256 = new string('b', 64),
    };
}
