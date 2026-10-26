using System.Windows;
using System.Windows.Media;
using DynamicIsland.Core.Status;
using DynamicIsland.UI.Icons;

namespace DynamicIsland.UI.Controls;

/// <summary>
/// iOS-style battery: an outlined cell filled to the charge level (white, red when low, yellow in
/// energy saver), or a green circle with a bolt while charging. Collapses when there is no battery.
/// </summary>
public sealed class BatteryIndicator : FrameworkElement
{
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(BatteryStatus), typeof(BatteryIndicator),
        new FrameworkPropertyMetadata(BatteryStatus.None, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure, OnStatusChanged));

    private const double CellWidth = 22;
    private const double CellHeight = 11;
    private const double NubWidth = 2;

    static BatteryIndicator()
    {
        IsHitTestVisibleProperty.OverrideMetadata(typeof(BatteryIndicator), new UIPropertyMetadata(false));
    }

    /// <summary>Battery snapshot.</summary>
    public BatteryStatus Status
    {
        get => (BatteryStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => Status.Appearance switch
    {
        BatteryAppearance.Hidden => default,
        BatteryAppearance.Charging => new Size(16, 16),
        _ => new Size(CellWidth + NubWidth + 1, CellHeight),
    };

    protected override void OnRender(DrawingContext drawingContext)
    {
        var appearance = Status.Appearance;
        if (appearance == BatteryAppearance.Hidden)
        {
            return;
        }

        var y = (ActualHeight - CellHeight) / 2;
        if (appearance == BatteryAppearance.Charging)
        {
            var size = Math.Min(ActualWidth, ActualHeight);
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            drawingContext.DrawEllipse(Brush("Brush.Green"), null, center, size / 2, size / 2);
            if (IconProvider.Get("Zap") is { } bolt)
            {
                var scale = size * 0.62 / IconProvider.GridSize;
                drawingContext.PushTransform(new TranslateTransform(center.X - (IconProvider.GridSize * scale / 2), center.Y - (IconProvider.GridSize * scale / 2)));
                drawingContext.PushTransform(new ScaleTransform(scale, scale));
                var black = Brush("Island.Background");
                drawingContext.DrawGeometry(black, new Pen(black, 1.5) { LineJoin = PenLineJoin.Round }, bolt);
                drawingContext.Pop();
                drawingContext.Pop();
            }

            return;
        }

        var outline = new Pen(Brush("Brush.TextTertiary"), 1);
        drawingContext.DrawRoundedRectangle(null, outline, new Rect(0.5, y + 0.5, CellWidth - 1, CellHeight - 1), 3, 3);
        drawingContext.DrawRoundedRectangle(Brush("Brush.TextTertiary"), null, new Rect(CellWidth + 0.5, y + 3.5, NubWidth, CellHeight - 7), 1, 1);

        var fill = appearance switch
        {
            BatteryAppearance.Low => Brush("Brush.Red"),
            BatteryAppearance.Saver => Brush("Brush.Yellow"),
            _ => Brush("Brush.TextPrimary"),
        };
        var width = Math.Max(2, (CellWidth - 4) * Math.Clamp(Status.Percent, 0, 100) / 100.0);
        drawingContext.DrawRoundedRectangle(fill, null, new Rect(2, y + 2, width, CellHeight - 4), 1.5, 1.5);
    }

    private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var indicator = (BatteryIndicator)d;
        indicator.Visibility = indicator.Status.Appearance == BatteryAppearance.Hidden ? Visibility.Collapsed : Visibility.Visible;
    }

    private Brush Brush(string key) => TryFindResource(key) as Brush ?? Brushes.White;
}
