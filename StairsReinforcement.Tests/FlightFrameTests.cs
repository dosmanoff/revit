using StairsReinforcement.Geometry;
using Xunit;

namespace StairsReinforcement.Tests;

public class FlightFrameTests
{
    private const double Tol = 1e-9;

    [Fact]
    public void Axes_AreOrthonormal_AndRightHanded()
    {
        var f = FlightFrame.Create(new Pt3(0, 0, 0), new Pt2(1, 0), Math.PI / 6);

        Assert.Equal(1, f.U.Length, 9);
        Assert.Equal(1, f.W.Length, 9);
        Assert.Equal(1, f.N.Length, 9);
        Assert.Equal(0, f.U.Dot(f.W), 9);
        Assert.Equal(0, f.U.Dot(f.N), 9);
        Assert.Equal(0, f.W.Dot(f.N), 9);

        // N = U × W (right-handed)
        Pt3 cross = f.U.Cross(f.W);
        Assert.Equal(cross.X, f.N.X, 9);
        Assert.Equal(cross.Y, f.N.Y, 9);
        Assert.Equal(cross.Z, f.N.Z, 9);
    }

    [Fact]
    public void WaistNormal_PointsUp()
    {
        var f = FlightFrame.Create(new Pt3(0, 0, 0), new Pt2(1, 0), Math.PI / 5);
        Assert.True(f.N.Z > 0);
    }

    [Fact]
    public void WidthAxis_IsHorizontal()
    {
        var f = FlightFrame.Create(new Pt3(0, 0, 0), new Pt2(0.7, 0.7), Math.PI / 4);
        Assert.Equal(0, f.W.Z, 9);
    }

    [Fact]
    public void UpSlopeAxis_RisesWithSlope()
    {
        // 30° pitch climbing +X: U should be (cos30, 0, sin30).
        var f = FlightFrame.Create(new Pt3(0, 0, 0), new Pt2(1, 0), Math.PI / 6);
        Assert.Equal(Math.Cos(Math.PI / 6), f.U.X, 9);
        Assert.Equal(0, f.U.Y, 9);
        Assert.Equal(Math.Sin(Math.PI / 6), f.U.Z, 9);
    }

    [Fact]
    public void FromRiseRun_RecoversSlope()
    {
        // rise 6, run 9 ⇒ slope atan2(6,9)
        var f = FlightFrame.FromRiseRun(new Pt3(0, 0, 0), new Pt2(1, 0), horizRun: 9, rise: 6);
        Assert.Equal(Math.Atan2(6, 9), f.SlopeRad, 9);
    }

    [Fact]
    public void At_PlacesPointAlongAxes()
    {
        var f = FlightFrame.Create(new Pt3(1, 2, 3), new Pt2(1, 0), 0); // flat
        Pt3 p = f.At(10, 4, 0.5);
        Assert.Equal(11, p.X, Tol);   // along U(=+X)
        Assert.Equal(6, p.Y, Tol);    // along W(=+Y)
        Assert.Equal(3.5, p.Z, Tol);  // along N(=+Z when flat)
    }
}
