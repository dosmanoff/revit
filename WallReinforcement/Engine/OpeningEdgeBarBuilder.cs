using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallReinforcement.Config;
using WallReinforcement.Domain;

namespace WallReinforcement.Engine;

/// <summary>
/// Places U-bars ("пэшки") along the four edges of each wall opening, hooking the two face mats and
/// wrapping the cut ends of the field bars that <see cref="FaceBarBuilder"/> stops at the opening.
/// One leg runs away from the opening into the surrounding wall (anchorage length), the back caps
/// the two face mats at the opening face. Complements the straight trim / diagonal bars from
/// <see cref="OpeningTrimBuilder"/>.
/// </summary>
public class OpeningEdgeBarBuilder
{
    private readonly Document _doc;

    public OpeningEdgeBarBuilder(Document doc) => _doc = doc;

    public int Build(WallAxes axes, ReinforcementConfig cfg, WallLayering lay, ISet<long> mergeOpeningIds, string tag)
    {
        OpeningsConfig op = cfg.Openings;
        if (!op.Enabled) return 0;
        ElementId barTypeId = RebarFactory.LookupBarType(_doc, op.BarType);
        if (barTypeId == ElementId.InvalidElementId) return 0;

        double minWidth = cfg.Ft(op.MinWidth);
        double legLen   = cfg.DevLengthFeet(op.BarType, cfg.Ft(op.Extension));
        double spacing  = cfg.Ft(cfg.FaceMesh.Exterior?.Horizontal.Spacing ?? new Length(200));
        double extOff   = lay.FieldFaceH(true);
        double intOff   = lay.FieldFaceH(false);

        int count = 0;
        foreach (OpeningRect o in WallGeometry.GetOpenings(axes))
        {
            if (o.Width < minWidth) continue;

            // Top edge: skipped when a closed merge stirrup spans the strip above this opening.
            if (!mergeOpeningIds.Contains(o.InsertId.Value))
                count += EdgeU(axes, barTypeId, tag, distAlong: axes.LengthDir, spacing,
                               o.UMin, o.UMax, fixedV: o.VMax, legV: o.VMax + legLen, extOff, intOff, alongU: true);
            // Bottom edge: legs run down away from the opening, distributed along u.
            count += EdgeU(axes, barTypeId, tag, distAlong: axes.LengthDir, spacing,
                           o.UMin, o.UMax, fixedV: o.VMin, legV: o.VMin - legLen, extOff, intOff, alongU: true);
            // Left & right edges: legs run horizontally away from the opening, distributed along v.
            count += EdgeU(axes, barTypeId, tag, distAlong: axes.HeightDir, spacing,
                           o.VMin, o.VMax, fixedV: o.UMin, legV: o.UMin - legLen, extOff, intOff, alongU: false);
            count += EdgeU(axes, barTypeId, tag, distAlong: axes.HeightDir, spacing,
                           o.VMin, o.VMax, fixedV: o.UMax, legV: o.UMax + legLen, extOff, intOff, alongU: false);
        }

        return count;
    }

    /// <summary>One U-bar SET along one opening edge. <paramref name="alongU"/> true ⇒ the edge runs
    /// along the wall length (top/bottom of opening), distributing the U up... handled by the caller's
    /// <c>distAlong</c>; the back sits at <paramref name="fixedV"/> and the leg reaches
    /// <paramref name="legV"/> (both interpreted in the edge's primary axis).</summary>
    private int EdgeU(WallAxes axes, ElementId barTypeId, string tag, XYZ distAlong, double spacing,
                      double from, double to, double fixedV, double legV, double extOff, double intOff, bool alongU)
    {
        var (n, step, first) = RebarFactory.UniformLayout(from, to, spacing);
        if (n == 0) return 0;

        // alongU: edge spans u∈[from,to] at height fixedV; leg goes to height legV.
        // !alongU: edge spans v∈[from,to] at u fixedV; leg goes to u legV.
        XYZ At(double primary, double crossOrLeg, double off) =>
            alongU ? axes.At(primary, crossOrLeg, off) : axes.At(crossOrLeg, primary, off);

        XYZ p1 = At(first, legV,   extOff);
        XYZ p2 = At(first, fixedV, extOff);
        XYZ p3 = At(first, fixedV, intOff);
        XYZ p4 = At(first, legV,   intOff);

        RebarFactory.CreateSet(_doc, RebarStyle.Standard, barTypeId, axes.Wall, distAlong,
            new List<Curve> { Line.CreateBound(p1, p2), Line.CreateBound(p2, p3), Line.CreateBound(p3, p4) },
            n, step, tag);
        return n;
    }
}
