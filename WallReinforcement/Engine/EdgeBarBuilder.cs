using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallReinforcement.Config;
using WallReinforcement.Domain;

namespace WallReinforcement.Engine;

/// <summary>
/// Places U-shaped bars ("пэшки") along the top, bottom, and ends of a wall to tie the two face
/// mats together at the perimeter (standard practice for monolithic walls).
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
/// At an END that meets another wall (L-corner or T-junction), the end U-bar is the continuity
/// detail: its middle (back) segment is pushed past the joint to the adjoining wall's far-cover
/// line, so the two walls' U-bars interlock and the main bars develop into the joint. Free ends keep
/// the back at end-cover. Bars are spaced along the edge by <see cref="EdgeConfig.Spacing"/>.
/// </summary>
public class EdgeBarBuilder
{
    private readonly Document _doc;

    public EdgeBarBuilder(Document doc) => _doc = doc;

    public int Build(WallAxes axes, ReinforcementConfig cfg, WallLayering lay,
                     IReadOnlyList<WallJunction> junctions, string tag)
    {
        int n = 0;
        n += BuildTopOrBottom(axes, cfg, lay, cfg.Edges.Top,    isTop: true,  tag);
        n += BuildTopOrBottom(axes, cfg, lay, cfg.Edges.Bottom, isTop: false, tag);
        n += BuildEnds(axes, cfg, lay, cfg.Edges.Ends, junctions, tag);
        return n;
    }

    private int BuildTopOrBottom(WallAxes axes, ReinforcementConfig cfg, WallLayering lay, EdgeConfig edge, bool isTop, string tag)
    {
        if (!edge.Enabled) return 0;
        ElementId barTypeId = RebarFactory.LookupBarType(_doc, edge.BarType);
        if (barTypeId == ElementId.InvalidElementId) return 0;

        double endsCover = cfg.Ft(cfg.Cover.Ends);
        double spacing   = cfg.Ft(edge.Spacing);
        // The U-bar leg anchors the bar into the wall: ACI mode sizes it as the tension
        // development length ℓd for the bar; otherwise the configured leg length.
        double legLen    = cfg.DevLengthFeet(edge.BarType, cfg.Ft(edge.LegLength));
        double extOffset = lay.FieldFaceH(true);
        double intOffset = lay.FieldFaceH(false);

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

    private int BuildEnds(WallAxes axes, ReinforcementConfig cfg, WallLayering lay, EdgeConfig edge,
                          IReadOnlyList<WallJunction> junctions, string tag)
    {
        if (!edge.Enabled) return 0;
        ElementId barTypeId = RebarFactory.LookupBarType(_doc, edge.BarType);
        if (barTypeId == ElementId.InvalidElementId) return 0;

        double topCover    = cfg.Ft(cfg.Cover.Top);
        double bottomCover = cfg.Ft(cfg.Cover.Bottom);
        double endsCover   = cfg.Ft(cfg.Cover.Ends);
        double adjCover    = cfg.Ft(cfg.Cover.Exterior);   // cover at the adjoining wall's far face
        double spacing     = cfg.Ft(edge.Spacing);
        double legLen      = cfg.DevLengthFeet(edge.BarType, cfg.Ft(edge.LegLength));
        double extOffset   = lay.FieldFaceH(true);
        double intOffset   = lay.FieldFaceH(false);
        // The back lands a full bar diameter inside the adjoining wall's far-cover line so the bar
        // SURFACE (not centerline) keeps cover — without this the bar sits in the cover zone.
        double barRadius   = _doc.GetElement(barTypeId) is RebarBarType bt ? bt.BarNominalDiameter / 2 : 0;

        var (vCount, vSpacing, vFirst) = RebarFactory.UniformLayout(bottomCover, axes.Height - topCover, spacing);
        if (vCount == 0) return 0;

        int count = 0;
        // One U-bar SET per physical end (u=0 and u=Length), distributed up the wall height.
        foreach (var (endU, outwardSign) in new[] { (0.0, -1.0), (axes.Length, +1.0) })
        {
            // If this end meets another wall, drive the U-bar's back past the joint to the adjoining
            // wall's far-cover line (kept inside by the bar radius); otherwise keep it at end-cover.
            WallJunction? j = junctions.FirstOrDefault(x => Math.Abs(x.OurU - endU) < 1e-3);
            double backU = j is not null
                ? endU + outwardSign * (WallAxes.For(j.OtherWall).HalfThickness - adjCover - barRadius)
                : endU - outwardSign * endsCover;
            double deepU = backU - outwardSign * legLen;   // legs run inward from the back

            XYZ p1 = axes.At(deepU, vFirst, extOffset);
            XYZ p2 = axes.At(backU, vFirst, extOffset);
            XYZ p3 = axes.At(backU, vFirst, intOffset);
            XYZ p4 = axes.At(deepU, vFirst, intOffset);

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
