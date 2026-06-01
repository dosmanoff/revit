using SlabReinforcement.Geometry;
using Xunit;

namespace SlabReinforcement.Tests;

public class GeometryMathTests
{
    private const double Eps = 1e-9;

    // ── Signed area (shoelace) ────────────────────────────────────────────────

    [Fact]
    public void SignedArea_CcwSquare_IsPositive()
    {
        Pt2[] sq = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
        Assert.Equal(100.0, GeometryMath.SignedArea(sq), 6);
    }

    [Fact]
    public void SignedArea_CwSquare_IsNegative()
    {
        Pt2[] sq = [new(0, 0), new(0, 10), new(10, 10), new(10, 0)];
        Assert.Equal(-100.0, GeometryMath.SignedArea(sq), 6);
    }

    [Fact]
    public void Loop_Area_IsAlwaysAbsolute()
    {
        Pt2[] cw = [new(0, 0), new(0, 10), new(10, 10), new(10, 0)];
        Assert.Equal(100.0, new Loop2(cw).Area, 6);
    }

    [Fact]
    public void SignedArea_Triangle()
    {
        Pt2[] tri = [new(0, 0), new(4, 0), new(0, 3)];
        Assert.Equal(6.0, GeometryMath.SignedArea(tri), 6);
    }

    // ── Largest loop selection (outer boundary vs openings) ───────────────────

    [Fact]
    public void LargestLoopIndex_PicksBiggestArea()
    {
        var small = new Loop2([new(0, 0), new(2, 0), new(2, 2), new(0, 2)]);   // 4
        var big   = new Loop2([new(0, 0), new(10, 0), new(10, 10), new(0, 10)]); // 100
        var mid   = new Loop2([new(0, 0), new(5, 0), new(5, 5), new(0, 5)]);   // 25

        Assert.Equal(1, GeometryMath.LargestLoopIndex([small, big, mid]));
    }

    [Fact]
    public void LargestLoopIndex_EmptyList_IsMinusOne()
    {
        Assert.Equal(-1, GeometryMath.LargestLoopIndex([]));
    }

    // ── Longest-edge direction / plan basis ───────────────────────────────────

    [Fact]
    public void LongestEdge_Rectangle_RunsAlongTheLongSide()
    {
        // 30 (along X) × 10 rectangle — longest edges are horizontal.
        Pt2[] rect = [new(0, 0), new(30, 0), new(30, 10), new(0, 10)];
        Pt2 dir = GeometryMath.LongestEdgeDirection(rect);

        Assert.Equal(1.0, dir.X, 9);
        Assert.Equal(0.0, dir.Y, 9);
    }

    [Fact]
    public void Basis_RectangleRotated90_XRunsVertical_YIsPerp()
    {
        // Longest edge (length 30) runs along +Y.
        Pt2[] rect = [new(0, 0), new(0, 30), new(-10, 30), new(-10, 0)];
        PlanBasis b = GeometryMath.BasisFromLoop(rect);

        Assert.Equal(0.0, b.X.X, 9);
        Assert.Equal(1.0, b.X.Y, 9);
        Assert.Equal(90.0, b.AngleDeg, 6);

        // Y = 90° CCW from X = (-1, 0)
        Assert.Equal(-1.0, b.Y.X, 9);
        Assert.Equal(0.0, b.Y.Y, 9);
    }

    [Fact]
    public void Basis_Diagonal_ReportsFortyFiveDegrees()
    {
        // Longest edge is the diagonal from (0,0) to (10,10) ≈ 14.14, the rest are shorter.
        Pt2[] loop = [new(0, 0), new(10, 10), new(8, 12), new(-2, 2)];
        PlanBasis b = GeometryMath.BasisFromLoop(loop);

        Assert.Equal(45.0, b.AngleDeg, 6);
        Assert.Equal(Math.Sqrt(0.5), b.X.X, 9);
        Assert.Equal(Math.Sqrt(0.5), b.X.Y, 9);
    }

    [Fact]
    public void LongestEdge_Square_TiesToFirstEdge()
    {
        Pt2[] sq = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
        Pt2 dir = GeometryMath.LongestEdgeDirection(sq);

        Assert.Equal(1.0, dir.X, 9);
        Assert.Equal(0.0, dir.Y, 9);
    }

    // ── Bounds ────────────────────────────────────────────────────────────────

    [Fact]
    public void Bounds_OfLoop_AreTight()
    {
        var loop = new Loop2([new(2, -1), new(7, -1), new(7, 4), new(2, 4)]);
        Bounds2 bb = loop.Bounds;

        Assert.Equal(2.0, bb.MinX, Eps);
        Assert.Equal(-1.0, bb.MinY, Eps);
        Assert.Equal(7.0, bb.MaxX, Eps);
        Assert.Equal(4.0, bb.MaxY, Eps);
        Assert.Equal(5.0, bb.Width, Eps);
        Assert.Equal(5.0, bb.Height, Eps);
    }
}
