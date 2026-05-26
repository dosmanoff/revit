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

        Element? best = null;
        double bestBottomZ = double.PositiveInfinity;

        var elems = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Floors)
            .WhereElementIsNotElementType();

        foreach (Element e in elems)
        {
            BoundingBoxXYZ? bb = e.get_BoundingBox(null);
            if (bb is null) continue;

            if (bb.Min.Z < columnTopZ - zTolerance) continue;
            if (columnXY.X < bb.Min.X || columnXY.X > bb.Max.X) continue;
            if (columnXY.Y < bb.Min.Y || columnXY.Y > bb.Max.Y) continue;

            if (bb.Min.Z < bestBottomZ)
            {
                bestBottomZ = bb.Min.Z;
                best = e;
            }
        }
        return best;
    }
}
