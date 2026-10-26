using DynamicIsland.Core.Animation;
using DynamicIsland.Core.Island;

namespace DynamicIsland.Tests.Island;

public sealed class SpringTests
{
    [Fact]
    public void Bouncy_spring_overshoots_slightly_and_settles_within_half_a_second()
    {
        var spring = Spring.Bouncy(0);
        spring.Target = 100;
        var max = 0.0;
        var elapsed = 0.0;
        while (spring.Step(1.0 / 60) && elapsed < 2)
        {
            elapsed += 1.0 / 60;
            max = Math.Max(max, spring.Value);
        }

        Assert.InRange(max, 100.5, 110);
        Assert.InRange(elapsed, 0.25, 0.6);
        Assert.Equal(100, spring.Value);
        Assert.True(spring.IsAtRest);
    }

    [Fact]
    public void Result_does_not_depend_on_frame_rate()
    {
        var a = Spring.Bouncy(0);
        var b = Spring.Bouncy(0);
        a.Target = b.Target = 50;
        for (var i = 0; i < 6; i++)
        {
            a.Step(1.0 / 60);
        }

        for (var i = 0; i < 12; i++)
        {
            b.Step(1.0 / 120);
        }

        Assert.Equal(a.Value, b.Value, 0.5);
    }

    [Fact]
    public void Smooth_spring_never_overshoots()
    {
        var spring = Spring.Smooth(0);
        spring.Target = 1;
        while (spring.Step(1.0 / 60))
        {
            Assert.True(spring.Value <= 1.0001);
        }
    }

    [Fact]
    public void Huge_or_invalid_time_steps_are_safe()
    {
        var spring = Spring.Bouncy(0);
        spring.Target = 10;
        spring.Step(double.NaN);
        spring.Step(-1);
        Assert.Equal(0, spring.Value);
        spring.Step(5);
        Assert.Equal(10, spring.Value);
    }
}

public sealed class IslandShapeMathTests
{
    public static TheoryData<IslandMode> Modes() => [IslandMode.Compact, IslandMode.LiveActivity, IslandMode.Expanded, IslandMode.DropTarget];

    private static IslandShapeParams For(IslandMode mode, double cx = 400)
    {
        var size = IslandMetrics.For(mode);
        return new IslandShapeParams(cx, size.Width, size.Height, size.BottomRadius, size.Shoulder);
    }

    private static IEnumerable<Point2> AllPoints(IslandOutline outline)
    {
        yield return outline.Start;
        foreach (var segment in outline.Segments)
        {
            if (segment is CubicSegment c)
            {
                yield return c.Control1;
                yield return c.Control2;
            }

            yield return segment.End;
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Outline_is_closed_and_stays_inside_its_bounds(IslandMode mode)
    {
        var p = For(mode);
        var outline = IslandShapeMath.Build(p);

        Assert.Equal(outline.Start, outline.Segments[^1].End);
        foreach (var pt in AllPoints(outline))
        {
            Assert.InRange(pt.Y, 0, p.Height);
            Assert.InRange(pt.X, p.CenterX - (p.Width / 2) - p.Shoulder - 1e-9, p.CenterX + (p.Width / 2) + p.Shoulder + 1e-9);
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Outline_is_mirror_symmetric(IslandMode mode)
    {
        var p = For(mode, cx: 0);
        var points = AllPoints(IslandShapeMath.Build(p)).ToList();

        foreach (var pt in points)
        {
            Assert.Contains(points, q => Math.Abs(q.X + pt.X) < 1e-9 && Math.Abs(q.Y - pt.Y) < 1e-9);
        }
    }

    [Fact]
    public void Top_edge_lies_on_the_screen_edge_and_stroke_variant_omits_it()
    {
        var p = For(IslandMode.Expanded);
        var closed = IslandShapeMath.Build(p);
        var open = IslandShapeMath.Build(p, includeTopEdge: false);

        Assert.Equal(0, closed.Start.Y);
        Assert.Equal(closed.Segments.Count - 1, open.Segments.Count);
    }

    [Theory]
    [InlineData(10, 5, 40, 20)]
    [InlineData(0, 0, 30, 30)]
    [InlineData(200, 1, 30, 30)]
    public void Radii_are_clamped_for_tiny_sizes(double width, double height, double radius, double shoulder)
    {
        var p = IslandShapeMath.Clamp(new IslandShapeParams(0, width, height, radius, shoulder));

        Assert.True(p.Shoulder <= p.Height * 0.45 + 1e-9);
        Assert.True((p.BottomRadius * IslandShapeMath.CornerExtent) + p.Shoulder <= p.Height + 1e-9);
        Assert.True(2 * p.BottomRadius * IslandShapeMath.CornerExtent <= p.Width + 1e-9);
    }

    [Fact]
    public void Corner_midpoint_matches_a_circular_arc()
    {
        // Midpoint of the bottom-left corner cubic should sit ≈ 0.293·r from the corner, like a circle.
        var p = new IslandShapeParams(0, 400, 200, 30, 0);
        var corner = IslandShapeMath.Build(p).Segments.OfType<CubicSegment>().ElementAt(1);
        var left = -200.0;
        var start = new Point2(left, 200 - (30 * IslandShapeMath.CornerExtent));
        var mid = (0.125 * start.X) + (0.375 * corner.Control1.X) + (0.375 * corner.Control2.X) + (0.125 * corner.End.X);

        Assert.Equal(0.293 * 30, mid - left, 0.5);
    }
}
