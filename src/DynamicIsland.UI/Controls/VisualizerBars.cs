using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.UI.Controls;

/// <summary>Rounded vertical bars drawn from a list of levels (0–1), centred vertically.</summary>
public sealed class VisualizerBars : FrameworkElement
{
    public static readonly DependencyProperty LevelsProperty = DependencyProperty.Register(
        nameof(Levels), typeof(IReadOnlyList<double>), typeof(VisualizerBars),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(VisualizerBars),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarWidthProperty = DependencyProperty.Register(
        nameof(BarWidth), typeof(double), typeof(VisualizerBars),
        new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender));

    static VisualizerBars()
    {
        IsHitTestVisibleProperty.OverrideMetadata(typeof(VisualizerBars), new UIPropertyMetadata(false));
    }

    /// <summary>Bar heights 0–1.</summary>
    public IReadOnlyList<double>? Levels
    {
        get => (IReadOnlyList<double>?)GetValue(LevelsProperty);
        set => SetValue(LevelsProperty, value);
    }

    /// <summary>Bar brush.</summary>
    public Brush BarBrush
    {
        get => (Brush)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    /// <summary>Width of each bar.</summary>
    public double BarWidth
    {
        get => (double)GetValue(BarWidthProperty);
        set => SetValue(BarWidthProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var levels = Levels;
        if (levels is null || levels.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var gap = (ActualWidth - (levels.Count * BarWidth)) / Math.Max(1, levels.Count - 1);
        for (var i = 0; i < levels.Count; i++)
        {
            var h = Math.Max(BarWidth, ActualHeight * Math.Clamp(levels[i], 0, 1));
            var x = i * (BarWidth + gap);
            var y = (ActualHeight - h) / 2;
            drawingContext.DrawRoundedRectangle(BarBrush, null, new Rect(x, y, BarWidth, h), BarWidth / 2, BarWidth / 2);
        }
    }
}
