using DynamicIsland.Core.Island;
using DynamicIsland.Modules.Tools.Services;

namespace DynamicIsland.Tests.Tools;

public sealed class CpuLoadTests
{
    [Fact]
    public void Percent_IsBusyShareOfKernelPlusUser()
    {
        // Kernel time includes idle: 100 kernel (60 idle) + 100 user = 200 total, 140 busy.
        Assert.Equal(70, CpuLoad.Percent(0, 0, 0, 60, 100, 100), 3);
    }

    [Fact]
    public void Percent_WithoutElapsedTime_IsZero() => Assert.Equal(0, CpuLoad.Percent(5, 5, 5, 5, 5, 5));

    [Fact]
    public void Percent_FullyIdle_IsZero() => Assert.Equal(0, CpuLoad.Percent(0, 0, 0, 100, 100, 0), 3);

    [Theory]
    [InlineData(0, LoadLevel.Low)]
    [InlineData(49.9, LoadLevel.Low)]
    [InlineData(50, LoadLevel.Medium)]
    [InlineData(79.9, LoadLevel.Medium)]
    [InlineData(80, LoadLevel.High)]
    [InlineData(100, LoadLevel.High)]
    public void Level_Bands(double percent, LoadLevel expected) => Assert.Equal(expected, CpuLoad.Level(percent));

    [Fact]
    public void Sample_FirstCallPrimes_SecondReturnsPercent()
    {
        var cpu = new CpuLoad();
        Assert.Null(cpu.Sample());
        var second = cpu.Sample();
        Assert.NotNull(second);
        Assert.InRange(second.Value, 0, 100);
    }
}

public sealed class IslandRenderStatsTests
{
    [Fact]
    public void Report_ComputesFpsAndRaisesChanged()
    {
        var stats = new IslandRenderStats();
        var raised = 0;
        stats.Changed += (_, _) => raised++;

        stats.Report(60, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(21));

        Assert.Equal(120, stats.LastAnimationFps!.Value, 3);
        Assert.Equal(21, stats.LongestFrameMs, 3);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Report_IgnoresTooShortRuns()
    {
        var stats = new IslandRenderStats();
        stats.Report(1, TimeSpan.FromMilliseconds(16), TimeSpan.FromMilliseconds(16));
        stats.Report(10, TimeSpan.Zero, TimeSpan.Zero);
        Assert.Null(stats.LastAnimationFps);
    }
}
