using SlabReinforcement.Geometry;
using Xunit;

namespace SlabReinforcement.Tests;

public class FieldLayoutTests
{
    // ── SplitWithLaps ─────────────────────────────────────────────────────────

    [Fact]
    public void Split_ShortRun_IsOneBar()
    {
        var segs = FieldLayout.SplitWithLaps(30, 40, 2);
        Assert.Single(segs);
        Assert.Equal((0, 30), segs[0]);
    }

    [Fact]
    public void Split_LongRun_LapsAndEndsAtLength()
    {
        var segs = FieldLayout.SplitWithLaps(100, 40, 2);

        Assert.Equal(3, segs.Count);
        Assert.Equal(0.0, segs[0].Start, 9);
        Assert.Equal(100.0, segs[^1].End, 9);

        // every bar ≤ max, every interior joint overlaps by exactly the lap
        foreach (var s in segs) Assert.True(s.End - s.Start <= 40 + 1e-9);
        for (int i = 1; i < segs.Count; i++)
            Assert.Equal(2.0, segs[i - 1].End - segs[i].Start, 9);
    }

    [Fact]
    public void Split_Stagger_OffsetsTheFirstJoint()
    {
        var normal = FieldLayout.SplitWithLaps(100, 40, 2);
        var staggered = FieldLayout.SplitWithLaps(100, 40, 2, firstBarLen: 20);

        Assert.Equal(40.0, normal[0].End, 9);
        Assert.Equal(20.0, staggered[0].End, 9);          // first joint shifted
        Assert.Equal(100.0, staggered[^1].End, 9);        // still ends at the full length
        foreach (var s in staggered) Assert.True(s.End - s.Start <= 40 + 1e-9);
    }

    [Fact]
    public void Split_CoversWholeRun_NoGaps()
    {
        var segs = FieldLayout.SplitWithLaps(57.3, 20, 1.5);
        Assert.Equal(0.0, segs[0].Start, 9);
        Assert.Equal(57.3, segs[^1].End, 9);
        // consecutive bars touch or overlap (no gap)
        for (int i = 1; i < segs.Count; i++)
            Assert.True(segs[i].Start <= segs[i - 1].End + 1e-9);
    }

    // ── Rails ─────────────────────────────────────────────────────────────────

    private static Loop2 Square(double s) => new([new(0, 0), new(s, 0), new(s, s), new(0, s)]);

    [Fact]
    public void Rails_Square_NoHoles_SpansFullWidth()
    {
        var rails = FieldLayout.Rails(Square(20), [], new Pt2(1, 0), spacing: 5, sideInset: 1, endInset: 1);

        // scan y = 1, 6, 11, 16 → 4 rails, each clipped to x ∈ [1, 19]
        Assert.Equal(4, rails.Count);
        foreach (Seg2 r in rails)
        {
            Assert.Equal(18.0, r.Length, 6);
            Assert.Equal(1.0, Math.Min(r.A.X, r.B.X), 6);
            Assert.Equal(19.0, Math.Max(r.A.X, r.B.X), 6);
        }
    }

    [Fact]
    public void Rails_Square_WithHole_SplitsAroundIt()
    {
        var hole = new Loop2([new(8, 8), new(12, 8), new(12, 12), new(8, 12)]);

        // a scan line through the hole's band must yield two rails (left and right of the hole)
        var rails = FieldLayout.Rails(Square(20), [hole], new Pt2(1, 0), spacing: 20, sideInset: 10, endInset: 0);

        // sideInset 10 + step 20 → a single scan line at y = 10 (through the hole)
        Assert.Equal(2, rails.Count);
        double[] spans = rails.Select(r => Math.Min(r.A.X, r.B.X)).OrderBy(x => x).ToArray();
        Assert.Equal(0.0, spans[0], 6);    // left rail starts at x=0
        Assert.Equal(12.0, spans[1], 6);   // right rail starts at the hole's far edge
    }

