using System.Globalization;
using System.Resources;
using DynamicIsland.UI.Localization;

namespace DynamicIsland.Tests.UI;

public sealed class LocalizationConcurrencyTests
{
    [Fact]
    public async Task Registration_during_lookup_preserves_snapshot_and_exposes_new_resource_on_next_lookup()
    {
        // An isolated instance avoids changing the application singleton/culture.
        var localization = (Loc)Activator.CreateInstance(typeof(Loc), nonPublic: true)!;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        localization.Register(new BlockingResource(entered, release));
        var pending = Task.Run(() => localization["Concurrency.Probe"], TestContext.Current.CancellationToken);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "Lookup did not enter the resource manager");
            localization.Register(new ProbeResource());
        }
        finally { release.Set(); }
        Assert.Equal("Concurrency.Probe", await pending);
        Assert.Equal("registered", localization["Concurrency.Probe"]);
    }

    private sealed class BlockingResource(ManualResetEventSlim entered, ManualResetEventSlim release) : ResourceManager
    {
        public override string? GetString(string name, CultureInfo? culture)
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Concurrent registration did not complete");
            return null;
        }
    }

    private sealed class ProbeResource : ResourceManager
    {
        public override string? GetString(string name, CultureInfo? culture) => name == "Concurrency.Probe" ? "registered" : null;
    }
}
