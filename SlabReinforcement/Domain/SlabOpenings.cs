using Autodesk.Revit.DB;
using SlabReinforcement.Geometry;

namespace SlabReinforcement.Domain;

public enum OpeningSource { SketchLoop, FloorOpening, ShaftOpening }

/// <summary>A penetration through the slab, with its plan boundary and a trim flag.</summary>
public sealed class SlabOpening
{
    public required OpeningSource Source { get; init; }
    public required long ElementId { get; init; }      // 0 for sketch loops
    public required Loop2 Boundary { get; init; }      // world XY, feet
    public required bool NeedsTrim { get; init; }

    public Bounds2 Bounds => Boundary.Bounds;
    public double AreaSf => Boundary.Area;
}

/// <summary>
/// Collects slab penetrations from three sources: inner loops drawn in the floor sketch,
/// hosted <see cref="Opening"/> elements on the floor, and shaft openings that cut through
/// it. By-family penetrations (FamilyInstances cutting the floor) are Phase 5.
/// </summary>
public static class SlabOpenings
{
    /// <summary>Openings this size (either plan dimension) or larger get trim bars / U-bars + diagonals.</summary>
    public const double TrimThresholdFt = 1.0;   // 12"

    public static IReadOnlyList<SlabOpening> For(SlabGeometry geom)
    {
        var result = new List<SlabOpening>();
        Floor floor = geom.Floor;
        Document doc = floor.Document;

        // 1) Inner loops in the floor sketch.
        foreach (Loop2 loop in geom.SketchOpenings)
            result.Add(Make(OpeningSource.SketchLoop, 0, loop));

        // 2) Hosted Opening elements on this floor + shafts cutting it.
        foreach (Opening op in new FilteredElementCollector(doc).OfClass(typeof(Opening)).Cast<Opening>())
        {
            try
            {
                if (IsFloorOpeningOf(op, floor))
                {
                    if (BoundaryOf(op) is { } loop)
                        result.Add(Make(OpeningSource.FloorOpening, op.Id.Value, loop));
                }
                else if (IsShaftCutting(op, geom))
                {
                    if (BoundaryOf(op) is { } loop)
                        result.Add(Make(OpeningSource.ShaftOpening, op.Id.Value, loop));
                }
            }
            catch { /* skip a malformed / unsupported opening */ }
        }

        return result;
    }

    public static int TrimCount(IReadOnlyList<SlabOpening> openings) => openings.Count(o => o.NeedsTrim);

    private static SlabOpening Make(OpeningSource src, long id, Loop2 loop) => new()
    {
        Source = src,
        ElementId = id,
        Boundary = loop,
        NeedsTrim = Math.Max(loop.Bounds.Width, loop.Bounds.Height) >= TrimThresholdFt,
    };

    private static bool IsFloorOpeningOf(Opening op, Floor floor) =>
        op.Host is { } host && host.Id == floor.Id;

    private static bool IsShaftCutting(Opening op, SlabGeometry geom)
    {
        if (op.Host is not null) return false;            // shafts are host-less
        BoundingBoxXYZ? bb = op.get_BoundingBox(null);
        if (bb is null) return false;

        double z = geom.TopElevationFt;
        if (bb.Min.Z - 0.5 > z || bb.Max.Z + 0.5 < z) return false;   // must span this slab

        var center = new Pt2((bb.Min.X + bb.Max.X) * 0.5, (bb.Min.Y + bb.Max.Y) * 0.5);
        return Geometry2D.PointInLoop(geom.Outer.Points, center);
    }

    private static Loop2? BoundaryOf(Opening op)
    {
        if (op.IsRectBoundary)
        {
            IList<XYZ> rect = op.BoundaryRect;
            if (rect.Count >= 2)
            {
                XYZ a = rect[0], b = rect[1];
                return new Loop2([new(a.X, a.Y), new(b.X, a.Y), new(b.X, b.Y), new(a.X, b.Y)]);
            }
        }

        CurveArray? ca = op.BoundaryCurves;
        if (ca is null || ca.Size < 3) return null;

        var pts = new List<Pt2>(ca.Size);
        foreach (Curve c in ca)
        {
            XYZ p = c.GetEndPoint(0);
            pts.Add(new Pt2(p.X, p.Y));
        }
        return pts.Count >= 3 ? new Loop2(pts) : null;
    }
}
