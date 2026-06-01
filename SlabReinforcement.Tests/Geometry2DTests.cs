using SlabReinforcement.Geometry;
using Xunit;

namespace SlabReinforcement.Tests;

public class Geometry2DTests
{
    private static readonly Pt2[] Square = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
    private const double AngTol = 0.05;   // ~2.9°

    // ── Point in loop ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(5, 5, true)]
    [InlineData(0.1, 0.1, true)]
    [InlineData(9.9, 5, true)]
    [InlineData(15, 5, false)]
    [InlineData(-1, 5, false)]
    [InlineData(5, 15, false)]
    public void PointInLoop_Square(double x, double y, bool expected)
    {
        Assert.Equal(expected, Geometry2D.PointInLoop(Square, new Pt2(x, y)));
    }

    [Fact]
    public void PointInLoop_ConcaveLShape()
    {
        // L-shape: outer corner at (10,10) cut away to (5,5).
        Pt2[] el = [new(0, 0), new(10, 0), new(10, 5), new(5, 5), new(5, 10), new(0, 10)];
        Assert.True(Geometry2D.PointInLoop(el, new Pt2(2, 8)));    // in the standing leg
        Assert.False(Geometry2D.PointInLoop(el, new Pt2(8, 8)));   // in the cut-away notch
    }

    // ── Distance point→segment ────────────────────────────────────────────────

    [Fact]
    public void DistancePointToSegment_PerpendicularFoot()
    {
        var s = new Seg2(new Pt2(0, 0), new Pt2(10, 0));
        Assert.Equal(5.0, Geometry2D.DistancePointToSegment(new Pt2(5, 5), s), 9);
    }

    [Fact]
    public void DistancePointToSegment_ClampsToEndpoint()
    {
        var s = new Seg2(new Pt2(0, 0), new Pt2(10, 0));
        Assert.Equal(5.0, Geometry2D.DistancePointToSegment(new Pt2(-5, 0), s), 9);
        Assert.Equal(0.0, Geometry2D.DistancePointToSegment(new Pt2(5, 0), s), 9);
    }

    // ── Collinear overlap (edge ↔ wall/beam/neighbor centerline) ───────────────

    [Fact]
    public void CollinearOverlap_InnerSegment()
    {
        var edge = new Seg2(new Pt2(0, 0), new Pt2(10, 0));
        var wall = new Seg2(new Pt2(2, 0), new Pt2(8, 0));
        Assert.Equal(6.0, Geometry2D.CollinearOverlapLength(edge, wall, AngTol, 0.1), 9);
    }

    [Fact]
    public void CollinearOverlap_SmallOffsetWithinTolerance()
    {
        var edge = new Seg2(new Pt2(0, 0), new Pt2(10, 0));
        var wall = new Seg2(new Pt2(2, 0.05), new Pt2(8, 0.05));
        Assert.Equal(6.0, Geometry2D.CollinearOverlapLength(edge, wall, AngTol, 0.1), 9);
    }

    [Fact]
    public void CollinearOverlap_TooFarOffset_IsZero()
    {
        var edge = new Seg2(new Pt2(0, 0), new Pt2(10, 0));
        var wall = new Seg2(new Pt2(2, 1.0), new Pt2(8, 1.0));
        Assert.Equal(0.0, Geometry2D.CollinearOverlapLength(edge, wall, AngTol, 0.1), 9);
    }

    [Fact]
    public void CollinearOverlap_OppositeDirection_StillCounts()
    {
        var edge = new Seg2(new Pt2(0, 0), new Pt2(10, 0));
        var wall = new Seg2(new Pt2(8, 0), new Pt2(2, 0));
        Assert.Equal(6.0, Geometry2D.CollinearOverlapLength(edge, wall, AngTol, 0.1), 9);
    }

    [Fact]
    public void CollinearOverlap_PartlyPastTheEnd_ClipsToReference()
    {
        var edge = new Seg2(new Pt2(0, 0), new Pt2(10, 0));
        var wall = new Seg2(new Pt2(6, 0), new Pt2(15, 0));
        Assert.Equal(4.0, Geometry2D.CollinearOverlapLength(edge, wall, AngTol, 0.1), 9);
    }

    [Fact]
    public void CollinearOverlap_Perpendicular_IsZero()
    {
        var edge = new Seg2(new Pt2(0, 0), new Pt2(10, 0));
        var cross = new Seg2(new Pt2(5, -5), new Pt2(5, 5));
        Assert.Equal(0.0, Geometry2D.CollinearOverlapLength(edge, cross, AngTol, 0.1), 9);
    }
}
