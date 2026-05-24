using Autodesk.Revit.DB;

namespace WallReinforcement.Domain;

public enum JunctionKind
{
    /// <summary>Both walls end at the same point (L-shaped corner).</summary>
    LCorner,
    /// <summary>Our wall ends at a point on the interior of the other wall (we are the stem).</summary>
    TStem,
}

public class WallJunction
{
    public required JunctionKind Kind        { get; init; }
    public required Wall         OtherWall   { get; init; }
    public required XYZ          Point       { get; init; }   // world point of the joint
    /// <summary>"u" coord on OUR wall where the junction sits (0 or Length).</summary>
    public required double       OurU        { get; init; }
    /// <summary>Unit vector along the other wall, pointing AWAY from the joint into that wall.</summary>
    public required XYZ          OtherDir    { get; init; }
}

public static class WallJunctions
{
    private const double Tolerance = 1e-3; // feet ~= 0.3 mm

    /// <summary>
    /// Detect L-corner and T-stem junctions at the two endpoints of <paramref name="axes"/>.Wall.
    /// Caller filters to only the joints they want (e.g. only walls also in the selected set).
    /// </summary>
    public static IReadOnlyList<WallJunction> Detect(WallAxes axes)
    {
        Wall wall = axes.Wall;
        Document doc = wall.Document;

        if (wall.Location is not LocationCurve loc) return [];

        XYZ ourStart = loc.Curve.GetEndPoint(0);
        XYZ ourEnd   = loc.Curve.GetEndPoint(1);

        var found = new List<WallJunction>();
        ScanEndpoint(doc, wall, axes, ourStart, 0.0,              found);
        ScanEndpoint(doc, wall, axes, ourEnd,   axes.Length,      found);
        return found;
    }

    private static void ScanEndpoint(Document doc, Wall ourWall, WallAxes axes,
                                     XYZ point, double ourU, List<WallJunction> sink)
    {
        var outline = new Outline(point - new XYZ(1, 1, 1), point + new XYZ(1, 1, 1));
        var bboxFilter = new BoundingBoxIntersectsFilter(outline);

        var candidates = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Walls)
            .WhereElementIsNotElementType()
            .WherePasses(bboxFilter)
            .Cast<Wall>()
            .Where(w => w.Id != ourWall.Id && w.Location is LocationCurve);

        foreach (Wall other in candidates)
        {
            var otherLoc = (LocationCurve)other.Location;
            Curve oc = otherLoc.Curve;
            XYZ oStart = oc.GetEndPoint(0);
            XYZ oEnd   = oc.GetEndPoint(1);

            // L-corner: other wall also ends at this point.
            if (oStart.DistanceTo(point) < Tolerance)
            {
                XYZ dir = (oEnd - oStart).Normalize();
                sink.Add(new WallJunction
                {
                    Kind = JunctionKind.LCorner, OtherWall = other, Point = point,
                    OurU = ourU, OtherDir = dir,
                });
                continue;
            }
            if (oEnd.DistanceTo(point) < Tolerance)
            {
                XYZ dir = (oStart - oEnd).Normalize();
                sink.Add(new WallJunction
                {
                    Kind = JunctionKind.LCorner, OtherWall = other, Point = point,
                    OurU = ourU, OtherDir = dir,
                });
                continue;
            }

            // T-stem: our endpoint lies on the interior of the other wall's curve.
            IntersectionResult? proj = oc.Project(point);
            if (proj is null) continue;
            if (proj.XYZPoint.DistanceTo(point) > Tolerance) continue;

            double param = proj.Parameter;
            double pStart = oc.GetEndParameter(0);
            double pEnd   = oc.GetEndParameter(1);
            if (param <= pStart + Tolerance || param >= pEnd - Tolerance) continue;

            XYZ tangent = oc.ComputeDerivatives(param, normalized: false).BasisX.Normalize();
            sink.Add(new WallJunction
            {
                Kind = JunctionKind.TStem, OtherWall = other, Point = point,
                OurU = ourU, OtherDir = tangent,
            });
        }
    }
}
