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
        // The U-bar leg anchors the bar into the wall: ACI mode sizes it as the tension
        // development length ℓd for the bar; otherwise the configured leg length.
        double legLen    = cfg.DevLengthFeet(edge.BarType, cfg.Ft(edge.LegLength));
        double extOffset =  axes.HalfThickness - cfg.Ft(cfg.Cover.Exterior);
        double intOffset = -axes.HalfThickness + cfg.Ft(cfg.Cover.Interior);

        double crossV = isTop
            ? axes.Height - cfg.Ft(cfg.Cover.Top)
            : cfg.Ft(cfg.Cover.Bottom);
        double legV = isTop ? crossV - legLen : crossV + legLen;

        var (uCount, uSpacing, uFirst) = RebarFactory.UniformLayout(endsCover, axes.Length - endsCover, spacing);
        if (uCount == 0) return 0;

        // One U-bar SET distributed along the wall length.
        XYZ p1 = axes.At(uFirst, legV,   extOffset);
        XYZ p2 = axes.At(uFirst, crossV, extOffset);
        XYZ p3 = axes.At(uFirst, crossV, intOffset);
        XYZ p4 = axes.At(uFirst, legV,   intOffset);
        PlaceUSet(axes, barTypeId, tag, axes.LengthDir, uCount, uSpacing, p1, p2, p3, p4);
        return uCount;
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
        double legLen      = cfg.DevLengthFeet(edge.BarType, cfg.Ft(edge.LegLength));
        double extOffset   =  axes.HalfThickness - cfg.Ft(cfg.Cover.Exterior);
        double intOffset   = -axes.HalfThickness + cfg.Ft(cfg.Cover.Interior);

        var (vCount, vSpacing, vFirst) = RebarFactory.UniformLayout(bottomCover, axes.Height - topCover, spacing);
        if (vCount == 0) return 0;

        int count = 0;
        // One U-bar SET per end, distributed up the wall height.
        foreach (var (uEdge, legSign) in new[] { (endsCover, +1.0), (axes.Length - endsCover, -1.0) })
        {
            double uLeg = uEdge + legSign * legLen;

            XYZ p1 = axes.At(uLeg,  vFirst, extOffset);
            XYZ p2 = axes.At(uEdge, vFirst, extOffset);
            XYZ p3 = axes.At(uEdge, vFirst, intOffset);
            XYZ p4 = axes.At(uLeg,  vFirst, intOffset);

            PlaceUSet(axes, barTypeId, tag, axes.HeightDir, vCount, vSpacing, p1, p2, p3, p4);
            count += vCount;
        }

        return count;
    }

    private void PlaceUSet(WallAxes axes, ElementId barTypeId, string tag, XYZ distributionDir,
                           int count, double spacing, XYZ p1, XYZ p2, XYZ p3, XYZ p4)
    {
        var curves = new List<Curve>
        {
            Line.CreateBound(p1, p2),
            Line.CreateBound(p2, p3),
            Line.CreateBound(p3, p4),
        };
        RebarFactory.CreateSet(_doc, RebarStyle.Standard, barTypeId, axes.Wall, distributionDir,
                               curves, count, spacing, tag);
    }
}
