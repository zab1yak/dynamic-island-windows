namespace DynamicIsland.Core.Input;

/// <summary>
/// Two-step confirmation for a destructive button («Выйти из программы»): the first press arms it, a
/// second press within <see cref="Window"/> confirms; afterwards it disarms by itself.
/// </summary>
public sealed class ConfirmGate(TimeProvider time)
{
    /// <summary>How long the armed state lasts.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(4);

    private DateTimeOffset? _armedAt;

    /// <summary>Whether the next press confirms.</summary>
    public bool IsArmed => _armedAt is { } at && time.GetUtcNow() - at < Window;

    /// <summary>Registers a press; returns <c>true</c> when it confirms the action.</summary>
    public bool Press()
    {
        if (IsArmed)
        {
            _armedAt = null;
            return true;
        }

        _armedAt = time.GetUtcNow();
        return false;
    }
}
