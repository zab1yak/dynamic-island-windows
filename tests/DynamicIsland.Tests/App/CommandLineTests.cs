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
        Assert.Null(cl.RestartFromPid);
    }

    [Theory]
    [InlineData("--restart=4242", 4242)]
    [InlineData("--RESTART=7", 7)]
    [InlineData("--restart=", null)]
    [InlineData("--restart=-1", null)]
    [InlineData("--restart=abc", null)]
    public void Restart_carries_the_previous_process_id(string arg, int? expected)
    {
        Assert.Equal(expected, CommandLine.Parse([arg]).RestartFromPid);
    }

    [Fact]
    public void Empty_arguments_mean_no_switches()
    {
        Assert.Equal(new CommandLine(false, false, false, null), CommandLine.Parse([]));
    }
}

public sealed class CommandLineRobustnessTests
{
    [Fact]
    public void Null_arguments_are_ignored()
    {
        var cl = DynamicIsland.App.Hosting.CommandLine.Parse([null!, "--show", null!]);
        Assert.True(cl.Show);
    }
}
