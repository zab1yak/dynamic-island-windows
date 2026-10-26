using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland.UI.Controls;

/// <summary>
/// Single-line text that, when too long for its box, scrolls smoothly to show the rest, pauses,
/// and scrolls back. Short text stays still. The animation is a Storyboard on the render thread.
/// </summary>
public sealed class MarqueeText : Canvas
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(MarqueeText), new PropertyMetadata(string.Empty, (d, _) => ((MarqueeText)d).Restart()));

    public static readonly DependencyProperty TextStyleProperty = DependencyProperty.Register(
        nameof(TextStyle), typeof(Style), typeof(MarqueeText), new PropertyMetadata(null, (d, e) => ((MarqueeText)d)._text.Style = (Style?)e.NewValue));

    private const double PixelsPerSecond = 28;
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(2);

    private readonly TextBlock _text = new() { TextTrimming = TextTrimming.None, TextWrapping = TextWrapping.NoWrap };
    private readonly TranslateTransform _offset = new();

    public MarqueeText()
    {
        ClipToBounds = true;
        _text.RenderTransform = _offset;
        Children.Add(_text);
        SizeChanged += (_, _) => Restart();
        IsVisibleChanged += (_, _) => Restart();
    }

    /// <summary>Text to show.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Style of the inner TextBlock.</summary>
    public Style? TextStyle
    {
        get => (Style?)GetValue(TextStyleProperty);
        set => SetValue(TextStyleProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = double.IsInfinity(constraint.Width) ? _text.DesiredSize.Width : constraint.Width;
        return new Size(width, _text.DesiredSize.Height);
    }

    private void Restart()
    {
        _text.Text = Text;
        _offset.BeginAnimation(TranslateTransform.XProperty, null);
        _offset.X = 0;
        if (!IsVisible)
        {
            return;
        }

        _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var overflow = _text.DesiredSize.Width - ActualWidth;
        if (overflow <= 1 || ActualWidth <= 0)
        {
            return;
        }

        var travel = TimeSpan.FromSeconds(overflow / PixelsPerSecond);
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var frames = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(Pause)));
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(Pause + travel), ease));
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(Pause + travel + Pause)));
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(Pause + travel + Pause + travel), ease));
        _offset.BeginAnimation(TranslateTransform.XProperty, frames);
    }
}
