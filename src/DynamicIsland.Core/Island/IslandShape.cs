namespace DynamicIsland.Core.Island;

/// <summary>A 2-D point in device-independent pixels.</summary>
public readonly record struct Point2(double X, double Y);

/// <summary>One segment of the island outline.</summary>
public abstract record ShapeSegment(Point2 End);

/// <summary>Straight line to <see cref="ShapeSegment.End"/>.</summary>
public sealed record LineSegment(Point2 End) : ShapeSegment(End);

/// <summary>Cubic Bézier to <see cref="ShapeSegment.End"/>.</summary>
public sealed record CubicSegment(Point2 Control1, Point2 Control2, Point2 End) : ShapeSegment(End);

/// <summary>Size and corner parameters of the island at one instant.</summary>
/// <param name="CenterX">Horizontal centre in the host's coordinates.</param>
/// <param name="Width">Body width (without shoulders).</param>
/// <param name="Height">Body height, measured from the top edge of the screen.</param>
/// <param name="BottomRadius">Radius of the two bottom corners.</param>
/// <param name="Shoulder">Radius of the concave "shoulders" joining the body to the screen edge.</param>
public readonly record struct IslandShapeParams(double CenterX, double Width, double Height, double BottomRadius, double Shoulder);

/// <summary>The outline: <see cref="Start"/> followed by <see cref="Segments"/>.</summary>
public sealed record IslandOutline(Point2 Start, IReadOnlyList<ShapeSegment> Segments);

/// <summary>
/// Builds the island outline: the top is fused with the screen edge, the top corners flare out
/// into concave inverted fillets ("shoulders"), and the bottom corners use continuous-curvature
/// (squircle-like) curves instead of circular arcs.
/// </summary>
public static class IslandShapeMath
{
    /// <summary>
    /// A continuous corner starts this many radii before the corner point (Apple's iOS corners use
    /// ≈1.53); the curve then blends into the straight edge with nearly zero curvature.
    /// </summary>
    public const double CornerExtent = 1.528;

    /// <summary>
    /// Where the Bézier control points sit along each edge (fraction of the extent). Chosen so the
    /// curve's midpoint matches a circular arc of the same radius (0.293·r from the corner).
    /// </summary>
    public const double CornerControl = 0.822;

    /// <summary>Control-point factor of a cubic approximating a quarter circle.</summary>
    public const double CircleKappa = 0.5523;

    /// <summary>Clamps radii so the curves never overlap at small sizes.</summary>
    public static IslandShapeParams Clamp(IslandShapeParams p)
    {
        var width = Math.Max(p.Width, 0);
        var height = Math.Max(p.Height, 0);
        var shoulder = Math.Clamp(p.Shoulder, 0, height * 0.45);
        var maxCorner = Math.Min((height - shoulder) / CornerExtent, width / (2 * CornerExtent));
        var radius = Math.Clamp(p.BottomRadius, 0, Math.Max(maxCorner, 0));
        return p with { Width = width, Height = height, Shoulder = shoulder, BottomRadius = radius };
    }

    /// <summary>
    /// Builds the closed outline (for the fill and clip). Use <paramref name="includeTopEdge"/> =
    /// <c>false</c> for a stroke that must not draw along the screen edge.
    /// </summary>
    public static IslandOutline Build(IslandShapeParams parameters, bool includeTopEdge = true)
    {
        var p = Clamp(parameters);
        var left = p.CenterX - (p.Width / 2);
        var right = p.CenterX + (p.Width / 2);
        var s = p.Shoulder;
        var r = p.BottomRadius;
        var e = r * CornerExtent;
        var c = e * CornerControl;
        var k = s * CircleKappa;
        var h = p.Height;

        var segments = new List<ShapeSegment>(10);
        var start = new Point2(left - s, 0);

        // Left shoulder: concave quarter circle centred at (left - s, s), from the top edge down into the wall.
        segments.Add(new CubicSegment(new Point2(left - s + k, 0), new Point2(left, s - k), new Point2(left, s)));
        segments.Add(new LineSegment(new Point2(left, h - e)));
        // Bottom-left continuous corner.
        segments.Add(new CubicSegment(new Point2(left, h - e + c), new Point2(left + e - c, h), new Point2(left + e, h)));
        segments.Add(new LineSegment(new Point2(right - e, h)));
        // Bottom-right continuous corner.
        segments.Add(new CubicSegment(new Point2(right - e + c, h), new Point2(right, h - e + c), new Point2(right, h - e)));
        segments.Add(new LineSegment(new Point2(right, s)));
        // Right shoulder.
        segments.Add(new CubicSegment(new Point2(right, s - k), new Point2(right + s - k, 0), new Point2(right + s, 0)));
        if (includeTopEdge)
        {
            segments.Add(new LineSegment(start));
        }

        return new IslandOutline(start, segments);
    }
}
