using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallReinforcement.Config;
using WallReinforcement.Domain;

namespace WallReinforcement.Engine;

/// <summary>
/// L-shaped lap bars at T-junctions where OUR wall is the stem terminating on the through wall.
///
/// One L-bar per face per height step: one leg along our wall going away from the joint
/// (length <see cref="TJunctionsConfig.LapLength"/>), one leg along the through wall.
/// Through-wall direction alternates with the height step to approximate the typical "fan" pattern
/// rather than piling all laps on one side.
/// </summary>
public class TJunctionBarBuilder
{
    private readonly Document _doc;

    public TJunctionBarBuilder(Document doc) => _doc = doc;

    public int Build(WallAxes axes, ReinforcementConfig cfg, IReadOnlyList<WallJunction> junctions, string tag)
    {
        if (!cfg.TJunctions.Enabled) return 0;
        ElementId barTypeId = RebarFactory.LookupBarType(_doc, cfg.TJunctions.BarType);
        if (barTypeId == ElementId.InvalidElementId) return 0;

        // The stem laps into the through wall: ACI mode sizes each leg as the Class B tension lap
        // splice ℓst for the bar; otherwise the configured lap length.
        double lap         = cfg.LapFeet(cfg.TJunctions.BarType, cfg.Ft(cfg.TJunctions.LapLength));
        double spacing     = cfg.Ft(cfg.TJunctions.Spacing);
        double topCover    = cfg.Ft(cfg.Cover.Top);
        double bottomCover = cfg.Ft(cfg.Cover.Bottom);
        double extCover    = cfg.Ft(cfg.Cover.Exterior);
        double intCover    = cfg.Ft(cfg.Cover.Interior);

        var (vCount, vSpacing, vFirst) = RebarFactory.UniformLayout(bottomCover, axes.Height - topCover, spacing);
        if (vCount == 0) return 0;

        // Split the height positions into two interleaved SETS so laps alternate sides of the
        // through wall (the typical "fan"): even rows lap one way, odd rows the other. Each set is
        // spaced at 2x the row pitch; the odd set starts one row up.
        int countEven = (vCount + 1) / 2;
        int countOdd  = vCount / 2;
        double doubleSpacing = vSpacing * 2;

        int count = 0;
        foreach (WallJunction j in junctions)
        {
            if (j.Kind != JunctionKind.TStem) continue;

            double ourSign = j.OurU < axes.Length * 0.5 ? +1 : -1;

            foreach (double faceOffset in new[] {  axes.HalfThickness - extCover,
                                                  -axes.HalfThickness + intCover })
            {
                count += PlaceTSet(axes, barTypeId, tag, j, ourSign, lap, faceOffset,
                                   vFirst, doubleSpacing, countEven, +1);
                if (countOdd > 0)
                    count += PlaceTSet(axes, barTypeId, tag, j, ourSign, lap, faceOffset,
                                       vFirst + vSpacing, doubleSpacing, countOdd, -1);
            }
        }

        return count;
    }

    private int PlaceTSet(WallAxes axes, ElementId barTypeId, string tag, WallJunction j,
                          double ourSign, double lap, double faceOffset,
                          double vStart, double spacing, int count, double thruSign)
    {
        XYZ p0    = axes.At(j.OurU, vStart, faceOffset);
        XYZ pStem = axes.At(j.OurU + ourSign * lap, vStart, faceOffset);
        XYZ jointAtHeight = new(j.Point.X, j.Point.Y, axes.Origin.Z + vStart);
        XYZ pThru = jointAtHeight + j.OtherDir * (thruSign * lap);

        RebarFactory.CreateSet(_doc, RebarStyle.Standard, barTypeId, axes.Wall, axes.HeightDir,
            new List<Curve> { Line.CreateBound(pStem, p0), Line.CreateBound(p0, pThru) },
            count, spacing, tag);
        return count;
    }
}
