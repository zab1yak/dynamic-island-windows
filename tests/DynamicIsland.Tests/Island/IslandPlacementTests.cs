using DynamicIsland.Core.Island;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Tests.Island;

public sealed class IslandPlacementTests
{
    [Theory]
    [InlineData(-1920, 1920, 664, IslandPosition.Left, 12, -1908)]
    [InlineData(-1920, 1920, 664, IslandPosition.Center, 12, -1292)]
    [InlineData(-1920, 1920, 664, IslandPosition.Right, 12, -676)]
    [InlineData(1920, 2560, 996, IslandPosition.Right, 18, 3466)]
    [InlineData(0, 600, 580, IslandPosition.Left, 40, 10)]
    [InlineData(0, 600, 580, IslandPosition.Right, 40, 10)]
    [InlineData(0, 600, 700, IslandPosition.Center, 40, 0)]
    public void Placement_uses_selected_monitor_and_clamps_inset(int left, int monitorWidth, int hostWidth, IslandPosition position, int inset, int expected)
    {
        Assert.Equal(expected, IslandPlacement.Left(left, monitorWidth, hostWidth, position, inset));
    }
}
