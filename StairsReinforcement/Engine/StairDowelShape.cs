using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace StairsReinforcement.Engine;

/// <summary>
/// Authors a clean 2-segment <see cref="RebarShape"/> (segments named <c>A</c> and <c>B</c>, the bend
/// angle baked, legs free) for connection dowels, so the bending schedule shows standard A/B dimensions
/// instead of Revit's auto-generated per-bar shapes — whose second leg lands on a stray <c>ADSK_*</c>
/// shared parameter and proliferate one throwaway shape per bar.
/// <para>
/// Call <see cref="Ensure"/> BEFORE creating the dowel set: with the clean shape already present and no
/// competing auto-shape for that angle yet, <c>CreateFromCurves(useExistingShapeIfPossible:true)</c> matches
/// it exactly (no snap), so every dowel of a given bend angle shares one shape. One shape per distinct
/// rounded bend angle ("STR Dowel 55", "STR Dowel 34", …).
/// </para>
/// Best-effort: if the project has no <c>A</c>/<c>B</c> rebar-dimension shared parameters (the convention
/// this matches), it no-ops and the dowel falls back to an auto shape. Must run inside an open transaction.
/// </summary>
internal static class StairDowelShape
{
    /// <summary>Ensure a clean A/B shape exists for the bend angle of this 2-leg dowel; no-op otherwise.</summary>
    public static void Ensure(Document doc, IList<Curve> curves, XYZ normal)
    {
        if (curves.Count != 2 || curves[0] is not Line l0 || curves[1] is not Line l1) return;

        ElementId pA = FindDim(doc, "A");
        ElementId pB = FindDim(doc, "B");
        if (pA == ElementId.InvalidElementId || pB == ElementId.InvalidElementId) return;

        XYZ d0 = (l0.GetEndPoint(1) - l0.GetEndPoint(0)).Normalize();
        XYZ d1 = (l1.GetEndPoint(1) - l1.GetEndPoint(0)).Normalize();
        double turnDeg = d0.AngleTo(d1) * 180.0 / Math.PI;
        // Name by the bend angle to 0.1° — one shape per distinct flight slope (e.g. 34.3° vs 33.7° must
        // stay separate, or near-equal slopes would snap to one baked angle and tilt off the soffit).
        string name = $"STR Dowel {Math.Round(turnDeg, 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        if (Exists(doc, name)) return;

        // 2-D frame of the bend plane: X along leg 0, Y = normal × X. leg 1 reads as (s1x, s1y).
        XYZ x = d0, y = normal.CrossProduct(x).Normalize();
        double s1x = d1.DotProduct(x), s1y = d1.DotProduct(y);
        RebarShapeVertexTurn turn = s1y >= 0 ? RebarShapeVertexTurn.Left : RebarShapeVertexTurn.Right;
        RebarShapeBendAngle bend = turnDeg < 89 ? RebarShapeBendAngle.Acute
                                 : turnDeg > 91 ? RebarShapeBendAngle.Obtuse
                                 : RebarShapeBendAngle.Right;

        var def = new RebarShapeDefinitionBySegments(doc, 2);
        def.AddParameter(pA, l0.Length);
        def.AddParameter(pB, l1.Length);
        def.SetSegmentFixedDirection(0, 1, 0);
        def.AddConstraintParallelToSegment(0, pA, false, false);
        def.SetSegmentFixedDirection(1, s1x, s1y);
        def.AddConstraintParallelToSegment(1, pB, false, false);
        def.AddBendDefaultRadius(1, turn, bend);

        RebarShape shape = RebarShape.Create(doc, def, null, RebarStyle.Standard,
            StirrupTieAttachmentType.InteriorFace,
            0, RebarHookOrientation.Right, 0, RebarHookOrientation.Right, 0);
        try { shape.Name = name; } catch { /* name clash — keep the auto name */ }
    }

    private static ElementId FindDim(Document doc, string name) =>
        new FilteredElementCollector(doc).OfClass(typeof(SharedParameterElement)).Cast<SharedParameterElement>()
            .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal))?.Id
        ?? ElementId.InvalidElementId;

    private static bool Exists(Document doc, string name) =>
        new FilteredElementCollector(doc).OfClass(typeof(RebarShape)).Cast<RebarShape>()
            .Any(s => string.Equals(s.Name, name, StringComparison.Ordinal));
}
