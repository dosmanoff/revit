using SlabReinforcement.Geometry;
using Xunit;

namespace SlabReinforcement.Tests;

public class AciAnchorageTests
{
    private static AciAnchorageCalculator.Inputs Base(string bar = "#5", bool top = false) => new()
    {
        BarSize = bar, FcPsi = 5000, FyPsi = 60000, IsTopBar = top, AdequateSpacing = true,
    };

    [Fact]
    public void Ld_No5_5000psi_Grade60_Bottom_IsAbout21in()
    {
        // ℓd/db = (60000)/(25·1·√5000) = 33.94 → ×0.625 = 21.2"
        double ld = AciAnchorageCalculator.DevelopmentLengthTensionIn(Base());
        Assert.InRange(ld, 21.0, 21.5);
    }

    [Fact]
    public void ClassBLap_Is_1p3_Times_Ld()
    {
        var i = Base();
        double ld = AciAnchorageCalculator.DevelopmentLengthTensionIn(i);
        double lst = AciAnchorageCalculator.TensionLapSpliceClassBIn(i);
        Assert.Equal(1.3 * ld, lst, 6);
    }

    [Fact]
    public void TopBarFactor_Multiplies_By_1p3()
    {
        double bottom = AciAnchorageCalculator.DevelopmentLengthTensionIn(Base(top: false));
        double top = AciAnchorageCalculator.DevelopmentLengthTensionIn(Base(top: true));
        Assert.Equal(1.3 * bottom, top, 6);
    }

    [Fact]
    public void LimitedSpacing_Is_Longer_Than_Adequate()
    {
        double adequate = AciAnchorageCalculator.DevelopmentLengthTensionIn(Base() with { AdequateSpacing = true });
        double limited = AciAnchorageCalculator.DevelopmentLengthTensionIn(Base() with { AdequateSpacing = false });
        Assert.True(limited > adequate);
    }

    [Fact]
    public void Enforces_12in_Minimum()
    {
        var i = new AciAnchorageCalculator.Inputs { BarSize = "#3", FcPsi = 10000, FyPsi = 60000 };
        Assert.True(AciAnchorageCalculator.DevelopmentLengthTensionIn(i) >= 12.0);
    }

    [Fact]
    public void UnknownBarSize_Throws()
    {
        var i = new AciAnchorageCalculator.Inputs { BarSize = "#99", FcPsi = 5000, FyPsi = 60000 };
        Assert.Throws<System.ArgumentException>(() => AciAnchorageCalculator.DevelopmentLengthTensionIn(i));
    }
}
