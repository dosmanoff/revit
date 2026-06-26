using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallReinforcement.Config;
using WallReinforcement.Domain;

namespace WallReinforcement.Engine;

/// <summary>
/// L-shaped continuity bars at wall-to-wall L-corners.
///
/// One L-bar per face per height step: two legs of length <see cref="CornersConfig.LapLength"/>,
/// one along our wall going away from the joint, one along the other wall going away from the joint.
/// Both legs are inset by the appropriate face cover.
///
/// Owner-by-min-ElementId: when both corner walls are in the same run, only the wall with the
/// smaller Id places the bars so we never duplicate.
/// </summary>
public class CornerBarBuilder
{
    private readonly Document _doc;

    public CornerBarBuilder(Document doc) => _doc = doc;

    public int Build(WallAxes axes, ReinforcementConfig cfg, IReadOnlyList<WallJunction> junctions, string tag)
    {
        if (!cfg.Corners.Enabled) return 0;
        ElementId barTypeId = RebarFactory.LookupBarType(_doc, cfg.Corners.BarType);
        if (barTypeId == ElementId.InvalidElementId) return 0;

        // Corner L-bars lap the two walls' mats: ACI mode sizes each leg as the Class B tension
        // lap splice ℓst for the bar; otherwise the configured lap length.
        double lap         = cfg.LapFeet(cfg.Corners.BarType, cfg.Ft(cfg.Corners.LapLength));
        double spacing     = cfg.Ft(cfg.Corners.Spacing);
        double topCover    = cfg.Ft(cfg.Cover.Top);
        double bottomCover = cfg.Ft(cfg.Cover.Bottom);
        double extCover    = cfg.Ft(cfg.Cover.Exterior);
        double intCover    = cfg.Ft(cfg.Cover.Interior);

        var (vCount, vSpacing, vFirst) = RebarFactory.UniformLayout(bottomCover, axes.Height - topCover, spacing);
        if (vCount == 0) return 0;

        int count = 0;
        foreach (WallJunction j in junctions)
        {
            if (j.Kind != JunctionKind.LCorner) continue;
            if (axes.Wall.Id.Value > j.OtherWall.Id.Value) continue;

            double ourSign = j.OurU < axes.Length * 0.5 ? +1 : -1;

            // One L-bar SET per face, distributed up the wall height.
            foreach (double faceOffset in new[] {  axes.HalfThickness - extCover,
                                                  -axes.HalfThickness + intCover })
            {
                XYZ p0 = axes.At(j.OurU, vFirst, faceOffset);
                XYZ p1 = axes.At(j.OurU + ourSign * lap, vFirst, faceOffset);
                XYZ jointAtHeight = new(j.Point.X, j.Point.Y, axes.Origin.Z + vFirst);
                XYZ p2 = jointAtHeight + j.OtherDir * lap;

                RebarFactory.CreateSet(_doc, RebarStyle.Standard, barTypeId, axes.Wall, axes.HeightDir,
                    new List<Curve> { Line.CreateBound(p1, p0), Line.CreateBound(p0, p2) },
                    vCount, vSpacing, tag);
                count += vCount;
            }
        }

        return count;
    }
}
