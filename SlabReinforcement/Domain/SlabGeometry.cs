using Autodesk.Revit.DB;
using SlabReinforcement.Geometry;

namespace SlabReinforcement.Domain;

/// <summary>
/// Plan geometry of a floor slab: local basis, outer boundary and sketch openings
/// (as 2-D world-XY loops in feet), thickness and top/bottom elevations.
///
/// Revit-facing — it reads the floor's sketch profile and delegates the math to the
/// Revit-free <c>SlabReinforcement.Geometry</c> assembly. Arcs are approximated by
/// their chord (vertex-to-vertex) in this phase; true arc support is Phase 5.
/// Hosted <c>Opening</c> elements and by-family penetrations are added in PR-03 — here
/// only loops drawn directly in the floor sketch are seen.
/// </summary>
public sealed class SlabGeometry
{
    public required Floor Floor { get; init; }
    public required Loop2 Outer { get; init; }                    // world XY, feet
    public required IReadOnlyList<Loop2> SketchOpenings { get; init; }
    public required PlanBasis Basis { get; init; }
    public required double ThicknessFt { get; init; }
    public required double TopElevationFt { get; init; }
    public required double BottomElevationFt { get; init; }

    public Bounds2 Bounds => Outer.Bounds;

    /// <summary>Gross plan area of the outer boundary (ft²).</summary>
    public double GrossAreaSf => Outer.Area;

    /// <summary>Plan area less the sketch openings (ft²).</summary>
    public double NetAreaSf => Outer.Area - SketchOpenings.Sum(o => o.Area);

    /// <summary>Angle of the local X axis from world +X, degrees.</summary>
    public double XWorldDeg => Basis.AngleDeg;

    public static SlabGeometry For(Floor floor)
    {
        IReadOnlyList<Loop2> loops = ExtractSketchLoops(floor);
        if (loops.Count == 0)
            throw new InvalidOperationException(
                $"Floor {floor.Id.Value} has no readable sketch profile (linked or non-sketched floor?).");

        int outerIdx = GeometryMath.LargestLoopIndex(loops);
        Loop2 outer = loops[outerIdx];

        var openings = new List<Loop2>(loops.Count - 1);
        for (int i = 0; i < loops.Count; i++)
            if (i != outerIdx) openings.Add(loops[i]);

        BoundingBoxXYZ? bb = floor.get_BoundingBox(null);
        double topZ = bb?.Max.Z ?? 0;
        double botZ = bb?.Min.Z ?? 0;

        return new SlabGeometry
        {
            Floor = floor,
            Outer = outer,
            SketchOpenings = openings,
            Basis = GeometryMath.BasisFromLoop(outer.Points),
            ThicknessFt = ResolveThicknessFt(floor, topZ - botZ),
            TopElevationFt = topZ,
            BottomElevationFt = botZ,
        };
    }

    /// <summary>
    /// Z of the centerline of a reinforcement layer, given the cover (to the bar
    /// nearest the face) measured from the relevant face, in feet. Used by the engine
    /// to place bottom/top mats. Bar radius is accounted for by the caller.
    /// </summary>
    public double LayerPlaneZ(bool top, double coverFt) =>
        top ? TopElevationFt - coverFt : BottomElevationFt + coverFt;

    private static IReadOnlyList<Loop2> ExtractSketchLoops(Floor floor)
    {
        var result = new List<Loop2>();

        ElementId sketchId = floor.SketchId;
        if (sketchId == ElementId.InvalidElementId) return result;
        if (floor.Document.GetElement(sketchId) is not Sketch sketch) return result;

        foreach (CurveArray ca in sketch.Profile)
        {
            var pts = new List<Pt2>(ca.Size);
            foreach (Curve c in ca)
            {
                // Start point of each segment = a polygon vertex (chord-approximates arcs).
                XYZ p = c.GetEndPoint(0);
                pts.Add(new Pt2(p.X, p.Y));
            }
            if (pts.Count >= 3) result.Add(new Loop2(pts));
        }

        return result;
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
