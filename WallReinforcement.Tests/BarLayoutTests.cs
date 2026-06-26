using WallReinforcement.Geometry;
using Xunit;

namespace WallReinforcement.Tests;

public class BarLayoutTests
{
    // ── UniformLayout (count + exact spacing for a rebar set) ─────────────────

    [Fact]
    public void UniformLayout_FitsEndpointsWithinDesiredStep()
    {
        // 0..20 ft at a desired 16" (1.333 ft) pitch → 15 gaps, 16 bars, spacing ≤ pitch.
        var (count, spacing, first) = BarLayout.UniformLayout(0, 20, 16.0 / 12.0);

        Assert.Equal(16, count);
        Assert.Equal(0.0, first, 9);
        Assert.True(spacing <= 16.0 / 12.0 + 1e-9);
        Assert.Equal(20.0, (count - 1) * spacing, 6); // last bar lands exactly on 'to'
    }

    [Fact]
    public void UniformLayout_StepLargerThanSpan_PlacesTwoEndBars()
    {
        var (count, spacing, _) = BarLayout.UniformLayout(0, 1, 10);
        Assert.Equal(2, count);
        Assert.Equal(1.0, spacing, 9);
    }

    [Theory]
    [InlineData(5, 5, 1)]     // zero span
    [InlineData(10, 5, 1)]    // reversed
    [InlineData(0, 10, 0)]    // non-positive step
    public void UniformLayout_DegenerateInput_IsEmpty(double from, double to, double step)
    {
        Assert.Equal(0, BarLayout.UniformLayout(from, to, step).count);
    }

    // ── EvenlySpaced (per-row positions) ──────────────────────────────────────

    [Fact]
    public void EvenlySpaced_IncludesBothEndpoints()
    {
        var pts = BarLayout.EvenlySpaced(0, 10, 2.5).ToList();
        Assert.Equal(5, pts.Count); // 0, 2.5, 5, 7.5, 10
        Assert.Equal(0.0, pts.First(), 9);
        Assert.Equal(10.0, pts.Last(), 9);
    }

    [Fact]
    public void EvenlySpaced_RoundsUpToKeepStepWithinLimit()
    {
        // span 10, step 3 → ceil(10/3) = 4 gaps → 5 points, actual step 2.5 ≤ 3.
        var pts = BarLayout.EvenlySpaced(0, 10, 3).ToList();
        Assert.Equal(5, pts.Count);
        for (int i = 1; i < pts.Count; i++)
            Assert.True(pts[i] - pts[i - 1] <= 3 + 1e-9);
    }

    [Theory]
    [InlineData(5, 5, 1)]
    [InlineData(10, 0, 1)]
    [InlineData(0, 10, 0)]
    public void EvenlySpaced_BadRange_IsEmpty(double from, double to, double step)
    {
        Assert.Empty(BarLayout.EvenlySpaced(from, to, step));
    }
}
