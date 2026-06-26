using WallReinforcement.Geometry;
using Xunit;

namespace WallReinforcement.Tests;

public class OpeningTopMergeTests
{
    [Fact]
    public void Fires_WhenStripShorterThanCombinedLegs()
        => Assert.True(OpeningTopMerge.Fires(stripClearFt: 1.4, legUpFt: 1.4, legDownFt: 1.4));

    [Fact]
    public void DoesNotFire_WhenStripTallerThanCombinedLegs()
        => Assert.False(OpeningTopMerge.Fires(stripClearFt: 3.0, legUpFt: 1.4, legDownFt: 1.4));

    [Fact]
    public void Boundary_StripEqualsCombinedLegs_Fires()
        => Assert.True(OpeningTopMerge.Fires(stripClearFt: 2.8, legUpFt: 1.4, legDownFt: 1.4));

    [Theory]
    [InlineData(0.0)]     // opening top at the wall-top cover line
    [InlineData(-0.5)]    // opening above the wall top (degenerate)
    public void DoesNotFire_ForNonPositiveStrip(double strip)
        => Assert.False(OpeningTopMerge.Fires(strip, 1.4, 1.4));
}
