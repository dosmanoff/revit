using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StairsReinforcement.Geometry;

namespace StairsReinforcement.Domain;

/// <summary>Revit-API ⇆ Revit-free geometry adapters and small solid/face helpers.</summary>
internal static class RevitGeom
{
    public static Pt3 P3(XYZ p) => new(p.X, p.Y, p.Z);
    public static Pt2 P2(XYZ p) => new(p.X, p.Y);
    public static XYZ ToXYZ(Pt3 p) => new(p.X, p.Y, p.Z);

    public static IEnumerable<Solid> Solids(Element e)
    {
        var opt = new Options
        {
            ComputeReferences = false,
            IncludeNonVisibleObjects = false,
            DetailLevel = ViewDetailLevel.Fine,
        };
        GeometryElement? ge = e.get_Geometry(opt);
        if (ge is null) yield break;

        foreach (GeometryObject go in ge)
        {
            if (go is Solid s && s.Volume > 1e-9) yield return s;
            else if (go is GeometryInstance gi)
                foreach (GeometryObject g2 in gi.GetInstanceGeometry())
                    if (g2 is Solid s2 && s2.Volume > 1e-9) yield return s2;
        }
    }

    public static Solid? LargestSolid(Element e) =>
        Solids(e).OrderByDescending(s => s.Volume).FirstOrDefault();

    /// <summary>
    /// The run's inclined soffit: the largest planar face whose normal points down-and-out (a
    /// horizontal component, so it is the sloped underside, not a flat landing soffit). Its normal is
    /// the ground truth for the slope frame — see <see cref="FlightFrame.FromSoffit"/>.
    /// </summary>
    public static PlanarFace? SoffitFace(Element e)
    {
        PlanarFace? best = null;
        double bestArea = -1;
        foreach (Solid s in Solids(e))
            foreach (Face f in s.Faces)
            {
                if (f is not PlanarFace pf) continue;
                XYZ n = pf.FaceNormal;
                if (n.Z > -0.3 || n.Z < -0.97) continue;                       // inclined and downward
                if (Math.Sqrt(n.X * n.X + n.Y * n.Y) < 0.15) continue;          // not a flat soffit
                if (pf.Area > bestArea) { bestArea = pf.Area; best = pf; }
            }
        return best;
    }

    /// <summary>Largest planar face whose outward normal points up (top) or down (bottom).</summary>
    public static PlanarFace? ExtremeFace(Solid solid, bool top)
    {
        PlanarFace? best = null;
        double bestArea = -1;
        foreach (Face f in solid.Faces)
        {
            if (f is not PlanarFace pf) continue;
            double nz = pf.FaceNormal.Z;
            bool match = top ? nz > 0.2 : nz < -0.2;
            if (!match) continue;
            if (pf.Area > bestArea) { bestArea = pf.Area; best = pf; }
        }
        return best;
    }

    public static Bounds3 ElemBounds(Element e)
    {
        var b = new Bounds3();
        BoundingBoxXYZ? bb = e.get_BoundingBox(null);
        if (bb is not null) { b.Add(P3(bb.Min)); b.Add(P3(bb.Max)); }
        return b;
    }

    /// <summary>
    /// World AABB from the element's OWN solid geometry. For native stair components
    /// (StairsRun/StairsLanding of a Monolithic Stair) <c>get_BoundingBox(null)</c> returns the
    /// whole-stair box, so per-component Z is wrong; the solids give the true per-component extent.
    /// </summary>
    public static Bounds3 SolidBounds(Element e)
    {
        var b = new Bounds3();
        foreach (Solid s in Solids(e))
        {
            BoundingBoxXYZ? bb = s.GetBoundingBox();
            if (bb is null) continue;
            Transform t = bb.Transform;
            foreach (XYZ corner in BoxCorners(bb.Min, bb.Max))
                b.Add(P3(t.OfPoint(corner)));
        }
        return b;
    }

    private static IEnumerable<XYZ> BoxCorners(XYZ min, XYZ max)
    {
        foreach (double x in new[] { min.X, max.X })
            foreach (double y in new[] { min.Y, max.Y })
                foreach (double z in new[] { min.Z, max.Z })
                    yield return new XYZ(x, y, z);
    }

    public static bool IsValidRebarHost(Element e)
    {
        try
        {
            RebarHostData? hd = RebarHostData.GetRebarHostData(e);
            return hd is not null && hd.IsValidHost();
        }
        catch { return false; }
    }

    /// <summary>Tessellate a closed curve loop to a plan polygon (XY, feet), dropping duplicate vertices.</summary>
    public static List<Pt2> ToPlanLoop(IEnumerable<Curve> curves)
    {
        var pts = new List<Pt2>();
        foreach (Curve c in curves)
            foreach (XYZ p in c.Tessellate())
            {
                Pt2 q = P2(p);
                if (pts.Count == 0 || (pts[^1] - q).Length > 1e-7) pts.Add(q);
            }
        if (pts.Count > 1 && (pts[0] - pts[^1]).Length < 1e-7) pts.RemoveAt(pts.Count - 1);
        return pts;
    }

    /// <summary>String param by exact name on the element, or null.</summary>
    public static double? LookupLengthFt(Element e, params string[] names)
    {
        foreach (string n in names)
        {
            Parameter? p = e.LookupParameter(n);
            if (p is { StorageType: StorageType.Double }) return p.AsDouble();
        }
        return null;
    }

    public static string? Mark(Element e) =>
        e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();

    public static string? Comments(Element e) =>
        e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();

    /// <summary>Floor structural thickness from the compound structure, fallback to bbox height.</summary>
    public static double FloorThicknessFt(Floor floor)
    {
        if (floor.Document.GetElement(floor.GetTypeId()) is FloorType ft)
        {
            CompoundStructure? cs = ft.GetCompoundStructure();
            if (cs is not null) { double w = cs.GetWidth(); if (w > 1e-6) return w; }
        }
        Bounds3 b = ElemBounds(floor);
        return b.IsEmpty ? 0 : b.Size.Z;
    }
}
