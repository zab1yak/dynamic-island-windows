using System.Windows;
using System.Windows.Media;
using DynamicIsland.Core.Island;

namespace DynamicIsland.UI.Shapes;

/// <summary>Converts a Core <see cref="IslandOutline"/> into a frozen WPF <see cref="StreamGeometry"/>.</summary>
public static class OutlineGeometry
{
    /// <summary>Builds the geometry; <paramref name="closed"/> = <c>false</c> for an open stroke.</summary>
    public static StreamGeometry ToGeometry(IslandOutline outline, bool closed)
    {
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(ToPoint(outline.Start), isFilled: closed, isClosed: closed);
            foreach (var segment in outline.Segments)
            {
                switch (segment)
                {
                    case CubicSegment c:
                        ctx.BezierTo(ToPoint(c.Control1), ToPoint(c.Control2), ToPoint(c.End), isStroked: true, isSmoothJoin: true);
                        break;
                    default:
                        ctx.LineTo(ToPoint(segment.End), isStroked: true, isSmoothJoin: true);
                        break;
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point ToPoint(Point2 p) => new(p.X, p.Y);
}
