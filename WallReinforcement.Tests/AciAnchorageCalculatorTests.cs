using System;
using WallReinforcement.Geometry;
using Xunit;

namespace WallReinforcement.Tests;

public class AciAnchorageCalculatorTests
{
    private static AciAnchorageCalculator.Inputs In(
        string bar, double fc = 4000, double fy = 60000,
        bool top = false, bool epoxy = false, bool light = false, bool adequate = true) => new()
    {
        BarSize = bar, FcPsi = fc, FyPsi = fy,
        IsTopBar = top, IsEpoxyCoated = epoxy, IsLightweight = light, AdequateSpacing = adequate,
    };

    [Theory]
    [InlineData("#3", 0.375)]
    [InlineData("#5", 0.625)]
    [InlineData("#8", 1.000)]
    [InlineData("#11", 1.410)]
    public void DiameterIn_Astm(string bar, double db) => Assert.Equal(db, AciAnchorageCalculator.DiameterIn(bar), 3);

    [Fact]
    public void DiameterIn_UnknownBar_Throws() =>
        Assert.Throws<ArgumentException>(() => AciAnchorageCalculator.DiameterIn("Ø12"));

    // ℓd = (fy·ψt·ψe·ψg)/(divisor·λ·√f'c) · db, min 12".
    [Theory]
    [InlineData("#5", 4000, 60000, 23.72)]   // small bar → divisor 25
    [InlineData("#5", 5000, 60000, 21.21)]
    [InlineData("#8", 4000, 60000, 47.43)]   // large bar → divisor 20
    public void DevelopmentLength_BottomBar(string bar, double fc, double fy, double expectedIn)
    {
        double ld = AciAnchorageCalculator.DevelopmentLengthTensionIn(In(bar, fc, fy));
        Assert.Equal(expectedIn, ld, 1);
    }

    [Fact]
    public void DevelopmentLength_TopBarIsLonger()
    {
        double bottom = AciAnchorageCalculator.DevelopmentLengthTensionIn(In("#5"));
        double top = AciAnchorageCalculator.DevelopmentLengthTensionIn(In("#5", top: true));
        Assert.True(top > bottom);
        Assert.Equal(bottom * 1.3, top, 1);   // ψt = 1.3
    }

    [Fact]
    public void DevelopmentLength_EpoxyTopCappedAt1_7()
    {
        // ψt·ψe would be 1.3·1.5 = 1.95 but is capped at 1.7 (§25.4.2.5).
        double plain = AciAnchorageCalculator.DevelopmentLengthTensionIn(In("#5"));
        double topEpoxy = AciAnchorageCalculator.DevelopmentLengthTensionIn(In("#5", top: true, epoxy: true));
        Assert.Equal(plain * 1.7, topEpoxy, 1);
    }

    [Fact]
    public void DevelopmentLength_HasTwelveInchFloor()
    {
        // A small bar in very strong concrete drops below 12" → clamped to 12".
        double ld = AciAnchorageCalculator.DevelopmentLengthTensionIn(In("#3", fc: 10000));
        Assert.Equal(12.0, ld, 3);
    }

    [Fact]
    public void DevelopmentLength_InadequateSpacingIsLonger()
    {
        double adequate = AciAnchorageCalculator.DevelopmentLengthTensionIn(In("#5", adequate: true));
        double tight = AciAnchorageCalculator.DevelopmentLengthTensionIn(In("#5", adequate: false));
        Assert.True(tight > adequate);
    }

    [Fact]
    public void TensionLapSplice_IsClassB_1_3_Ld()
    {
        double ld = AciAnchorageCalculator.DevelopmentLengthTensionIn(In("#5"));
        double lap = AciAnchorageCalculator.TensionLapSpliceClassBIn(In("#5"));
        Assert.Equal(Math.Max(12.0, 1.3 * ld), lap, 3);
    }

    [Theory]
    [InlineData(0, 60000)]
    [InlineData(4000, 0)]
    public void DevelopmentLength_NonPositiveInputs_Throw(double fc, double fy) =>
        Assert.Throws<ArgumentException>(() => AciAnchorageCalculator.DevelopmentLengthTensionIn(In("#5", fc, fy)));
}
