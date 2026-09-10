using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallReinforcement.Config;
using WallReinforcement.Domain;

namespace WallReinforcement.Engine;

/// <summary>
/// At an L-corner the two walls' end U-bars ("пэшки") interlock into a closed loop, and the corner
/// behaves like a small tied column. This builder places the FOUR vertical corner bars at the
/// loop corners (each inset by cover from both walls' faces). The field-mesh verticals inside the
/// loop are suppressed by <see cref="FaceBarBuilder"/> so only these clean corner bars remain.
///
/// Owner-by-min-ElementId: only the wall with the smaller Id places the corner bars at a shared
/// corner so they are not duplicated.
/// </summary>
public class CornerColumnBuilder
{
    private readonly Document _doc;

    public CornerColumnBuilder(Document doc) => _doc = doc;

    public int Build(WallAxes axes, ReinforcementConfig cfg, IReadOnlyList<WallJunction> junctions, string tag)
    {
        string barName = cfg.FaceMesh.Exterior?.Vertical.BarType ?? cfg.FaceMesh.Interior?.Vertical.BarType ?? "";
        ElementId barTypeId = RebarFactory.LookupBarType(_doc, barName);
        if (barTypeId == ElementId.InvalidElementId) return 0;

        double bottomCover = cfg.Ft(cfg.Cover.Bottom);
        double topCover    = cfg.Ft(cfg.Cover.Top);
        double cover       = cfg.Ft(cfg.Cover.Exterior);
        double zBot = axes.Origin.Z + bottomCover;
        double zTop = axes.Origin.Z + axes.Height - topCover;

        int count = 0;
        foreach (WallJunction j in junctions)
        {
            if (j.Kind != JunctionKind.LCorner) continue;
            if (axes.Wall.Id.Value > j.OtherWall.Id.Value) continue;   // owner = smaller Id

            WallAxes other = WallAxes.For(j.OtherWall);
            double ourInset   = axes.HalfThickness  - cover;
            double otherInset = other.HalfThickness - cover;
            XYZ jointXY = new(j.Point.X, j.Point.Y, 0);

            // Four loop corners = our two cover faces × the other wall's two cover faces.
            foreach (double sA in new[] { +1.0, -1.0 })
            foreach (double sB in new[] { +1.0, -1.0 })
            {
                XYZ plan = jointXY + axes.Normal * (sA * ourInset) + other.Normal * (sB * otherInset);
                XYZ p0 = new(plan.X, plan.Y, zBot);
                XYZ p1 = new(plan.X, plan.Y, zTop);
                RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, axes.Wall, axes.Normal,
                                    new List<Curve> { Line.CreateBound(p0, p1) }, tag);
                count++;
            }
        }

        return count;
    }
}
