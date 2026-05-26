using Autodesk.Revit.DB;

namespace ColumnReinforcement.Domain;

/// <summary>
/// Local frame and overall sizes of a vertical structural column, expressed in
/// Revit's internal units (feet). Slanted columns are out of scope for Phase 1
/// — the selection filter excludes them, and <see cref="For"/> assumes vertical.
///
/// <para>
/// Width is the cross-section extent along the column's local X axis; Depth is
/// along local Y. Both are recovered by projecting the world-axis-aligned
/// bounding box of the instance into the local frame, so they are correct for
/// any in-plan rotation.
/// </para>
/// </summary>
public class ColumnGeometry
{
    public required FamilyInstance Instance { get; init; }

    /// <summary>Centre of the column's base section, in world coordinates (feet).</summary>
    public required XYZ BaseCenter { get; init; }

    /// <summary>Unit vector along the column's local X axis (in-plan), accounting for rotation.</summary>
    public required XYZ LocalX { get; init; }

    /// <summary>Unit vector along the column's local Y axis (in-plan), accounting for rotation.</summary>
    public required XYZ LocalY { get; init; }

    /// <summary>Cross-section size along <see cref="LocalX"/>, in feet.</summary>
    public required double Width { get; init; }

    /// <summary>Cross-section size along <see cref="LocalY"/>, in feet.</summary>
    public required double Depth { get; init; }

    /// <summary>Top-of-column elevation minus base elevation, in feet (world Z extent of the AABB).</summary>
    public required double Height { get; init; }

    public static ColumnGeometry For(FamilyInstance inst)
    {
        if (inst.IsSlantedColumn)
            throw new InvalidOperationException(
                $"Column {inst.Id.Value} is slanted; Phase 1 supports vertical columns only.");

        Transform tr = inst.GetTotalTransform();
        BoundingBoxXYZ bb = inst.get_BoundingBox(null)
            ?? throw new InvalidOperationException(
                $"Column {inst.Id.Value} has no bounding box (view-dependent geometry only).");

        // Project the eight AABB corners onto the column's local X/Y axes to
        // recover the cross-section size, regardless of in-plan rotation.
        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        foreach (XYZ corner in EnumerateCorners(bb))
        {
            XYZ rel = corner - tr.Origin;
            double x = rel.DotProduct(tr.BasisX);
            double y = rel.DotProduct(tr.BasisY);
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        double width  = maxX - minX;
        double depth  = maxY - minY;
        double height = bb.Max.Z - bb.Min.Z;

        return new ColumnGeometry
        {
            Instance   = inst,
            BaseCenter = new XYZ(tr.Origin.X, tr.Origin.Y, bb.Min.Z),
            LocalX     = tr.BasisX.Normalize(),
            LocalY     = tr.BasisY.Normalize(),
            Width      = width,
            Depth      = depth,
            Height     = height,
        };
    }

    /// <summary>
    /// World-coordinate point at local (x, y) inside the column's cross-section, at height
    /// <paramref name="z"/> above the column's base. <paramref name="x"/>/<paramref name="y"/>
    /// are measured from <see cref="BaseCenter"/> along <see cref="LocalX"/>/<see cref="LocalY"/>.
    /// </summary>
    public XYZ At(double x, double y, double z) =>
        BaseCenter + LocalX * x + LocalY * y + XYZ.BasisZ * z;

    private static IEnumerable<XYZ> EnumerateCorners(BoundingBoxXYZ bb)
    {
        XYZ mn = bb.Min, mx = bb.Max;
        yield return new XYZ(mn.X, mn.Y, mn.Z);
        yield return new XYZ(mx.X, mn.Y, mn.Z);
        yield return new XYZ(mn.X, mx.Y, mn.Z);
        yield return new XYZ(mx.X, mx.Y, mn.Z);
        yield return new XYZ(mn.X, mn.Y, mx.Z);
        yield return new XYZ(mx.X, mn.Y, mx.Z);
        yield return new XYZ(mn.X, mx.Y, mx.Z);
        yield return new XYZ(mx.X, mx.Y, mx.Z);
    }
}
