using Autodesk.Revit.DB;

namespace WallReinforcement.Domain;

/// <summary>
/// Local axes of a wall (Length / Height / Normal) plus its bounding rectangle in those axes.
/// Phase-1/2 simplification: assumes a straight, plumb wall. Arc walls fall back to chord-based axes.
/// </summary>
public class WallAxes
{
    public required Wall Wall      { get; init; }
    public required XYZ  Origin    { get; init; }   // base corner at the "start" end, bottom of wall
    public required XYZ  LengthDir { get; init; }   // along LocationCurve (unit vector)
    public required XYZ  HeightDir { get; init; }   // typically world Z
    public required XYZ  Normal    { get; init; }   // wall facing direction; points from interior to exterior
    public required double Length     { get; init; }
    public required double Height     { get; init; }
    public required double Thickness  { get; init; }

    public static WallAxes For(Wall wall)
    {
        if (wall.Location is not LocationCurve loc)
            throw new InvalidOperationException("Wall has no LocationCurve.");

        Curve curve = loc.Curve;
        XYZ start = curve.GetEndPoint(0);
        XYZ end   = curve.GetEndPoint(1);
        XYZ lengthDir = (end - start).Normalize();

        double height = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble()
                        ?? (wall.get_BoundingBox(null) is { } bb ? bb.Max.Z - bb.Min.Z : 0);

        double baseOffset = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? 0;
        XYZ origin = new(start.X, start.Y, GetBaseElevation(wall) + baseOffset);

        return new WallAxes
        {
            Wall      = wall,
            Origin    = origin,
            LengthDir = lengthDir,
            HeightDir = XYZ.BasisZ,
            Normal    = wall.Orientation.Normalize(),
            Length    = curve.Length,
            Height    = height,
            Thickness = wall.Width,
        };
    }

    private static double GetBaseElevation(Wall wall)
    {
        ElementId baseLevelId = wall.LevelId;
        if (baseLevelId == ElementId.InvalidElementId) return 0;
        return wall.Document.GetElement(baseLevelId) is Level level ? level.Elevation : 0;
    }

    /// <summary>Point at (u along length, v along height), on the wall's centerplane (offset = 0 from normal).</summary>
    public XYZ At(double u, double v, double offset = 0) =>
        Origin + LengthDir * u + HeightDir * v + Normal * offset;

    /// <summary>Distance from the wall's centerplane to the exterior face (positive along <see cref="Normal"/>).</summary>
    public double HalfThickness => Thickness * 0.5;
}

/// <summary>Axis-aligned opening on a wall, expressed in wall (u,v) coords.</summary>
public class OpeningRect
{
    public required ElementId InsertId { get; init; }
    public required double UMin { get; init; }
    public required double UMax { get; init; }
    public required double VMin { get; init; }
    public required double VMax { get; init; }

    public double Width  => UMax - UMin;
    public double Height => VMax - VMin;
}

public static class WallGeometry
{
    /// <summary>
    /// Return rectangles for every door/window/rectangular opening hosted by the wall, in the
    /// wall's (u,v) coordinate system. Non-axis-aligned or non-rectangular cuts use their AABB.
    /// </summary>
    public static IReadOnlyList<OpeningRect> GetOpenings(WallAxes axes)
    {
        Wall wall = axes.Wall;
        Document doc = wall.Document;

        IList<ElementId> insertIds = wall.FindInserts(true, false, true, true);
        var rects = new List<OpeningRect>(insertIds.Count);

        foreach (ElementId id in insertIds)
        {
            Element? insert = doc.GetElement(id);
            if (insert is null) continue;

            BoundingBoxXYZ? bb = insert.get_BoundingBox(null);
            if (bb is null) continue;

            // Project the 8 corners of the AABB into wall (u,v) and keep min/max.
            double uMin = double.MaxValue, uMax = double.MinValue;
            double vMin = double.MaxValue, vMax = double.MinValue;
            foreach (XYZ corner in EnumerateCorners(bb))
            {
                XYZ rel = corner - axes.Origin;
                double u = rel.DotProduct(axes.LengthDir);
                double v = rel.DotProduct(axes.HeightDir);
                if (u < uMin) uMin = u;
                if (u > uMax) uMax = u;
                if (v < vMin) vMin = v;
                if (v > vMax) vMax = v;
            }

            // Clip to wall extents — bounding boxes of family instances often overshoot.
            uMin = Math.Max(uMin, 0);
            uMax = Math.Min(uMax, axes.Length);
            vMin = Math.Max(vMin, 0);
            vMax = Math.Min(vMax, axes.Height);

            if (uMax - uMin < 1e-3 || vMax - vMin < 1e-3) continue;

            rects.Add(new OpeningRect
            {
                InsertId = id,
                UMin = uMin, UMax = uMax,
                VMin = vMin, VMax = vMax,
            });
        }

        return rects;
    }

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
