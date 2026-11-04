using DynamicIsland.Core.Island;
using Microsoft.Extensions.Time.Testing;

namespace DynamicIsland.Tests.Island;

public sealed class ManualVisibilityTests
{
    [Fact]
    public void Hiding_cancels_pending_hover_and_ignores_all_expansion_inputs()
    {
        var time = new FakeTimeProvider();
        using var sm = new IslandStateMachine(time, new InlineDispatcher());
        var dropped = false;
        sm.DropCompleted += (_, _) => dropped = true;
        sm.PointerEntered();
        sm.SetManuallyHidden(true);
        sm.PointerEntered();
        sm.Clicked();
        sm.HotkeyPressed();
        sm.DragNear();
        sm.Dropped();
        sm.SetLiveActivity(true);
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(IslandMode.Hidden, sm.Mode);
        Assert.True(sm.IsManuallyHidden);
        Assert.False(dropped);

        sm.SetManuallyHidden(false);
        Assert.Equal(IslandMode.LiveActivity, sm.Mode);
        sm.PointerEntered();
        time.Advance(sm.ExpandDelay);
        Assert.Equal(IslandMode.Expanded, sm.Mode);
    }

    [Fact]
    public void Manual_hide_overrides_pin_and_fullscreen_changes_do_not_restore_it()
    {
        using var sm = new IslandStateMachine(new FakeTimeProvider(), new InlineDispatcher());
        sm.HotkeyPressed();
        Assert.Equal(IslandMode.Pinned, sm.Mode);
        sm.SetManuallyHidden(true);
        sm.SetFullscreen(true);
        sm.SetFullscreen(false);
        Assert.Equal(IslandMode.Hidden, sm.Mode);
        sm.SetManuallyHidden(false);
        Assert.Equal(IslandMode.Compact, sm.Mode);
    }

    [Fact]
    public void Restoring_respects_fullscreen_and_lock()
    {
        using var sm = new IslandStateMachine(new FakeTimeProvider(), new InlineDispatcher());
        sm.SetManuallyHidden(true);
        sm.SetFullscreen(true);
        sm.SetLocked(true);
        sm.SetManuallyHidden(false);
        Assert.Equal(IslandMode.Hidden, sm.Mode);
        sm.SetFullscreen(false);
        Assert.Equal(IslandMode.Compact, sm.Mode);
        sm.PointerEntered();
        Assert.Equal(HoverPhase.Idle, sm.Phase);
    }
}
