using DynamicIsland.Core.Input;

namespace DynamicIsland.Tests.Island;

public sealed class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x20)]
    [InlineData("win + shift + d", HotkeyModifiers.Win | HotkeyModifiers.Shift, 'D')]
    [InlineData("Ctrl+F12", HotkeyModifiers.Ctrl, 0x7B)]
    [InlineData("Alt+1", HotkeyModifiers.Alt, '1')]
    public void Parses_valid_gestures(string text, HotkeyModifiers modifiers, int vk)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(new HotkeyGesture(modifiers, vk), gesture);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Space")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Ж")]
    public void Rejects_invalid_gestures(string? text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }

    [Theory]
    [InlineData("alt+ctrl+space", "Ctrl+Alt+Space")]
    [InlineData("Ctrl+f5", "Ctrl+F5")]
    [InlineData("Win+Esc", "Win+Esc")]
    public void Round_trips_to_canonical_text(string input, string expected)
    {
        Assert.True(HotkeyGesture.TryParse(input, out var gesture));
        Assert.Equal(expected, gesture.Value.ToString());
    }
}
