using System.Windows;
using System.Windows.Media.Animation;

namespace DynamicIsland.UI.Animation;

/// <summary>
/// Easing that follows an underdamped spring and settles exactly at the end of the animation.
/// Use with 350–450 ms durations for iOS-like motion; <see cref="DampingRatio"/> below 1 overshoots.
/// </summary>
public sealed class SpringEase : EasingFunctionBase
{
    public static readonly DependencyProperty DampingRatioProperty = DependencyProperty.Register(
        nameof(DampingRatio), typeof(double), typeof(SpringEase), new PropertyMetadata(0.7));

    public SpringEase()
    {
        EasingMode = EasingMode.EaseIn;
    }

    /// <summary>ζ in (0, 1): 0.5 is bouncy, 0.7 is a light overshoot, 0.9 almost none.</summary>
    public double DampingRatio
    {
        get => (double)GetValue(DampingRatioProperty);
        set => SetValue(DampingRatioProperty, value);
    }

    /// <summary>Spring progress at normalized time <paramref name="t"/> (0..1).</summary>
    public static double Evaluate(double t, double dampingRatio)
    {
        if (t >= 1)
        {
            return 1;
        }

        var zeta = Math.Clamp(dampingRatio, 0.05, 0.99);
        // Natural frequency chosen so the envelope decays to 0.1 % by t = 1.
        var omega = 6.9 / zeta;
        var omegaD = omega * Math.Sqrt(1 - (zeta * zeta));
        var envelope = Math.Exp(-zeta * omega * t);
        return 1 - (envelope * (Math.Cos(omegaD * t) + (zeta * omega / omegaD * Math.Sin(omegaD * t))));
    }

    protected override double EaseInCore(double normalizedTime) => Evaluate(normalizedTime, DampingRatio);

    protected override Freezable CreateInstanceCore() => new SpringEase();
}