    // ── Bands (rebar sets) ────────────────────────────────────────────────────

    [Fact]
    public void Bands_Square_IsOneBand()
    {
        var rails = FieldLayout.LocalRails(Square(20), [], new Pt2(1, 0), spacing: 5, sideInset: 1, endInset: 1);
        var bands = FieldLayout.Bands(rails, 5);

        Assert.Single(bands);
        Assert.Equal(4, bands[0].Count);              // scan y = 1,6,11,16
        Assert.Equal(1.0, bands[0].Start, 6);
        Assert.Equal(19.0, bands[0].End, 6);
        Assert.Equal(1.0, bands[0].Perp0, 6);
    }

    [Fact]
    public void Bands_WithHole_SplitsIntoRegions()
    {
        var hole = new Loop2([new(8, 8), new(12, 8), new(12, 12), new(8, 12)]);
        var rails = FieldLayout.LocalRails(Square(20), [hole], new Pt2(1, 0), spacing: 1, sideInset: 1, endInset: 0);
        var bands = FieldLayout.Bands(rails, 1);

        Assert.True(bands.Count >= 3);                              // above, below, left, right of the hole
        Assert.Contains(bands, b => Math.Abs(b.End - 8) < 1e-3);    // a band stops at the hole's left edge
        Assert.Contains(bands, b => Math.Abs(b.Start - 12) < 1e-3); // a band starts at the hole's right edge
        // full-width bands above & below the hole still exist
        Assert.Contains(bands, b => Math.Abs(b.Start) < 1e-3 && Math.Abs(b.End - 20) < 1e-3);
    }

    [Fact]
    public void Rails_YDirection_RunsVertically()
    {
        var rails = FieldLayout.Rails(Square(20), [], new Pt2(0, 1), spacing: 5, sideInset: 1, endInset: 1);

        Assert.Equal(4, rails.Count);
        foreach (Seg2 r in rails)
        {
            Assert.Equal(18.0, r.Length, 6);
            Assert.Equal(0.0, Math.Abs(r.A.X - r.B.X), 6);   // vertical: constant X
        }
    }

    // ── ClipToFootprint ─────────────────────────────────────────────────────────

    [Fact]
    public void Clip_FullyInside_ReturnedUnchanged()
    {
        var pieces = FieldLayout.ClipToFootprint(new Seg2(new(5, 5), new(15, 5)), Square(20), []);

        Assert.Single(pieces);
        Assert.Equal(10.0, pieces[0].Length, 6);
    }

    [Fact]
    public void Clip_StraddlingEdge_TrimmedToInside()
    {
        // bar runs off the right edge (x = 20)
        var pieces = FieldLayout.ClipToFootprint(new Seg2(new(15, 10), new(25, 10)), Square(20), []);

        Assert.Single(pieces);
        Assert.Equal(5.0, pieces[0].Length, 6);
        double maxX = Math.Max(pieces[0].A.X, pieces[0].B.X);
        Assert.Equal(20.0, maxX, 6);
    }

    [Fact]
    public void Clip_FullyOutside_ReturnsEmpty()
    {
        var pieces = FieldLayout.ClipToFootprint(new Seg2(new(25, 10), new(35, 10)), Square(20), []);
        Assert.Empty(pieces);
    }

    [Fact]
    public void Clip_CrossingHole_SplitsIntoTwo()
    {
        var hole = new Loop2([new(8, 8), new(12, 8), new(12, 12), new(8, 12)]);
        var pieces = FieldLayout.ClipToFootprint(new Seg2(new(5, 10), new(15, 10)), Square(20), [hole]);

        Assert.Equal(2, pieces.Count);
        double total = pieces.Sum(p => p.Length);
        Assert.Equal(6.0, total, 6);                          // (5→8) + (12→15) = 3 + 3
        Assert.All(pieces, p => Assert.Equal(3.0, p.Length, 6));
    }
}
