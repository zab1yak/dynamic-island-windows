using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Core.Island;
using DynamicIsland.UI.Shapes;

namespace DynamicIsland.UI.Controls;

/// <summary>
/// A <see cref="Decorator"/> with continuous (squircle) corners: draws the background and an
/// optional hairline border, and clips its child to the same shape.
/// </summary>
public sealed class SquircleBorder : Decorator
{
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(SquircleBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush), typeof(Brush), typeof(SquircleBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness), typeof(double), typeof(SquircleBorder),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(SquircleBorder),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(SquircleBorder),
        new FrameworkPropertyMetadata(default(Thickness), FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Fill brush.</summary>
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>Border brush.</summary>
    public Brush? BorderBrush
    {
        get => (Brush?)GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>Border width.</summary>
    public double BorderThickness
    {
        get => (double)GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>Corner radius; values over half the short side make a capsule.</summary>
    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Inner padding around the child.</summary>
    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var pad = Padding;
        var inner = new Size(
            Math.Max(0, constraint.Width - pad.Left - pad.Right),
            Math.Max(0, constraint.Height - pad.Top - pad.Bottom));
        Child?.Measure(inner);
        var desired = Child?.DesiredSize ?? default;
        return new Size(desired.Width + pad.Left + pad.Right, desired.Height + pad.Top + pad.Bottom);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var pad = Padding;
        Child?.Arrange(new Rect(pad.Left, pad.Top,
            Math.Max(0, arrangeSize.Width - pad.Left - pad.Right),
            Math.Max(0, arrangeSize.Height - pad.Top - pad.Bottom)));
        Clip = BuildGeometry(arrangeSize, 0);
        return arrangeSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        drawingContext.DrawGeometry(Background, null, BuildGeometry(size, 0));
        if (BorderBrush is not null && BorderThickness > 0)
        {
            drawingContext.DrawGeometry(null, new Pen(BorderBrush, BorderThickness), BuildGeometry(size, BorderThickness / 2));
        }
    }

    private StreamGeometry BuildGeometry(Size size, double inset) =>
        OutlineGeometry.ToGeometry(
            IslandShapeMath.RoundedRect(inset, inset, size.Width - (2 * inset), size.Height - (2 * inset), Math.Max(0, CornerRadius - inset)),
            closed: true);
}
