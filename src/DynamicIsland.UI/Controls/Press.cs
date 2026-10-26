using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.UI.Animation;

namespace DynamicIsland.UI.Controls;

/// <summary>
/// Attached press feedback: the element shrinks to <c>Press.Scale</c> while pressed and springs
/// back on release. Usage: <c>controls:Press.Scale="0.92"</c>.
/// </summary>
public static class Press
{
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.RegisterAttached(
        "Scale", typeof(double), typeof(Press), new PropertyMetadata(1.0, OnScaleChanged));

    private static readonly Duration DownDuration = new(TimeSpan.FromMilliseconds(90));
    private static readonly Duration UpDuration = new(TimeSpan.FromMilliseconds(420));

    public static double GetScale(DependencyObject element) => (double)element.GetValue(ScaleProperty);

    public static void SetScale(DependencyObject element, double value) => element.SetValue(ScaleProperty, value);

    private static void OnScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.PreviewMouseLeftButtonDown -= OnDown;
        element.PreviewMouseLeftButtonUp -= OnUp;
        element.MouseLeave -= OnUp;
        if ((double)e.NewValue >= 1)
        {
            return;
        }

        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = new ScaleTransform(1, 1);
        element.PreviewMouseLeftButtonDown += OnDown;
        element.PreviewMouseLeftButtonUp += OnUp;
        element.MouseLeave += OnUp;
    }

    private static void OnDown(object sender, MouseButtonEventArgs e)
    {
        var element = (UIElement)sender;
        Animate(element, GetScale(element), DownDuration, new QuadraticEase { EasingMode = EasingMode.EaseOut });
    }

    private static void OnUp(object sender, MouseEventArgs e) =>
        Animate((UIElement)sender, 1, UpDuration, new SpringEase { DampingRatio = 0.55 });

    private static void Animate(UIElement element, double to, Duration duration, IEasingFunction ease)
    {
        if (element.RenderTransform is not ScaleTransform scale || scale.IsFrozen)
        {
            scale = new ScaleTransform(1, 1);
            element.RenderTransform = scale;
        }

        var animation = new DoubleAnimation(to, duration) { EasingFunction = ease };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }
}
