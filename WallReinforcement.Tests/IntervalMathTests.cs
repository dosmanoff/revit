using WallReinforcement.Geometry;
using Xunit;

namespace WallReinforcement.Tests;

public class IntervalMathTests
{
    // ── Merge ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Merge_CoalescesOverlappingAndTouching()
    {
        var merged = IntervalMath.Merge(new[]
        {
            new Interval(0, 2), new Interval(1, 3),   // overlap → 0..3
            new Interval(3, 4),                        // touches → 0..4
            new Interval(6, 7),                        // separate
        });

        Assert.Equal(2, merged.Count);
        Assert.Equal(new Interval(0, 4), merged[0]);
        Assert.Equal(new Interval(6, 7), merged[1]);
    }

    [Fact]
    public void Merge_SortsAndDropsDegenerate()
    {
        var merged = IntervalMath.Merge(new[] { new Interval(5, 6), new Interval(2, 2), new Interval(0, 1) });
        Assert.Equal(new[] { new Interval(0, 1), new Interval(5, 6) }, merged);
    }

    // ── Subtract ──────────────────────────────────────────────────────────────

    [Fact]
    public void Subtract_NoBlockers_ReturnsWholeSpan()
    {
        var clear = IntervalMath.Subtract(0, 10, System.Array.Empty<Interval>());
        Assert.Equal(new[] { new Interval(0, 10) }, clear);
    }

    [Fact]
    public void Subtract_OneMiddleBlocker_SplitsIntoTwo()
    {
        var clear = IntervalMath.Subtract(0, 10, new[] { new Interval(4, 6) });
        Assert.Equal(new[] { new Interval(0, 4), new Interval(6, 10) }, clear);
    }

    [Fact]
    public void Subtract_BlockerAtEdges_TrimsSpan()
    {
        var clear = IntervalMath.Subtract(0, 10, new[] { new Interval(-2, 2), new Interval(8, 12) });
        Assert.Equal(new[] { new Interval(2, 8) }, clear);
    }

    [Fact]
    public void Subtract_OverlappingBlockers_AreMergedFirst()
    {
        var clear = IntervalMath.Subtract(0, 10, new[] { new Interval(3, 5), new Interval(4, 7) });
        Assert.Equal(new[] { new Interval(0, 3), new Interval(7, 10) }, clear);
    }

    [Fact]
    public void Subtract_FullyCovered_ReturnsEmpty()
    {
        Assert.Empty(IntervalMath.Subtract(2, 8, new[] { new Interval(0, 10) }));
    }

    [Fact]
    public void Subtract_DegenerateSpan_ReturnsEmpty()
    {
        Assert.Empty(IntervalMath.Subtract(5, 5, new[] { new Interval(0, 1) }));
    }
}
