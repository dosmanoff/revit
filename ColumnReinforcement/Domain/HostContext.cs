using Autodesk.Revit.DB;

namespace ColumnReinforcement.Domain;

/// <summary>
/// Spatial context for one column at run time: the slab directly below it (used by
/// foundation-dowel placement) and, in later PRs, the slab/column directly above it
/// (used by upper-splice placement). Detection is bounding-box based — fast and
/// good enough for typical structural layouts where slabs are roughly axis-aligned.
/// </summary>
public class HostContext
{
    public required FamilyInstance Column { get; init; }
    public Element? SlabBelow { get; init; }
    public Element? SlabAbove { get; init; }

    /// <summary>
    /// Find the highest slab whose top elevation is at or just below the column's base
    /// and whose plan extent contains the column centreline. Used as the anchor for
    /// foundation dowels.
    /// </summary>
    /// <param name="onlyStructuralFoundation">
    /// When true, restrict the search to <see cref="BuiltInCategory.OST_StructuralFoundation"/>;
    /// when false, also include regular <see cref="BuiltInCategory.OST_Floors"/>.
    /// </param>
    public static Element? FindSlabBelow(
        FamilyInstance column, ColumnGeometry geom, bool onlyStructuralFoundation)
    {
        Document doc = column.Document;
        double columnBaseZ = geom.BaseCenter.Z;
        XYZ columnXY = geom.BaseCenter;

        // 1/8" tolerance — columns sitting flush on the slab can have BB.Max.Z slightly
        // above the column base due to floating-point rounding.
        const double zTolerance = 1.0 / 96.0;

        var categories = onlyStructuralFoundation
            ? new[] { BuiltInCategory.OST_StructuralFoundation }
            : new[] { BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_Floors };

        Element? best = null;
        double bestTopZ = double.NegativeInfinity;

        foreach (BuiltInCategory cat in categories)
        {
            var elems = new FilteredElementCollector(doc)
                .OfCategory(cat)
                .WhereElementIsNotElementType();

            foreach (Element e in elems)
            {
                BoundingBoxXYZ? bb = e.get_BoundingBox(null);
                if (bb is null) continue;

                if (bb.Max.Z > columnBaseZ + zTolerance) continue;             // slab top above column base — not below
                if (columnXY.X < bb.Min.X || columnXY.X > bb.Max.X) continue;  // column centre not over the slab
                if (columnXY.Y < bb.Min.Y || columnXY.Y > bb.Max.Y) continue;

                if (bb.Max.Z > bestTopZ)
                {
                    bestTopZ = bb.Max.Z;
                    best = e;
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Find the lowest slab whose bottom elevation is at or just above the column's top
    /// and whose plan extent contains the column centreline. Used as the anchor for
    /// upper splices that bend into the slab above. Only <see cref="BuiltInCategory.OST_Floors"/>
    /// is searched — foundation slabs are not typically located above a column.
    /// </summary>
    public static Element? FindSlabAbove(FamilyInstance column, ColumnGeometry geom)
    {
        Document doc = column.Document;
        double columnTopZ = geom.BaseCenter.Z + geom.Height;
        XYZ columnXY = geom.BaseCenter;

        const double zTolerance = 1.0 / 96.0;

        // The slab "above" is the one the column connects to at its top — its TOP must
        // reach at least the column top (so a bent bar can develop inside it), and it
        // must overlap the column in plan. A column modelled up to the top of the slab
        // (or partway into it) has that slab's bottom BELOW its top — so we must NOT
        // require slab.Min.Z >= columnTop, or the connecting slab is skipped and the
        // NEXT slab up gets picked (bent bars then fly to the storey above). Choose the
        // nearest qualifying slab = smallest top elevation.
        Element? best = null;
        double bestTopZ = double.PositiveInfinity;

        var elems = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Floors)
            .WhereElementIsNotElementType();

        foreach (Element e in elems)
        {
            BoundingBoxXYZ? bb = e.get_BoundingBox(null);
            if (bb is null) continue;

            if (bb.Max.Z < columnTopZ - zTolerance) continue;                 // slab top is below the column top — it's not above
            if (columnXY.X < bb.Min.X || columnXY.X > bb.Max.X) continue;
            if (columnXY.Y < bb.Min.Y || columnXY.Y > bb.Max.Y) continue;

            if (bb.Max.Z < bestTopZ)
            {
                bestTopZ = bb.Max.Z;
                best = e;
            }
        }
        return best;
    }

    /// <summary>
    /// Find a structural column whose top is at or just below the current column's
    /// base and whose plan AABB contains the current column's centreline. Used as the
    /// host for dowels when the section change is too large for a Cranked splice.
    /// </summary>
    public static Element? FindColumnBelow(FamilyInstance column, ColumnGeometry geom)
    {
        Document doc = column.Document;
        double columnBaseZ = geom.BaseCenter.Z;
        XYZ columnXY = geom.BaseCenter;
        const double zTolerance = 1.0 / 96.0;

        Element? best = null;
        double bestTopZ = double.NegativeInfinity;

        var elems = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_StructuralColumns)
            .WhereElementIsNotElementType();

        foreach (Element e in elems)
        {
            if (e.Id == column.Id) continue;             // skip self
            BoundingBoxXYZ? bb = e.get_BoundingBox(null);
            if (bb is null) continue;

            if (bb.Max.Z > columnBaseZ + zTolerance) continue;
            if (columnXY.X < bb.Min.X || columnXY.X > bb.Max.X) continue;
            if (columnXY.Y < bb.Min.Y || columnXY.Y > bb.Max.Y) continue;

            if (bb.Max.Z > bestTopZ)
            {
                bestTopZ = bb.Max.Z;
                best = e;
            }
        }
        return best;
    }

    /// <summary>
    /// Find a structural framing element (typically a beam) whose body intersects the
    /// column at its base elevation and whose plan AABB contains the column centreline.
    /// Used as the host for dowels when the column lands on a transfer beam.
    /// </summary>
    public static Element? FindBeamBelow(FamilyInstance column, ColumnGeometry geom)
    {
        Document doc = column.Document;
        double columnBaseZ = geom.BaseCenter.Z;
        XYZ columnXY = geom.BaseCenter;
        const double zTolerance = 1.0 / 96.0;

        // A beam sits below the column when its top is at-or-below the column base
        // (typical for a column resting ON a beam) OR when its body straddles the
        // column base (atypical, but possible at slab-band beams). Accept both.
        Element? best = null;
        double bestTopZ = double.NegativeInfinity;

        var elems = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_StructuralFraming)
            .WhereElementIsNotElementType();

        foreach (Element e in elems)
        {
            BoundingBoxXYZ? bb = e.get_BoundingBox(null);
            if (bb is null) continue;

            // Top of beam must not be above the column base (more than tolerance).
            if (bb.Max.Z > columnBaseZ + zTolerance) continue;
            if (columnXY.X < bb.Min.X || columnXY.X > bb.Max.X) continue;
            if (columnXY.Y < bb.Min.Y || columnXY.Y > bb.Max.Y) continue;

            if (bb.Max.Z > bestTopZ)
            {
                bestTopZ = bb.Max.Z;
                best = e;
            }
        }
        return best;
    }

    /// <summary>
    /// Resolve the actual host element below the column according to the configured
    /// <see cref="Config.DowelHost"/> kind. Returns the element (any category) along
    /// with the kind that matched, or <c>null</c> when no host of the requested kind
    /// is found.
    /// </summary>
    public static (Element host, Config.DowelHost kind)? ResolveDowelHost(
        FamilyInstance column, ColumnGeometry geom, Config.DowelHost requested, bool onlyStructuralFoundation)
    {
        Element? hit;
        switch (requested)
        {
            case Config.DowelHost.Foundation:
                hit = FindSlabBelow(column, geom, onlyStructuralFoundation: true);
                return hit is null ? null : (hit, Config.DowelHost.Foundation);

            case Config.DowelHost.Floor:
                hit = FindFloorBelow(column, geom);
                return hit is null ? null : (hit, Config.DowelHost.Floor);

            case Config.DowelHost.Column:
                hit = FindColumnBelow(column, geom);
                return hit is null ? null : (hit, Config.DowelHost.Column);

            case Config.DowelHost.Beam:
                hit = FindBeamBelow(column, geom);
                return hit is null ? null : (hit, Config.DowelHost.Beam);

            case Config.DowelHost.Auto:
            default:
                hit = FindSlabBelow(column, geom, onlyStructuralFoundation);
                if (hit is null) return null;
                // Detect which category we got so the builder can branch on geometry.
                var bic = (BuiltInCategory)hit.Category.Id.Value;
                var kind = bic == BuiltInCategory.OST_StructuralFoundation
                    ? Config.DowelHost.Foundation
                    : Config.DowelHost.Floor;
                return (hit, kind);
        }
    }

    /// <summary>Find an <see cref="BuiltInCategory.OST_Floors"/> slab below the column (helper for ResolveDowelHost).</summary>
    private static Element? FindFloorBelow(FamilyInstance column, ColumnGeometry geom)
    {
        Document doc = column.Document;
        double columnBaseZ = geom.BaseCenter.Z;
        XYZ columnXY = geom.BaseCenter;
        const double zTolerance = 1.0 / 96.0;

        Element? best = null;
        double bestTopZ = double.NegativeInfinity;

        var elems = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Floors)
            .WhereElementIsNotElementType();

        foreach (Element e in elems)
        {
            BoundingBoxXYZ? bb = e.get_BoundingBox(null);
            if (bb is null) continue;
            if (bb.Max.Z > columnBaseZ + zTolerance) continue;
            if (columnXY.X < bb.Min.X || columnXY.X > bb.Max.X) continue;
            if (columnXY.Y < bb.Min.Y || columnXY.Y > bb.Max.Y) continue;
            if (bb.Max.Z > bestTopZ) { bestTopZ = bb.Max.Z; best = e; }
        }
        return best;
    }
}
