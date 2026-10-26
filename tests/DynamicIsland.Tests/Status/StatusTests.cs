using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Status;
using Microsoft.Extensions.Time.Testing;

namespace DynamicIsland.Tests.Status;

public sealed class BatteryStatusTests
{
    [Theory]
    [InlineData(false, 50, false, false, BatteryAppearance.Hidden)]
    [InlineData(true, 50, false, false, BatteryAppearance.Normal)]
    [InlineData(true, 19, false, false, BatteryAppearance.Low)]
    [InlineData(true, 20, false, false, BatteryAppearance.Normal)]
    [InlineData(true, 10, true, false, BatteryAppearance.Charging)]
    [InlineData(true, 10, false, true, BatteryAppearance.Saver)]
    [InlineData(true, 80, true, true, BatteryAppearance.Charging)]
    public void Appearance_follows_iOS_rules(bool present, int percent, bool charging, bool saver, BatteryAppearance expected)
    {
        Assert.Equal(expected, new BatteryStatus(present, percent, charging, saver).Appearance);
    }
}

public sealed class ConsentStoreParserTests
{
    [Fact]
    public void Only_started_and_not_stopped_entries_are_in_use()
    {
        var apps = ConsentStoreParser.AppsInUse(
        [
            new ConsentEntry("C:#Program Files#Zoom#bin#Zoom.exe", true, 133000000000000000, 0),
            new ConsentEntry("C:#Tools#old.exe", true, 133000000000000000, 133000000000000001),
            new ConsentEntry("Microsoft.WindowsCamera_8wekyb3d8bbwe", false, 133000000000000000, 0),
            new ConsentEntry("Never.Used_abc", false, 0, 0),
        ]);

        Assert.Equal(["WindowsCamera", "Zoom"], apps);
    }

    [Theory]
    [InlineData("C:#Users#me#AppData#Local#Discord#app-1.0#Discord.exe", true, "Discord")]
    [InlineData("C:#tool", true, "tool")]
    [InlineData("Microsoft.SkypeApp_kzf8qxf38zg5c", false, "SkypeApp")]
    [InlineData("Simple", false, "Simple")]
    [InlineData("Trailing.", false, "Trailing.")]
    public void Display_names_are_readable(string key, bool nonPackaged, string expected)
    {
        Assert.Equal(expected, ConsentStoreParser.DisplayName(new ConsentEntry(key, nonPackaged, 1, 0)));
    }

    [Fact]
    public void Desktop_app_counts_only_while_its_process_runs()
    {
        var entries = new[]
        {
            new ConsentEntry("C:#Program Files#obs-studio#bin#64bit#obs64.exe", true, 1, 0),
            new ConsentEntry("Microsoft.WindowsCamera_8wekyb3d8bbwe", false, 1, 0),
        };
        string? asked = null;

        var apps = ConsentStoreParser.AppsInUse(entries, path =>
        {
            asked = path;
            return false;
        });

        Assert.Equal(["WindowsCamera"], apps);
        Assert.Equal(@"C:\Program Files\obs-studio\bin\64bit\obs64.exe", asked);
    }

    [Fact]
    public void Duplicates_are_merged_case_insensitively()
    {
        var apps = ConsentStoreParser.AppsInUse(
        [
            new ConsentEntry("C:#a#chrome.exe", true, 1, 0),
            new ConsentEntry("D:#b#Chrome.exe", true, 1, 0),
        ]);

        Assert.Single(apps);
    }
}

public sealed class LiveActivityArbiterTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero));

    private LiveActivity Activity(string module, int priority, bool alert = false, int startedSecondsAgo = 0) =>
        new(module, ActivityKind.Media, priority, new object(), _time.GetUtcNow().AddSeconds(-startedSecondsAgo), alert);

    [Fact]
    public void Highest_priority_wins_and_the_next_becomes_secondary()
    {
        var arbiter = new LiveActivityArbiter(_time);
        var media = Activity("media", LiveActivityPriority.MediaPlaying);
        var timer = Activity("pomodoro", LiveActivityPriority.TimerRunning);

        Assert.True(arbiter.Update([timer, media, null]));
        Assert.Equal("media", arbiter.Current.Primary?.ModuleId);
        Assert.Equal("pomodoro", arbiter.Current.Secondary?.ModuleId);
    }

    [Fact]
    public void Alert_beats_priority_immediately()
    {
        var arbiter = new LiveActivityArbiter(_time);
        arbiter.Update([Activity("media", 70)]);

        arbiter.Update([Activity("media", 70), Activity("pomodoro", 10, alert: true)]);

        Assert.Equal("pomodoro", arbiter.Current.Primary?.ModuleId);
    }

    [Fact]
    public void Switching_is_held_back_by_hysteresis_then_happens()
    {
        var arbiter = new LiveActivityArbiter(_time);
        arbiter.Update([Activity("media", 70)]);
        _time.Advance(TimeSpan.FromSeconds(0.5));

        arbiter.Update([Activity("media", 70), Activity("pomodoro", 90)]);
        Assert.Equal("media", arbiter.Current.Primary?.ModuleId);
        Assert.True(arbiter.IsSwitchPending);

        _time.Advance(LiveActivityArbiter.Hysteresis);
        arbiter.Update([Activity("media", 70), Activity("pomodoro", 90)]);
        Assert.Equal("pomodoro", arbiter.Current.Primary?.ModuleId);
        Assert.False(arbiter.IsSwitchPending);
    }

    [Fact]
    public void Primary_that_disappears_is_replaced_at_once()
    {
        var arbiter = new LiveActivityArbiter(_time);
        arbiter.Update([Activity("media", 70), Activity("pomodoro", 60)]);

        arbiter.Update([Activity("pomodoro", 60)]);

        Assert.Equal("pomodoro", arbiter.Current.Primary?.ModuleId);
        Assert.Null(arbiter.Current.Secondary);
    }

    [Fact]
    public void Nothing_active_gives_empty_result()
    {
        var arbiter = new LiveActivityArbiter(_time);
        arbiter.Update([Activity("media", 70)]);
        Assert.True(arbiter.Update([]));
        Assert.Equal(ArbiterResult.Empty, arbiter.Current);
    }
}
