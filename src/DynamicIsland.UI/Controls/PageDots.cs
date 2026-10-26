using System.Windows;
using System.Windows.Media;
using DynamicIsland.Core.Animation;
using DynamicIsland.UI.Animation;

namespace DynamicIsland.UI.Controls;

/// <summary>
/// Page indicator: dots, with the active one stretched into a pill. The stretch follows a spring
/// and the frame clock runs only while dots are moving.
/// </summary>
public sealed class PageDots : FrameworkElement
{
    public static readonly DependencyProperty CountProperty = DependencyProperty.Register(
        nameof(Count), typeof(int), typeof(PageDots),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((PageDots)d).Retarget(snap: true)));

    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(PageDots),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((PageDots)d).Retarget(snap: false)));

    public static readonly DependencyProperty ActiveBrushProperty = DependencyProperty.Register(
        nameof(ActiveBrush), typeof(Brush), typeof(PageDots),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty InactiveBrushProperty = DependencyProperty.Register(
        nameof(InactiveBrush), typeof(Brush), typeof(PageDots),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double DotSize = 6;
    private const double ActiveWidth = 18;
    private const double Gap = 6;

    private readonly AnimationClock _clock;
    private readonly List<Spring> _widths = [];

    public PageDots()
    {
        _clock = new AnimationClock(Tick);
        Unloaded += (_, _) => _clock.Stop();
    }

    /// <summary>Number of pages.</summary>
    public int Count
    {
        get => (int)GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    /// <summary>Active page.</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>Brush of the active pill.</summary>
    public Brush ActiveBrush
    {
        get => (Brush)GetValue(ActiveBrushProperty);
        set => SetValue(ActiveBrushProperty, value);
    }

    /// <summary>Brush of inactive dots.</summary>
    public Brush InactiveBrush
    {
        get => (Brush)GetValue(InactiveBrushProperty);
        set => SetValue(InactiveBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = Math.Max(Count, 0);
        return count == 0 ? default : new Size(ActiveWidth + ((count - 1) * (DotSize + Gap)), DotSize);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var x = (ActualWidth - DesiredSize.Width) / 2;
        for (var i = 0; i < _widths.Count; i++)
        {
            var width = _widths[i].Value;
            var progress = (width - DotSize) / (ActiveWidth - DotSize);
            var brush = progress > 0.5 ? ActiveBrush : InactiveBrush;
            drawingContext.DrawRoundedRectangle(brush, null, new Rect(x, (ActualHeight - DotSize) / 2, width, DotSize), DotSize / 2, DotSize / 2);
            x += width + Gap;
        }
    }

    private void Retarget(bool snap)
    {
        var count = Math.Max(Count, 0);
        while (_widths.Count < count)
        {
            _widths.Add(Spring.Bouncy(DotSize, restDelta: 0.05));
        }

        _widths.RemoveRange(count, _widths.Count - count);
        for (var i = 0; i < count; i++)
        {
            var target = i == SelectedIndex ? ActiveWidth : DotSize;
            if (snap)
            {
                _widths[i].SnapTo(target);
            }
            else
            {
                _widths[i].Target = target;
            }
        }

        if (snap)
        {
            InvalidateVisual();
        }
        else
        {
            _clock.Start();
        }
    }

    private bool Tick(double dt)
    {
        var moving = false;
        foreach (var spring in _widths)
        {
            moving |= spring.Step(dt);
        }

        InvalidateVisual();
        return moving;
    }
}
