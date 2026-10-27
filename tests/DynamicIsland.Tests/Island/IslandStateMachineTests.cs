using DynamicIsland.Core.Island;
using Microsoft.Extensions.Time.Testing;

namespace DynamicIsland.Tests.Island;

public sealed class IslandStateMachineTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly IslandStateMachine _sm;

    public IslandStateMachineTests()
    {
        _sm = new IslandStateMachine(_time, new InlineDispatcher());
    }

    public void Dispose() => _sm.Dispose();

    private void Advance(int ms) => _time.Advance(TimeSpan.FromMilliseconds(ms));

    [Fact]
    public void Hover_expands_after_the_delay_and_not_before()
    {
        _sm.PointerEntered();
        Advance(149);
        Assert.Equal(IslandMode.Compact, _sm.Mode);
        Advance(1);
        Assert.Equal(IslandMode.Expanded, _sm.Mode);
    }

    [Fact]
    public void Hover_after_fullscreen_ended_expands_again_without_leaving()
    {
        _sm.PointerEntered();
        _sm.SetFullscreen(true);
        Advance(500);
        _sm.SetFullscreen(false);

        _sm.PointerEntered();
        Advance(150);

        Assert.Equal(IslandMode.Expanded, _sm.Mode);
    }

    [Fact]
    public void Brief_hover_does_not_expand()
    {
        _sm.PointerEntered();
        Advance(100);
        _sm.PointerExited();
        Advance(1000);
        Assert.Equal(IslandMode.Compact, _sm.Mode);
    }

    [Fact]
    public void Leaving_collapses_after_the_delay_and_reentering_cancels_it()
    {
        _sm.PointerEntered();
        Advance(150);
        _sm.PointerExited();
        Advance(400);
        Assert.Equal(IslandMode.Expanded, _sm.Mode);
        _sm.PointerEntered();
        Advance(1000);
        Assert.Equal(IslandMode.Expanded, _sm.Mode);
        _sm.PointerExited();
        Advance(450);
        Assert.Equal(IslandMode.Compact, _sm.Mode);
    }

    [Fact]
    public void Click_pins_and_pinned_island_ignores_pointer_exit()
    {
        _sm.PointerEntered();
        Advance(150);
        _sm.Clicked();
        _sm.PointerExited();
        Advance(5000);
        Assert.Equal(IslandMode.Pinned, _sm.Mode);

        _sm.Clicked();
        Assert.Equal(IslandMode.Compact, _sm.Mode);
    }

    [Fact]
    public void Hotkey_toggles_pinned()
    {
        _sm.HotkeyPressed();
        Assert.Equal(IslandMode.Pinned, _sm.Mode);
        _sm.HotkeyPressed();
        Assert.Equal(IslandMode.Compact, _sm.Mode);
    }

    [Fact]
    public void Fullscreen_hides_and_restores_resting_mode()
    {
        _sm.SetLiveActivity(true);
        Assert.Equal(IslandMode.LiveActivity, _sm.Mode);
        _sm.SetFullscreen(true);
        Assert.Equal(IslandMode.Hidden, _sm.Mode);
        _sm.PointerEntered();
        Advance(500);
        Assert.Equal(IslandMode.Hidden, _sm.Mode);
        _sm.SetFullscreen(false);
        Assert.Equal(IslandMode.LiveActivity, _sm.Mode);
    }

    [Fact]
    public void Hotkey_shows_the_island_even_in_fullscreen()
    {
        _sm.SetFullscreen(true);
        _sm.HotkeyPressed();
        Assert.Equal(IslandMode.Pinned, _sm.Mode);
        _sm.HotkeyPressed();
        Assert.Equal(IslandMode.Hidden, _sm.Mode);
    }

    [Fact]
    public void Text_editing_keeps_the_island_open()
    {
        _sm.PointerEntered();
        Advance(150);
        _sm.TextEditStarted();
        _sm.PointerExited();
        Advance(5000);
        Assert.Equal(IslandMode.Expanded, _sm.Mode);
        _sm.TextEditEnded();
        Advance(450);
        Assert.Equal(IslandMode.Compact, _sm.Mode);
    }

    [Fact]
    public void Drag_near_shows_drop_target_and_drop_raises_event()
    {
        var dropped = false;
        _sm.DropCompleted += (_, _) => dropped = true;

        _sm.DragNear();
        Assert.Equal(IslandMode.DropTarget, _sm.Mode);
        _sm.Dropped();
        Assert.True(dropped);
        Assert.Equal(IslandMode.Expanded, _sm.Mode);
        Advance(450);
        Assert.Equal(IslandMode.Compact, _sm.Mode);
    }

    [Fact]
    public void Drag_is_ignored_when_drop_is_disabled()
    {
        _sm.DropEnabled = false;
        _sm.DragNear();
        Assert.Equal(IslandMode.Compact, _sm.Mode);
    }

    [Fact]
    public void Drag_leaving_returns_to_rest()
    {
        _sm.DragNear();
        _sm.DragLeft();
        Assert.Equal(IslandMode.Compact, _sm.Mode);
    }

    [Fact]
    public void ModeChanged_fires_once_per_change()
    {
        var changes = new List<IslandMode>();
        _sm.ModeChanged += (_, m) => changes.Add(m);

        _sm.SetLiveActivity(true);
        _sm.SetLiveActivity(true);
        _sm.SetLiveActivity(false);

        Assert.Equal([IslandMode.LiveActivity, IslandMode.Compact], changes);
    }
}

public sealed class IslandHoverStressTests
{
    [Fact]
    public void Rapid_enter_leave_never_leaves_the_island_stuck()
    {
        var time = new FakeTimeProvider();
        using var sm = new IslandStateMachine(time, new InlineDispatcher());
        var random = new Random(42);

        for (var i = 0; i < 500; i++)
        {
            if (random.Next(2) == 0)
            {
                sm.PointerEntered();
            }
            else
            {
                sm.PointerExited();
            }

            time.Advance(TimeSpan.FromMilliseconds(random.Next(0, 300)));
        }

        sm.PointerExited();
        time.Advance(TimeSpan.FromMilliseconds(450));
        Assert.Equal(IslandMode.Compact, sm.Mode);
        Assert.Equal(HoverPhase.Idle, sm.Phase);

        sm.PointerEntered();
        Assert.Equal(HoverPhase.ExpandPending, sm.Phase);
        time.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Equal(IslandMode.Expanded, sm.Mode);
        Assert.Equal(HoverPhase.Expanded, sm.Phase);
    }

    [Fact]
    public void Duplicate_enter_does_not_restart_the_hover_delay()
    {
        var time = new FakeTimeProvider();
        using var sm = new IslandStateMachine(time, new InlineDispatcher());

        sm.PointerEntered();
        time.Advance(TimeSpan.FromMilliseconds(100));
        sm.PointerEntered(); // child element / re-hit: must not push the expansion back
        time.Advance(TimeSpan.FromMilliseconds(50));

        Assert.Equal(IslandMode.Expanded, sm.Mode);
    }

    [Fact]
    public void Drag_left_after_a_cancelled_drag_returns_to_rest()
    {
        var time = new FakeTimeProvider();
        using var sm = new IslandStateMachine(time, new InlineDispatcher());
        sm.DragNear();
        sm.DragLeft();
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(IslandMode.Compact, sm.Mode);
    }
}
