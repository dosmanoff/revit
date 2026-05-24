using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallReinforcement.Config;
using WallReinforcement.Domain;

namespace WallReinforcement.Engine;

/// <summary>
/// L-shaped continuity bars at wall-to-wall L-corners.
///
/// One L-bar per face per height step: two legs of length <see cref="CornersConfig.LapLengthMm"/>,
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

        double lap         = UnitConv.MmToFt(cfg.Corners.LapLengthMm);
        double spacing     = UnitConv.MmToFt(cfg.Corners.SpacingMm);
        double topCover    = UnitConv.MmToFt(cfg.Cover.TopMm);
        double bottomCover = UnitConv.MmToFt(cfg.Cover.BottomMm);
        double extCover    = UnitConv.MmToFt(cfg.Cover.ExteriorMm);
        double intCover    = UnitConv.MmToFt(cfg.Cover.InteriorMm);

        int count = 0;
        foreach (WallJunction j in junctions)
        {
            if (j.Kind != JunctionKind.LCorner) continue;
            if (axes.Wall.Id.Value > j.OtherWall.Id.Value) continue;

            double ourSign = j.OurU < axes.Length * 0.5 ? +1 : -1;

            foreach (double v in RebarFactory.EvenlySpaced(bottomCover, axes.Height - topCover, spacing))
            foreach (double faceOffset in new[] {  axes.HalfThickness - extCover,
                                                  -axes.HalfThickness + intCover })
            {
                XYZ p0 = axes.At(j.OurU, v, faceOffset);
                XYZ p1 = axes.At(j.OurU + ourSign * lap, v, faceOffset);
                XYZ jointAtHeight = new(j.Point.X, j.Point.Y, axes.Origin.Z + v);
                XYZ p2 = jointAtHeight + j.OtherDir * lap;

                RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, axes.Wall, axes.HeightDir,
                    new List<Curve> { Line.CreateBound(p1, p0), Line.CreateBound(p0, p2) }, tag);
                count++;
            }
        }

        return count;
    }
}
