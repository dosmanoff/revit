using WallReinforcement.Geometry;
using Xunit;

namespace WallReinforcement.Tests;

public class ElevationProfileTests
{
    private static ElevationProfile Rect(double w, double h) => new(new[]
    {
        new UVPoint(0, 0), new UVPoint(w, 0), new UVPoint(w, h), new UVPoint(0, h),
    });

    [Fact]
    public void Rectangle_FullSpans_AndIsRect()
    {
        var p = Rect(16, 10);
        Assert.True(p.IsAxisAlignedRect());
        var vs = p.VerticalSpansAt(8);
        Assert.Single(vs);
        Assert.Equal(new Interval(0, 10), vs[0]);
        var hs = p.HorizontalSpansAt(5);
        Assert.Equal(new Interval(0, 16), hs[0]);
    }

    [Fact]
    public void SlantedTopEnd_ClipsVerticalSpan()
    {
        // 16 wide, 10 tall, but the top-right corner is cut down to v=4 at u=16 (slanted top end).
        var p = new ElevationProfile(new[]
        {
            new UVPoint(0, 0), new UVPoint(16, 0), new UVPoint(16, 4), new UVPoint(8, 10), new UVPoint(0, 10),
        });
        Assert.False(p.IsAxisAlignedRect());
        // Far from the slope the wall is full height.
        Assert.Equal(10.0, p.VerticalSpansAt(2)[0].To, 6);
        // At the slanted end the top is lower (the cut corner), v_top < 10.
        double topAtEnd = p.VerticalSpansAt(14)[0].To;
        Assert.True(topAtEnd < 10.0 && topAtEnd > 4.0, $"expected clipped top, got {topAtEnd}");
        // Right at u=16 the wall top is v=4.
        Assert.Equal(4.0, p.VerticalSpansAt(15.99)[0].To, 1);
    }

    [Fact]
    public void HorizontalSpan_ShrinksNearSlantedEnd()
    {
        var p = new ElevationProfile(new[]
        {
            new UVPoint(0, 0), new UVPoint(16, 0), new UVPoint(16, 4), new UVPoint(8, 10), new UVPoint(0, 10),
        });
        // Low down the wall spans the full 0..16; high up the slope cuts the right end short.
        Assert.Equal(16.0, p.HorizontalSpansAt(2)[0].To, 6);
        double hiEnd = p.HorizontalSpansAt(8)[0].To;
        Assert.True(hiEnd < 16.0, $"expected shortened top run, got {hiEnd}");
    }
}
