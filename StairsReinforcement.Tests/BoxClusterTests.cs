using StairsReinforcement.Geometry;
using Xunit;

namespace StairsReinforcement.Tests;

public class BoxClusterTests
{
    private static BoxCluster.Box B(double x0, double y0, double z0, double x1, double y1, double z1)
        => new(new Pt3(x0, y0, z0), new Pt3(x1, y1, z1));

    [Fact]
    public void TwoFarApartBoxes_AreTwoGroups()
    {
        var boxes = new[] { B(0, 0, 0, 1, 1, 1), B(50, 0, 0, 51, 1, 1) };
        var groups = BoxCluster.Group(boxes, gap: 0.5);
        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void OverlappingBoxes_AreOneGroup()
    {
        var boxes = new[] { B(0, 0, 0, 2, 2, 2), B(1, 1, 1, 3, 3, 3) };
        var groups = BoxCluster.Group(boxes, gap: 0.0);
        Assert.Single(groups);
        Assert.Equal(new[] { 0, 1 }, groups[0]);
    }

    [Fact]
    public void Chain_TransitivelyConnected_IsOneGroup()
    {
        // flight → landing → flight: ends 0 and 2 don't touch, but both touch landing 1.
        var boxes = new[]
        {
            B(0, 0, 0, 4, 3, 1),     // flight (bottom)
            B(4, 0, 1, 7, 3, 2),     // landing, overlaps both flights at the joint
            B(4, 0, 2, 8, 3, 3),     // flight (top)
        };
        var groups = BoxCluster.Group(boxes, gap: 0.1);
        Assert.Single(groups);
        Assert.Equal(3, groups[0].Count);
    }

    [Fact]
    public void GapThreshold_IsRespected()
    {
        var near = new[] { B(0, 0, 0, 1, 1, 1), B(1.3, 0, 0, 2, 1, 1) }; // 0.3 apart
        Assert.Single(BoxCluster.Group(near, gap: 0.5));   // within gap → joined
        Assert.Equal(2, BoxCluster.Group(near, gap: 0.1).Count); // beyond gap → split
    }

    [Fact]
    public void Empty_ReturnsNoGroups()
    {
        Assert.Empty(BoxCluster.Group(System.Array.Empty<BoxCluster.Box>(), gap: 0.5));
    }
}
