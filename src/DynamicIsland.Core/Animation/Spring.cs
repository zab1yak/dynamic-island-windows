namespace DynamicIsland.Core.Animation;

/// <summary>
/// A damped spring driving one value toward <see cref="Target"/>. Integrated with fixed small
/// sub-steps (semi-implicit Euler), so the result does not depend on the frame rate.
/// </summary>
public sealed class Spring
{
    private const double MaxSubStep = 1.0 / 240.0;

    /// <summary>Creates a spring resting at <paramref name="value"/>.</summary>
    /// <param name="stiffness">Spring constant k (higher = faster).</param>
    /// <param name="damping">Damping c; below <c>2·√k</c> the spring overshoots.</param>
    /// <param name="restDelta">Distance to the target under which the spring may settle.</param>
    public Spring(double value, double stiffness, double damping, double restDelta = 0.25)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stiffness);
        ArgumentOutOfRangeException.ThrowIfNegative(damping);
        Value = value;
        Target = value;
        Stiffness = stiffness;
        Damping = damping;
        RestDelta = restDelta;
    }

    /// <summary>Current value.</summary>
    public double Value { get; private set; }

    /// <summary>Current velocity (units per second).</summary>
    public double Velocity { get; private set; }

    /// <summary>Value the spring is moving to.</summary>
    public double Target { get; set; }

    /// <summary>Spring constant.</summary>
    public double Stiffness { get; set; }

    /// <summary>Damping coefficient.</summary>
    public double Damping { get; set; }

    /// <summary>Settling threshold for the distance; the velocity threshold is 4× this per second.</summary>
    public double RestDelta { get; }

    /// <summary>Whether the spring has settled on its target.</summary>
    public bool IsAtRest => Value == Target && Velocity == 0;

    /// <summary>Jumps to <paramref name="value"/> without animation.</summary>
    public void SnapTo(double value)
    {
        Value = value;
        Target = value;
        Velocity = 0;
    }

    /// <summary>Advances the simulation by <paramref name="seconds"/>. Returns <c>true</c> while still moving.</summary>
    public bool Step(double seconds)
    {
        if (IsAtRest || seconds <= 0 || !double.IsFinite(seconds))
        {
            return !IsAtRest;
        }

        var steps = (int)Math.Ceiling(seconds / MaxSubStep);
        var h = seconds / steps;
        for (var i = 0; i < steps; i++)
        {
            var acceleration = (-Stiffness * (Value - Target)) - (Damping * Velocity);
            Velocity += acceleration * h;
            Value += Velocity * h;
        }

        if (Math.Abs(Value - Target) < RestDelta && Math.Abs(Velocity) < RestDelta * 4)
        {
            Value = Target;
            Velocity = 0;
            return false;
        }

        return true;
    }

    /// <summary>A bouncy spring for size changes: settles in ≈0.4 s with a light overshoot.</summary>
    public static Spring Bouncy(double value, double restDelta = 0.25) => new(value, stiffness: 300, damping: 24, restDelta);

    /// <summary>A critically damped spring for fades: no overshoot.</summary>
    public static Spring Smooth(double value, double restDelta = 0.004) => new(value, stiffness: 400, damping: 40, restDelta);
}
