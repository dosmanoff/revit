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

        double lap         = cfg.Ft(cfg.TJunctions.LapLength);
        double spacing     = cfg.Ft(cfg.TJunctions.Spacing);
        double topCover    = cfg.Ft(cfg.Cover.Top);
        double bottomCover = cfg.Ft(cfg.Cover.Bottom);
        double extCover    = cfg.Ft(cfg.Cover.Exterior);
        double intCover    = cfg.Ft(cfg.Cover.Interior);

        int count = 0;
        int heightStep = 0;
        foreach (WallJunction j in junctions)
        {
            if (j.Kind != JunctionKind.TStem) continue;

            double ourSign = j.OurU < axes.Length * 0.5 ? +1 : -1;

            foreach (double v in RebarFactory.EvenlySpaced(bottomCover, axes.Height - topCover, spacing))
            foreach (double faceOffset in new[] {  axes.HalfThickness - extCover,
                                                  -axes.HalfThickness + intCover })
            {
                heightStep++;
                XYZ p0    = axes.At(j.OurU, v, faceOffset);
                XYZ pStem = axes.At(j.OurU + ourSign * lap, v, faceOffset);

                double thruSign = heightStep % 2 == 0 ? +1 : -1;
                XYZ jointAtHeight = new(j.Point.X, j.Point.Y, axes.Origin.Z + v);
                XYZ pThru = jointAtHeight + j.OtherDir * (thruSign * lap);

                RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, axes.Wall, axes.HeightDir,
                    new List<Curve> { Line.CreateBound(pStem, p0), Line.CreateBound(p0, pThru) }, tag);
                count++;
            }
        }

        return count;
    }
}
