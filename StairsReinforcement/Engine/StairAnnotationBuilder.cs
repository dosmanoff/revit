using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StairsReinforcement.Config;
using View = Autodesk.Revit.DB.View;

namespace StairsReinforcement.Engine;

/// <summary>
/// Annotates a stair view: a rebar tag on each of this stair's own sets, and (best-effort) a
/// <see cref="MultiReferenceAnnotation"/> spacing dimension on each distributed set whose bars spread
/// within the view plane. Tag/MRA types are resolved by config name with sensible 435E defaults; anything
/// missing or rejected is skipped (it never throws the view run). Tagging/MRA has no sibling-plugin
/// precedent, so this is written against the reflected Revit API:
/// <list type="bullet">
/// <item><c>IndependentTag.Create(doc, symId, viewId, Reference, addLeader, TagOrientation, pnt)</c></item>
/// <item><c>new MultiReferenceAnnotationOptions(MultiReferenceAnnotationType)</c> + DimensionLine
///   origin/direction/normal + tag, then <c>MultiReferenceAnnotation.Create(doc, viewId, options)</c>
///   — Revit dimensions the rebar the line crosses.</item>
/// </list>
/// </summary>
public sealed class StairAnnotationBuilder
{
    private readonly Document _doc;
    private readonly StairViewsConfig _cfg;

    public StairAnnotationBuilder(Document doc, StairViewsConfig cfg)
    {
        _doc = doc;
        _cfg = cfg;
    }

    /// <summary>Annotate one view; returns the number of annotations placed.</summary>
    public int Annotate(View view, long stairId)
    {
        List<Rebar> sets = OwnSets(view, stairId);
        if (sets.Count == 0) return 0;

        FamilySymbol? tagType = _cfg.CreateTags ? ResolveRebarTag() : null;
        ElementId mraTypeId = _cfg.CreateSpacingAnnotations ? ResolveMraType() : ElementId.InvalidElementId;
        if (tagType is null && mraTypeId == ElementId.InvalidElementId && !_cfg.CreateBendingDetails) return 0;

        int placed = 0;
        for (int i = 0; i < sets.Count; i++)
        {
            Rebar set = sets[i];
            if (tagType is not null && TryTag(view, set, tagType, i)) placed++;
            if (mraTypeId != ElementId.InvalidElementId && TrySpacing(view, set, mraTypeId)) placed++;
        }

        // A bending detail per unique Schedule Mark, in sections only (they don't read on a plan).
        if (_cfg.CreateBendingDetails && view is ViewSection && ResolveBendingDetailType() is { } bdType)
            placed += PlaceBendingDetails(view, sets, bdType);

        return placed;
    }

    private List<Rebar> OwnSets(View view, long stairId)
    {
        string id = stairId.ToString();
        var list = new List<Rebar>();
        foreach (Element e in new FilteredElementCollector(_doc, view.Id)
                     .OfCategory(BuiltInCategory.OST_Rebar).WhereElementIsNotElementType())
        {
            if (e is not Rebar rb) continue;
            string[] p = (e.LookupParameter("Comments")?.AsString() ?? "").Split(':');
            if (p.Length >= 3 && p[0] == "STR" && p[2] == id) list.Add(rb);
        }
        return list;
    }

