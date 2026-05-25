using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallReinforcement.Config;
using WallReinforcement.Domain;

namespace WallReinforcement.Engine;

/// <summary>
/// Places U-shaped bars along the top, bottom, and ends of a wall to tie the two face meshes
/// together at the perimeter (standard practice for monolithic walls).
///
/// Cross-section of the U at a TOP edge (wall length axis goes into the page):
///
///   exterior face                interior face
///         │                            │
///         │  ┌────── top cross ─────┐  │
///         │  │                      │  │
///         │  │  leg                 │  │  leg
///         │  ▼ (legLength)          │  ▼ (legLength)
///
/// Bars are spaced along the edge by <see cref="EdgeConfig.Spacing"/>, leaving the
/// end-cover at each end of the edge.
/// </summary>
public class EdgeBarBuilder
{
    private readonly Document _doc;

    public EdgeBarBuilder(Document doc) => _doc = doc;

    public int Build(WallAxes axes, ReinforcementConfig cfg, string tag)
    {
        int n = 0;
        n += BuildTopOrBottom(axes, cfg, cfg.Edges.Top,    isTop: true,  tag);
        n += BuildTopOrBottom(axes, cfg, cfg.Edges.Bottom, isTop: false, tag);
        n += BuildEnds(axes, cfg, cfg.Edges.Ends, tag);
        return n;
    }

    private int BuildTopOrBottom(WallAxes axes, ReinforcementConfig cfg, EdgeConfig edge, bool isTop, string tag)
    {
        if (!edge.Enabled) return 0;
        ElementId barTypeId = RebarFactory.LookupBarType(_doc, edge.BarType);
        if (barTypeId == ElementId.InvalidElementId) return 0;

        double endsCover = cfg.Ft(cfg.Cover.Ends);
        double spacing   = cfg.Ft(edge.Spacing);
        double legLen    = cfg.Ft(edge.LegLength);
        double extOffset =  axes.HalfThickness - cfg.Ft(cfg.Cover.Exterior);
        double intOffset = -axes.HalfThickness + cfg.Ft(cfg.Cover.Interior);

        double crossV = isTop
            ? axes.Height - cfg.Ft(cfg.Cover.Top)
            : cfg.Ft(cfg.Cover.Bottom);
        double legV = isTop ? crossV - legLen : crossV + legLen;

        int count = 0;
        foreach (double u in RebarFactory.EvenlySpaced(endsCover, axes.Length - endsCover, spacing))
        {
            XYZ p1 = axes.At(u, legV,   extOffset);
            XYZ p2 = axes.At(u, crossV, extOffset);
            XYZ p3 = axes.At(u, crossV, intOffset);
            XYZ p4 = axes.At(u, legV,   intOffset);

            count += PlaceU(axes, barTypeId, tag, normal: axes.LengthDir, p1, p2, p3, p4);
        }

        return count;
    }

    private int BuildEnds(WallAxes axes, ReinforcementConfig cfg, EdgeConfig edge, string tag)
    {
        if (!edge.Enabled) return 0;
        ElementId barTypeId = RebarFactory.LookupBarType(_doc, edge.BarType);
        if (barTypeId == ElementId.InvalidElementId) return 0;

        double topCover    = cfg.Ft(cfg.Cover.Top);
        double bottomCover = cfg.Ft(cfg.Cover.Bottom);
        double endsCover   = cfg.Ft(cfg.Cover.Ends);
        double spacing     = cfg.Ft(edge.Spacing);
        double legLen      = cfg.Ft(edge.LegLength);
        double extOffset   =  axes.HalfThickness - cfg.Ft(cfg.Cover.Exterior);
        double intOffset   = -axes.HalfThickness + cfg.Ft(cfg.Cover.Interior);

        int count = 0;
        foreach (var (uEdge, legSign) in new[] { (endsCover, +1.0), (axes.Length - endsCover, -1.0) })
        {
            double uLeg = uEdge + legSign * legLen;

            foreach (double v in RebarFactory.EvenlySpaced(bottomCover, axes.Height - topCover, spacing))
            {
                XYZ p1 = axes.At(uLeg,  v, extOffset);
                XYZ p2 = axes.At(uEdge, v, extOffset);
                XYZ p3 = axes.At(uEdge, v, intOffset);
                XYZ p4 = axes.At(uLeg,  v, intOffset);

                count += PlaceU(axes, barTypeId, tag, normal: axes.HeightDir, p1, p2, p3, p4);
            }
        }

        return count;
    }

    private int PlaceU(WallAxes axes, ElementId barTypeId, string tag, XYZ normal,
                       XYZ p1, XYZ p2, XYZ p3, XYZ p4)
    {
        var curves = new List<Curve>
        {
            Line.CreateBound(p1, p2),
            Line.CreateBound(p2, p3),
            Line.CreateBound(p3, p4),
        };
        RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, axes.Wall, normal, curves, tag);
        return 1;
    }
}
