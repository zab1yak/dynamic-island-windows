using DynamicIsland.App.Services;

namespace DynamicIsland.Tests.App;

public sealed class GlobalExceptionHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Up_to_the_limit_errors_in_the_window_are_tolerated()
    {
        var recent = new Queue<DateTimeOffset>();
        for (var i = 0; i < GlobalExceptionHandler.BurstLimit; i++)
        {
            Assert.False(GlobalExceptionHandler.RegisterAndCheckBurst(recent, T0.AddSeconds(i)));
        }
    }

    [Fact]
    public void One_more_error_inside_the_window_trips_the_limit()
    {
        var recent = new Queue<DateTimeOffset>();
        for (var i = 0; i < GlobalExceptionHandler.BurstLimit; i++)
        {
            GlobalExceptionHandler.RegisterAndCheckBurst(recent, T0.AddSeconds(i));
        }

        Assert.True(GlobalExceptionHandler.RegisterAndCheckBurst(recent, T0.AddSeconds(6)));
    }

    [Fact]
    public void Old_errors_fall_out_of_the_window()
    {
        var recent = new Queue<DateTimeOffset>();
        for (var i = 0; i < GlobalExceptionHandler.BurstLimit; i++)
        {
            GlobalExceptionHandler.RegisterAndCheckBurst(recent, T0);
        }

        Assert.False(GlobalExceptionHandler.RegisterAndCheckBurst(recent, T0 + GlobalExceptionHandler.BurstWindow + TimeSpan.FromSeconds(1)));
    }
}