    /// <summary>
    /// Tag the set via a mid-bar SUBELEMENT reference — a plain <c>new Reference(rebar)</c> is rejected
    /// ("the reference can not be tagged"); a subelement (one bar of the set) is the taggable reference, and
    /// a quantity/type/spacing tag on it reads the whole set. Heads are fanned to one side (stepped by
    /// index) to limit overlap.
    /// </summary>
    private bool TryTag(View view, Rebar set, FamilySymbol tagType, int index)
    {
        try
        {
            IList<Subelement> subs = set.GetSubelements();
            if (subs is null || subs.Count == 0) return false;
            Reference r = subs[subs.Count / 2].GetReference();

            BoundingBoxXYZ? bb = set.get_BoundingBox(view);
            XYZ anchor = bb is null ? XYZ.Zero : (bb.Min + bb.Max) * 0.5;
            double step = Math.Max(1.0, view.Scale / 16.0);
            XYZ head = anchor + view.RightDirection * (step * 2) + view.UpDirection * (step * (index - 0.5));
            IndependentTag.Create(_doc, tagType.Id, view.Id, r, addLeader: true, TagOrientation.Horizontal, head);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Place a spacing MRA (tag + dimension) on the set, only when its bar-to-bar axis lies in the view
    /// plane (a set distributed into the page can't read as a dimension). Two hard-won points:
    /// <list type="bullet">
    /// <item><see cref="MultiReferenceAnnotationOptions.SetElementsToDimension"/> is MANDATORY — geometry-only
    ///   options auto-detect nothing and Create throws "no elements / category mismatch".</item>
    /// <item><c>GetBarPositionTransform(i).Origin</c> is relative to bar 0, so it's a valid world DISPLACEMENT
    ///   but NOT a world point — anchor the line from the set's world <c>get_BoundingBox</c> centre.</item>
    /// </list>
    /// </summary>
    private bool TrySpacing(View view, Rebar set, ElementId mraTypeId)
    {
        try
        {
            int n = set.NumberOfBarPositions;
            if (n < 2) return false;
            RebarShapeDrivenAccessor acc = set.GetShapeDrivenAccessor();
            if (acc is null) return false;

            // (n-1) transform is relative to bar 0, so its Origin IS the world displacement bar0 → barN.
            XYZ along = acc.GetBarPositionTransform(n - 1).Origin;
            if (along.GetLength() < 1e-3) return false;
            XYZ dir = along.Normalize();

            XYZ viewN = view.ViewDirection.Normalize();
            if (Math.Abs(dir.DotProduct(viewN)) > 0.35) return false;          // distributed into the page

            XYZ dirInPlane = dir - viewN * dir.DotProduct(viewN);              // exactly ⟂ the plane normal
            if (dirInPlane.GetLength() < 1e-6) return false;
            dirInPlane = dirInPlane.Normalize();
            XYZ inPlanePerp = viewN.CrossProduct(dirInPlane).Normalize();

            BoundingBoxXYZ? bb = set.get_BoundingBox(view);                    // WORLD position of the bars
            if (bb is null) return false;
            XYZ centre = (bb.Min + bb.Max) * 0.5;
            double off = Math.Max(1.0, view.Scale / 12.0);

            var opt = new MultiReferenceAnnotationOptions(
                (MultiReferenceAnnotationType)_doc.GetElement(mraTypeId))
            {
                DimensionLineOrigin = centre + inPlanePerp * off,
                DimensionLineDirection = dirInPlane,
                DimensionPlaneNormal = viewN,
                TagHasLeader = true,
                TagHeadPosition = centre + inPlanePerp * (off * 2),
            };
            try { opt.DimensionStyleType = DimensionStyleType.Linear; } catch { }
            opt.SetElementsToDimension(new List<ElementId> { set.Id });

            MultiReferenceAnnotation.Create(_doc, view.Id, opt);
            return true;
        }
        catch { return false; }
    }

    private FamilySymbol? ResolveRebarTag()
    {
        List<FamilySymbol> tags = new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .Where(s => s.Category != null && s.Category.Id.Value == (long)BuiltInCategory.OST_RebarTags).ToList();
        if (tags.Count == 0) return null;

        FamilySymbol? hit = _cfg.RebarTagTypeName is not null
            ? tags.FirstOrDefault(s => string.Equals(s.Name, _cfg.RebarTagTypeName, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals($"{s.Family.Name} : {s.Name}", _cfg.RebarTagTypeName, StringComparison.OrdinalIgnoreCase))
            : null;
        hit ??= tags.FirstOrDefault(s => s.Name.IndexOf("Spacing", StringComparison.OrdinalIgnoreCase) >= 0) ?? tags[0];

        if (!hit.IsActive) { hit.Activate(); _doc.Regenerate(); }
        return hit;
    }

    private ElementId ResolveMraType()
    {
        List<ElementType> types = new FilteredElementCollector(_doc)
            .OfClass(typeof(MultiReferenceAnnotationType)).Cast<ElementType>().ToList();
        if (types.Count == 0) return ElementId.InvalidElementId;

        ElementType? hit = _cfg.MraTypeName is not null
            ? types.FirstOrDefault(t => string.Equals(t.Name, _cfg.MraTypeName, StringComparison.OrdinalIgnoreCase))
            : null;
        hit ??= types.FirstOrDefault(t => t.Name.IndexOf("Slabs", StringComparison.OrdinalIgnoreCase) >= 0) ?? types[0];
        return hit.Id;
    }

    private RebarBendingDetailType? ResolveBendingDetailType()
    {
        List<RebarBendingDetailType> types = new FilteredElementCollector(_doc)
            .OfClass(typeof(RebarBendingDetailType)).Cast<RebarBendingDetailType>().ToList();
        if (types.Count == 0) return null;
        return (_cfg.BendingDetailTypeName is not null
            ? types.FirstOrDefault(t => string.Equals(t.Name, _cfg.BendingDetailTypeName, StringComparison.OrdinalIgnoreCase))
            : null) ?? types[0];
    }

    /// <summary>
    /// One bending-detail diagram per unique Schedule Mark, stacked in a column down the right edge of the
    /// section crop. Hosted on the mark's representative bar at bar index 0 (the 0-BASED key — the 2000000
    /// subelement form is rejected). <see cref="RebarBendingDetail"/>.Create is its own API: the type is a
    /// <see cref="RebarBendingDetailType"/>, not a <see cref="FamilySymbol"/>, so it is not an IndependentTag.
    /// </summary>
    private int PlaceBendingDetails(View view, List<Rebar> sets, RebarBendingDetailType bdType)
    {
        List<Rebar> reps = sets
            .GroupBy(r => r.LookupParameter("Schedule Mark")?.AsString() ?? "")
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .OrderBy(g => g.Key)
            .Select(g => g.First())
            .ToList();
        if (reps.Count == 0) return 0;

        BoundingBoxXYZ cb = view.CropBox;
        Transform t = cb.Transform;
        double x = cb.Max.X - 2.0, yTop = cb.Max.Y - 1.5;
        double dy = (cb.Max.Y - cb.Min.Y - 3.0) / Math.Max(1, reps.Count);

        int placed = 0;
        for (int k = 0; k < reps.Count; k++)
        {
            try
            {
                XYZ pos = t.OfPoint(new XYZ(x, yTop - k * dy, 0));
                RebarBendingDetail.Create(_doc, view.Id, reps[k].Id, 0, bdType, pos, 0.0);
                placed++;
            }
            catch { }
        }
        return placed;
    }
}
