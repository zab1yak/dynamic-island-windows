using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland.UI.Controls;

/// <summary>
/// Capsule progress bar that can be dragged to seek. The track grows slightly while hovered or
/// dragged; <see cref="SeekCommand"/> receives the fraction (0–1) when the drag ends.
/// </summary>
public sealed class Scrubber : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(Scrubber), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsInteractiveProperty = DependencyProperty.Register(
        nameof(IsInteractive), typeof(bool), typeof(Scrubber), new PropertyMetadata(true));

    public static readonly DependencyProperty SeekCommandProperty = DependencyProperty.Register(
        nameof(SeekCommand), typeof(ICommand), typeof(Scrubber));

    public static readonly DependencyProperty IsDraggingProperty = DependencyProperty.Register(
        nameof(IsDragging), typeof(bool), typeof(Scrubber),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush), typeof(Brush), typeof(Scrubber), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(Scrubber), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    private double _dragValue;

    public Scrubber()
    {
        Height = 16;
        Cursor = Cursors.Hand;
        Focusable = false;
        MouseEnter += (_, _) => InvalidateVisual();
        MouseLeave += (_, _) => InvalidateVisual();
    }

    /// <summary>Progress 0–1.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Whether dragging is allowed (the app supports seeking).</summary>
    public bool IsInteractive
    {
        get => (bool)GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    /// <summary>Executed with the target fraction when the user releases the thumb.</summary>
    public ICommand? SeekCommand
    {
        get => (ICommand?)GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    /// <summary>True while the user drags.</summary>
    public bool IsDragging
    {
        get => (bool)GetValue(IsDraggingProperty);
        set => SetValue(IsDraggingProperty, value);
    }

    /// <summary>Played-part brush.</summary>
    public Brush FillBrush
    {
        get => (Brush)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    /// <summary>Track brush.</summary>
    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsInteractive)
        {
            return;
        }

        IsDragging = true;
        _dragValue = FractionAt(e.GetPosition(this));
        CaptureMouse();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsDragging)
        {
            _dragValue = FractionAt(e.GetPosition(this));
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsDragging)
        {
            return;
        }

        IsDragging = false;
        ReleaseMouseCapture();
        SetCurrentValue(ValueProperty, _dragValue);
        if (SeekCommand?.CanExecute(_dragValue) == true)
        {
            SeekCommand.Execute(_dragValue);
        }

        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        IsDragging = false;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var active = IsDragging || (IsMouseOver && IsInteractive);
        var thickness = active ? 8.0 : 5.0;
        var y = (ActualHeight - thickness) / 2;
        var radius = thickness / 2;
        drawingContext.DrawRoundedRectangle(TrackBrush, null, new Rect(0, y, ActualWidth, thickness), radius, radius);
        var value = Math.Clamp(IsDragging ? _dragValue : Value, 0, 1);
        var width = Math.Max(thickness, ActualWidth * value);
        drawingContext.DrawRoundedRectangle(FillBrush, null, new Rect(0, y, width, thickness), radius, radius);
    }

    // The hit area is the whole control height, not just the thin bar.
    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters) =>
        new PointHitTestResult(this, hitTestParameters.HitPoint);

    private double FractionAt(Point p) => ActualWidth > 0 ? Math.Clamp(p.X / ActualWidth, 0, 1) : 0;
}
