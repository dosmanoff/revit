using Autodesk.Revit.DB;
using SlabReinforcement.Geometry;

namespace SlabReinforcement.Domain;

/// <summary>
/// Plan geometry of a floor slab: local basis, outer boundary and openings (as 2-D world-XY
/// loops in feet), thickness and top/bottom elevations.
///
/// Revit-facing. Voids are taken from the slab's actual horizontal <b>face geometry</b>
/// (<see cref="Face.GetEdgesAsCurveLoops"/>), which captures openings of every kind — sketch
/// cut-outs, hosted <c>Opening</c>/shaft elements, and family void cuts — and tessellates arcs
/// (so round/curved openings are handled). The largest loop is the outer boundary; the rest are
/// holes. Falls back to the floor sketch when no horizontal face is queryable (e.g. sloped slabs).
/// The clipping/layout math is delegated to the Revit-free <c>SlabReinforcement.Geometry</c>.
/// </summary>
public sealed class SlabGeometry
{
    public required Floor Floor { get; init; }
    public required Loop2 Outer { get; init; }                    // world XY, feet
    public required IReadOnlyList<Loop2> Openings { get; init; }  // holes (no concrete)
    public required PlanBasis Basis { get; init; }
    public required double ThicknessFt { get; init; }
    public required double TopElevationFt { get; init; }
    public required double BottomElevationFt { get; init; }

    public Bounds2 Bounds => Outer.Bounds;

    /// <summary>Gross plan area of the outer boundary (ft²).</summary>
    public double GrossAreaSf => Outer.Area;

    /// <summary>Plan area less the openings (ft²).</summary>
    public double NetAreaSf => Outer.Area - Openings.Sum(o => o.Area);

    /// <summary>Angle of the local X axis from world +X, degrees.</summary>
    public double XWorldDeg => Basis.AngleDeg;

    public static SlabGeometry For(Floor floor)
    {
        IReadOnlyList<Loop2> loops = ExtractLoops(floor);
        if (loops.Count == 0)
            throw new InvalidOperationException(
                $"Floor {floor.Id.Value} has no readable boundary (linked or geometry-less floor?).");

        int outerIdx = GeometryMath.LargestLoopIndex(loops);
        Loop2 outer = loops[outerIdx];

        var openings = new List<Loop2>(Math.Max(0, loops.Count - 1));
        for (int i = 0; i < loops.Count; i++)
            if (i != outerIdx) openings.Add(loops[i]);

        BoundingBoxXYZ? bb = floor.get_BoundingBox(null);
        double topZ = bb?.Max.Z ?? 0;
        double botZ = bb?.Min.Z ?? 0;

        return new SlabGeometry
        {
            Floor = floor,
            Outer = outer,
            Openings = openings,
            Basis = GeometryMath.BasisFromLoop(outer.Points),
            ThicknessFt = ResolveThicknessFt(floor, topZ - botZ),
            TopElevationFt = topZ,
            BottomElevationFt = botZ,
        };
    }

    /// <summary>
    /// Z of the centerline of a reinforcement layer, given the cover from the relevant face (ft).
    /// </summary>
    public double LayerPlaneZ(bool top, double coverFt) =>
        top ? TopElevationFt - coverFt : BottomElevationFt + coverFt;

    // ── Loop extraction ──────────────────────────────────────────────────────────

    private static IReadOnlyList<Loop2> ExtractLoops(Floor floor)
    {
        List<Loop2> fromFace = ExtractFaceLoops(floor);
        return fromFace.Count > 0 ? fromFace : ExtractSketchLoops(floor);
    }

    /// <summary>
    /// Outer boundary + every hole, from the slab's largest horizontal planar face. Holes here
    /// reflect real voids regardless of how they were modelled. Arcs are tessellated.
    /// </summary>
    private static List<Loop2> ExtractFaceLoops(Floor floor)
    {
        var result = new List<Loop2>();

        var opt = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false };
        GeometryElement? ge;
        try { ge = floor.get_Geometry(opt); }
        catch { return result; }
        if (ge is null) return result;

        Face? best = null;
        double bestArea = -1;
        foreach (GeometryObject go in ge)
        {
            if (go is not Solid solid) continue;
            foreach (Face face in solid.Faces)
            {
                if (face is not PlanarFace pf || Math.Abs(pf.FaceNormal.Z) < 0.99) continue;
                if (face.Area > bestArea) { bestArea = face.Area; best = face; }
            }
        }
        if (best is null) return result;

        foreach (CurveLoop cl in best.GetEdgesAsCurveLoops())
            if (ToLoop(cl) is { } loop) result.Add(loop);

        return result;
    }

    private static List<Loop2> ExtractSketchLoops(Floor floor)
    {
        var result = new List<Loop2>();
        if (floor.SketchId == ElementId.InvalidElementId) return result;
        if (floor.Document.GetElement(floor.SketchId) is not Sketch sketch) return result;

        foreach (CurveArray ca in sketch.Profile)
        {
            var pts = new List<Pt2>();
            foreach (Curve c in ca) Tessellate(c, pts);
            if (pts.Count >= 3) result.Add(new Loop2(pts));
        }
        return result;
    }

    private static Loop2? ToLoop(CurveLoop loop)
    {
        var pts = new List<Pt2>();
        foreach (Curve c in loop) Tessellate(c, pts);
        return pts.Count >= 3 ? new Loop2(pts) : null;
    }

    /// <summary>Append a curve's tessellation to <paramref name="pts"/>, dropping the shared end
    /// point (the next curve in the loop starts there).</summary>
    private static void Tessellate(Curve c, List<Pt2> pts)
    {
        IList<XYZ> t = c.Tessellate();
        for (int i = 0; i < t.Count - 1; i++) pts.Add(new Pt2(t[i].X, t[i].Y));
    }

    private static double ResolveThicknessFt(Floor floor, double bboxHeight)
    {
        if (floor.Document.GetElement(floor.GetTypeId()) is FloorType ft)
        {
            CompoundStructure? cs = ft.GetCompoundStructure();
            if (cs is not null)
            {
                double w = cs.GetWidth();
                if (w > 1e-6) return w;
            }
        }
        return bboxHeight;
    }
}
