using DynamicIsland.App.Hosting;

namespace DynamicIsland.Tests.App;

public sealed class CommandLineTests
{
    [Fact]
    public void Parses_known_switches_case_insensitively_and_ignores_unknown()
    {
        var cl = CommandLine.Parse(["--BACKGROUND", "--settings", "--whatever"]);

        Assert.True(cl.Background);
        Assert.True(cl.OpenSettings);
        Assert.False(cl.Show);
        Assert.False(cl.Restart);
    }

    [Fact]
    public void Empty_arguments_mean_no_switches()
    {
        Assert.Equal(new CommandLine(false, false, false, false), CommandLine.Parse([]));
    }
}
