using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.Modules.Tools.Views;

/// <summary>Capsule bar whose fill colour comes from a theme brush key (green / yellow / red by load).</summary>
public sealed class LoadBar : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(LoadBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BrushKeyProperty = DependencyProperty.Register(
        nameof(BrushKey), typeof(string), typeof(LoadBar), new FrameworkPropertyMetadata("Brush.Green", FrameworkPropertyMetadataOptions.AffectsRender));

    public double Percent
    {
        get => (double)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public string BrushKey
    {
        get => (string)GetValue(BrushKeyProperty);
        set => SetValue(BrushKeyProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var r = ActualHeight / 2;
        var track = TryFindResource("Brush.Surface3") as Brush ?? Brushes.DimGray;
        var fill = TryFindResource(BrushKey) as Brush ?? Brushes.LimeGreen;
        drawingContext.DrawRoundedRectangle(track, null, new Rect(0, 0, ActualWidth, ActualHeight), r, r);
        var width = Math.Max(ActualHeight, ActualWidth * Math.Clamp(Percent, 0, 100) / 100);
        drawingContext.DrawRoundedRectangle(fill, null, new Rect(0, 0, width, ActualHeight), r, r);
    }
}
