using Autodesk.Revit.DB;
using StairsReinforcement.Geometry;

namespace StairsReinforcement.Domain;

/// <summary>
/// Enriches a resolved <see cref="StairAssembly"/> with adjacency: the support at each flight
/// end (a sibling landing, or a slab/beam/wall/foundation below), the supports under each landing
/// edge, and which flights each landing connects. Uses spatial <see cref="BoundingBoxIntersectsFilter"/>
/// queries — the stair analogue of SlabContext.
/// </summary>
public static class StairContext
{
    private const double PadFt = 1.0;
    private const double SearchDownFt = 3.0;
    private const double SearchUpFt = 0.5;

    public static void Populate(Document doc, StairAssembly asm)
    {
        var exclude = new HashSet<long> { asm.HostElement.Id.Value };
        foreach (FlightComponent f in asm.Flights) exclude.Add(f.ComponentId.Value);
        foreach (LandingComponent l in asm.Landings) exclude.Add(l.ComponentId.Value);

        foreach (FlightComponent f in asm.Flights)
        {
            XYZ lower = RevitGeom.ToXYZ(f.Frame.Origin);
            XYZ upper = RevitGeom.ToXYZ(f.Frame.At(f.SlopeLengthFt, 0, 0));

            f.LowerSupport = SiblingLanding(asm, lower) ?? FindSupportBelow(doc, lower, exclude);
            f.UpperSupport = SiblingLanding(asm, upper) ?? FindSupportBelow(doc, upper, exclude);
        }

        foreach (LandingComponent l in asm.Landings)
        {
            l.Supports = FindLandingSupports(doc, l, exclude);
            l.ConnectsFlights = asm.Flights
                .Where(f => TouchesLanding(l, f))
                .Select(f => f.Index)
                .ToList();
        }
    }

    private static SupportInfo? SiblingLanding(StairAssembly asm, XYZ pt)
    {
        foreach (LandingComponent l in asm.Landings)
        {
            if (l.Bounds.IsEmpty) continue;
            if (pt.X < l.Bounds.Min.X - PadFt || pt.X > l.Bounds.Max.X + PadFt) continue;
            if (pt.Y < l.Bounds.Min.Y - PadFt || pt.Y > l.Bounds.Max.Y + PadFt) continue;
            if (Math.Abs(pt.Z - l.ElevationFt) > SearchDownFt) continue;
            return new SupportInfo { Kind = "landing", ElementId = l.ComponentId.Value, ElevationFt = l.ElevationFt };
        }
        return null;
    }

    private static SupportInfo? FindSupportBelow(Document doc, XYZ pt, HashSet<long> exclude)
    {
        var min = new XYZ(pt.X - PadFt, pt.Y - PadFt, pt.Z - SearchDownFt);
        var max = new XYZ(pt.X + PadFt, pt.Y + PadFt, pt.Z + SearchUpFt);

        SupportInfo? best = null;
        double bestGap = double.MaxValue;

        foreach (Element e in new FilteredElementCollector(doc)
                     .WherePasses(new BoundingBoxIntersectsFilter(new Outline(min, max)))
                     .WhereElementIsNotElementType())
        {
            if (exclude.Contains(e.Id.Value)) continue;
            string? kind = ClassifyKind(e);
            if (kind is null) continue;

            BoundingBoxXYZ? bb = e.get_BoundingBox(null);
            if (bb is null || bb.Max.Z > pt.Z + SearchUpFt) continue;   // must sit below the end

            double gap = pt.Z - bb.Max.Z;
            if (gap < bestGap) { bestGap = gap; best = new SupportInfo { Kind = kind, ElementId = e.Id.Value, ElevationFt = bb.Max.Z }; }
        }
        return best;
    }

    private static List<SupportInfo> FindLandingSupports(Document doc, LandingComponent l, HashSet<long> exclude)
    {
        var found = new List<SupportInfo>();
        if (l.Bounds.IsEmpty) return found;

        var min = new XYZ(l.Bounds.Min.X - PadFt, l.Bounds.Min.Y - PadFt, l.ElevationFt - SearchDownFt);
        var max = new XYZ(l.Bounds.Max.X + PadFt, l.Bounds.Max.Y + PadFt, l.ElevationFt + SearchUpFt);

        var seen = new HashSet<long>();
        foreach (Element e in new FilteredElementCollector(doc)
                     .WherePasses(new BoundingBoxIntersectsFilter(new Outline(min, max)))
                     .WhereElementIsNotElementType())
        {
            if (exclude.Contains(e.Id.Value) || !seen.Add(e.Id.Value)) continue;
            string? kind = ClassifyKind(e);
            if (kind is null or "stairs") continue;

            BoundingBoxXYZ? bb = e.get_BoundingBox(null);
            if (bb is null || bb.Max.Z > l.ElevationFt + SearchUpFt) continue;
            found.Add(new SupportInfo { Kind = kind, ElementId = e.Id.Value, ElevationFt = bb.Max.Z });
        }
        return found;
    }

    private static bool TouchesLanding(LandingComponent l, FlightComponent f)
    {
        if (l.Bounds.IsEmpty) return false;
        foreach (XYZ pt in new[] { RevitGeom.ToXYZ(f.Frame.Origin), RevitGeom.ToXYZ(f.Frame.At(f.SlopeLengthFt, 0, 0)) })
        {
            bool inXY = pt.X >= l.Bounds.Min.X - PadFt && pt.X <= l.Bounds.Max.X + PadFt &&
                        pt.Y >= l.Bounds.Min.Y - PadFt && pt.Y <= l.Bounds.Max.Y + PadFt;
            if (inXY && Math.Abs(pt.Z - l.ElevationFt) <= SearchDownFt) return true;
        }
        return false;
    }

    private static string? ClassifyKind(Element e)
    {
        long? cat = e.Category?.Id.Value;
        if (cat is null) return null;
        if (cat == (long)BuiltInCategory.OST_StructuralFraming) return "beam";
        if (cat == (long)BuiltInCategory.OST_Walls) return "wall";
        if (cat == (long)BuiltInCategory.OST_Floors) return "slab";
        if (cat == (long)BuiltInCategory.OST_StructuralFoundation) return "foundation";
        if (cat == (long)BuiltInCategory.OST_Stairs) return "stairs";
        return null;
    }
}
