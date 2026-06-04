using Autodesk.Revit.DB;
using SlabReinforcement.Engine;
using SlabReinforcement.Geometry;

namespace SlabReinforcement.Domain;

public enum EdgeKind { Free, Beam, Wall, Slab }
public enum SupportKind { Column, Wall, Beam }

/// <summary>One outer-boundary edge of the slab and what it borders.</summary>
public sealed class BoundaryEdge
{
    public required int Index { get; init; }
    public required Seg2 Segment { get; init; }
    public required double OutwardNormalDeg { get; init; }   // deg from world +X

    public EdgeKind Kind { get; set; } = EdgeKind.Free;
    public long AdjacentElementId { get; set; }              // 0 = none
    public string? AdjacentMark { get; set; }

    public double LengthFt => Segment.Length;
}

/// <summary>A column / wall / beam under the slab — drives strengthening zones.</summary>
public sealed class SupportBelow
{
    public required SupportKind Kind { get; init; }
    public required long ElementId { get; init; }
    public string? Mark { get; init; }
    public required Pt2 CenterXY { get; init; }              // feet
    public double WidthIn { get; init; }
    public double DepthIn { get; init; }
}

/// <summary>
/// Topological context of a slab: how each boundary edge is supported (free / beam / wall /
/// neighboring slab) and which columns / walls / beams sit underneath it. Revit-facing —
/// the geometric predicates live in the Revit-free <c>Geometry2D</c> and are unit-tested.
/// </summary>
/// <summary>A slab directly above or below this one (overlapping in plan) — dowel/starter context.</summary>
public sealed class NeighborSlab
{
    public required long ElementId { get; init; }
    public string? Mark { get; init; }
    public double ThicknessIn { get; init; }
    public double GapFt { get; init; }
}

public sealed class SlabContext
{
    public required IReadOnlyList<BoundaryEdge> Edges { get; init; }
    public required IReadOnlyList<SupportBelow> Supports { get; init; }
    public NeighborSlab? SlabAbove { get; init; }
    public NeighborSlab? SlabBelow { get; init; }

    public IReadOnlyList<int> FreeEdgeIndices =>
        Edges.Where(e => e.Kind == EdgeKind.Free).Select(e => e.Index).ToList();

    // Heuristic tolerances (feet / radians).
    private const double AngTolRad      = 0.087;   // ~5°
    private const double EdgeOverlapFrac = 0.30;   // ≥30% of the edge must be covered to count
    private const double BeamOffsetTol   = 0.75;   // ft
    private const double WallExtraTol    = 0.25;   // ft, added to half the wall thickness
    private const double NeighborOffsetTol = 0.20; // ft, neighbor slab edges nearly coincide

    public static SlabContext For(SlabGeometry geom)
    {
        Floor floor = geom.Floor;
        Document doc = floor.Document;

        List<BoundaryEdge> edges = BuildEdges(geom);

        BoundingBoxXYZ? bb = floor.get_BoundingBox(null);
        (List<Wall> walls, List<Element> beams, List<Element> columns, List<Floor> neighborFloors) =
            CollectCandidates(doc, floor, bb);

        List<(long id, string? mark, List<Seg2> segs)> neighborEdges = NeighborEdges(neighborFloors);

        ClassifyEdges(edges, walls, beams, neighborEdges);

        var usedOnEdges = new HashSet<long>(edges.Where(e => e.AdjacentElementId != 0).Select(e => e.AdjacentElementId));
        List<SupportBelow> supports = FindSupports(geom, columns, walls, beams, usedOnEdges);

        return new SlabContext
        {
            Edges = edges,
            Supports = supports,
            SlabAbove = FindVerticalNeighbor(doc, floor, bb, above: true),
            SlabBelow = FindVerticalNeighbor(doc, floor, bb, above: false),
        };
    }

    private static NeighborSlab? FindVerticalNeighbor(Document doc, Floor self, BoundingBoxXYZ? bb, bool above)
    {
        if (bb is null) return null;
        const double pad = 1.0, band = 30.0;
        var min = new XYZ(bb.Min.X - pad, bb.Min.Y - pad, above ? bb.Max.Z + 0.01 : bb.Min.Z - band);
        var max = new XYZ(bb.Max.X + pad, bb.Max.Y + pad, above ? bb.Max.Z + band : bb.Min.Z - 0.01);
        var filter = new BoundingBoxIntersectsFilter(new Outline(min, max));

        NeighborSlab? best = null;
        double bestGap = double.MaxValue;
        foreach (Element e in new FilteredElementCollector(doc).OfClass(typeof(Floor)).WherePasses(filter))
        {
            if (e is not Floor f || f.Id == self.Id) continue;
            BoundingBoxXYZ? fb = f.get_BoundingBox(null);
            if (fb is null || !PlanOverlaps(bb, fb)) continue;

            double gap = above ? fb.Min.Z - bb.Max.Z : bb.Min.Z - fb.Max.Z;
            if (gap < -0.5 || gap >= bestGap) continue;

            bestGap = gap;
            double thick = (doc.GetElement(f.GetTypeId()) as FloorType)?.GetCompoundStructure()?.GetWidth() ?? 0;
            best = new NeighborSlab
            {
                ElementId = f.Id.Value,
                Mark = MarkOf(f),
                ThicknessIn = UnitConv.FtToIn(thick),
                GapFt = Math.Round(gap, 3),
            };
        }
        return best;
    }

    private static bool PlanOverlaps(BoundingBoxXYZ a, BoundingBoxXYZ b) =>
        a.Min.X < b.Max.X && a.Max.X > b.Min.X && a.Min.Y < b.Max.Y && a.Max.Y > b.Min.Y;

    // ── Boundary edges ────────────────────────────────────────────────────────

    private static List<BoundaryEdge> BuildEdges(SlabGeometry geom)
    {
        IReadOnlyList<Pt2> pts = geom.Outer.Points;
        int n = pts.Count;
        double sign = geom.Outer.SignedArea >= 0 ? 1 : -1;   // CCW → outward = (dy,-dx)

        var edges = new List<BoundaryEdge>(n);
        for (int i = 0; i < n; i++)
        {
            var seg = new Seg2(pts[i], pts[(i + 1) % n]);
            Pt2 d = seg.Dir;
            var outward = new Pt2(d.Y * sign, -d.X * sign);
            double deg = Math.Atan2(outward.Y, outward.X) * (180.0 / Math.PI);
            edges.Add(new BoundaryEdge { Index = i, Segment = seg, OutwardNormalDeg = deg });
        }
        return edges;
    }

    private static void ClassifyEdges(
        List<BoundaryEdge> edges, List<Wall> walls, List<Element> beams,
        List<(long id, string? mark, List<Seg2> segs)> neighbors)
    {
        foreach (BoundaryEdge edge in edges)
        {
            double need = EdgeOverlapFrac * edge.LengthFt;

            (long id, string? mark, double ov) wall = BestCenterlineMatch(
                edge.Segment, walls, w => w.Width * 0.5 + WallExtraTol);
            if (wall.ov >= need)
            {
                edge.Kind = EdgeKind.Wall; edge.AdjacentElementId = wall.id; edge.AdjacentMark = wall.mark;
                continue;
            }

            (long id, string? mark, double ov) beam = BestCenterlineMatch(
                edge.Segment, beams, _ => BeamOffsetTol);
            if (beam.ov >= need)
            {
                edge.Kind = EdgeKind.Beam; edge.AdjacentElementId = beam.id; edge.AdjacentMark = beam.mark;
                continue;
            }

            foreach ((long id, string? mark, List<Seg2> segs) in neighbors)
            {
                double best = 0;
                foreach (Seg2 s in segs)
                    best = Math.Max(best, Geometry2D.CollinearOverlapLength(edge.Segment, s, AngTolRad, NeighborOffsetTol));
                if (best >= need)
                {
                    edge.Kind = EdgeKind.Slab; edge.AdjacentElementId = id; edge.AdjacentMark = mark;
                    break;
                }
            }
        }
    }

    private static (long id, string? mark, double overlap) BestCenterlineMatch<T>(
        Seg2 edge, IEnumerable<T> elems, Func<T, double> offsetTol) where T : Element
    {
        long bestId = 0; string? bestMark = null; double bestOv = 0;
        foreach (T e in elems)
        {
            if (PlanCenterline(e) is not { } cl) continue;
            double ov = Geometry2D.CollinearOverlapLength(edge, cl, AngTolRad, offsetTol(e));
            if (ov > bestOv) { bestOv = ov; bestId = e.Id.Value; bestMark = MarkOf(e); }
        }
        return (bestId, bestMark, bestOv);
    }

    // ── Supports below ──────────────────────────────────────────────────────────

    private static List<SupportBelow> FindSupports(
        SlabGeometry geom, List<Element> columns, List<Wall> walls, List<Element> beams,
        HashSet<long> usedOnEdges)
    {
        IReadOnlyList<Pt2> outer = geom.Outer.Points;
        var supports = new List<SupportBelow>();

        foreach (Element col in columns)
        {
            if (PlanPoint(col) is not { } c) continue;
            if (!Geometry2D.PointInLoop(outer, c) || InAnyOpening(geom, c)) continue;

            BoundingBoxXYZ? cb = col.get_BoundingBox(null);
            supports.Add(new SupportBelow
            {
                Kind = SupportKind.Column,
                ElementId = col.Id.Value,
                Mark = MarkOf(col),
                CenterXY = c,
                WidthIn = cb is null ? 0 : UnitConv.FtToIn(cb.Max.X - cb.Min.X),
                DepthIn = cb is null ? 0 : UnitConv.FtToIn(cb.Max.Y - cb.Min.Y),
            });
        }

        AddLineSupports(walls, SupportKind.Wall, geom, outer, usedOnEdges, supports);
        AddLineSupports(beams, SupportKind.Beam, geom, outer, usedOnEdges, supports);

        return supports;
    }

    private static void AddLineSupports<T>(
        IEnumerable<T> elems, SupportKind kind, SlabGeometry geom, IReadOnlyList<Pt2> outer,
        HashSet<long> usedOnEdges, List<SupportBelow> supports) where T : Element
    {
        foreach (T e in elems)
        {
            if (usedOnEdges.Contains(e.Id.Value)) continue;     // already an edge support
            if (PlanCenterline(e) is not { } cl) continue;
            if (!Geometry2D.PointInLoop(outer, cl.Mid) || InAnyOpening(geom, cl.Mid)) continue;

            supports.Add(new SupportBelow
            {
                Kind = kind,
                ElementId = e.Id.Value,
                Mark = MarkOf(e),
                CenterXY = cl.Mid,
            });
        }
    }

    // ── Candidate collection ────────────────────────────────────────────────────

    private static (List<Wall>, List<Element>, List<Element>, List<Floor>) CollectCandidates(
        Document doc, Floor self, BoundingBoxXYZ? bb)
    {
        var walls = new List<Wall>();
        var beams = new List<Element>();
        var columns = new List<Element>();
        var neighbors = new List<Floor>();
        if (bb is null) return (walls, beams, columns, neighbors);

        const double pad = 1.0, zDown = 3.0, zUp = 0.5;
        var min = new XYZ(bb.Min.X - pad, bb.Min.Y - pad, bb.Min.Z - zDown);
        var max = new XYZ(bb.Max.X + pad, bb.Max.Y + pad, bb.Max.Z + zUp);
        var filter = new BoundingBoxIntersectsFilter(new Outline(min, max));

        foreach (Element e in new FilteredElementCollector(doc).OfClass(typeof(Wall)).WherePasses(filter))
            if (e is Wall w) walls.Add(w);

        foreach (Element e in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_StructuralFraming)
                     .WhereElementIsNotElementType().WherePasses(filter))
            beams.Add(e);

        var colFilter = new ElementMulticategoryFilter(
            new List<BuiltInCategory> { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns });
        foreach (Element e in new FilteredElementCollector(doc)
                     .WherePasses(colFilter).WhereElementIsNotElementType().WherePasses(filter))
            columns.Add(e);

        foreach (Element e in new FilteredElementCollector(doc).OfClass(typeof(Floor)).WherePasses(filter))
            if (e is Floor f && f.Id != self.Id) neighbors.Add(f);

        return (walls, beams, columns, neighbors);
    }

    private static List<(long, string?, List<Seg2>)> NeighborEdges(List<Floor> neighbors)
    {
        var list = new List<(long, string?, List<Seg2>)>();
        foreach (Floor f in neighbors)
        {
            try
            {
                SlabGeometry g = SlabGeometry.For(f);
                IReadOnlyList<Pt2> pts = g.Outer.Points;
                int n = pts.Count;
                var segs = new List<Seg2>(n);
                for (int i = 0; i < n; i++) segs.Add(new Seg2(pts[i], pts[(i + 1) % n]));
                list.Add((f.Id.Value, MarkOf(f), segs));
            }
            catch { /* neighbor without a readable sketch — skip */ }
        }
        return list;
    }

    // ── Small Revit helpers ─────────────────────────────────────────────────────

    private static Seg2? PlanCenterline(Element e)
    {
        if (e.Location is LocationCurve { Curve: { } c })
        {
            XYZ a = c.GetEndPoint(0);
            XYZ b = c.GetEndPoint(1);
            return new Seg2(new Pt2(a.X, a.Y), new Pt2(b.X, b.Y));
        }
        return null;
    }

    private static Pt2? PlanPoint(Element e)
    {
        if (e.Location is LocationPoint lp)
            return new Pt2(lp.Point.X, lp.Point.Y);

        BoundingBoxXYZ? bb = e.get_BoundingBox(null);
        if (bb is null) return null;
        return new Pt2((bb.Min.X + bb.Max.X) * 0.5, (bb.Min.Y + bb.Max.Y) * 0.5);
    }

    private static bool InAnyOpening(SlabGeometry geom, Pt2 p) =>
        geom.Openings.Any(o => Geometry2D.PointInLoop(o.Points, p));

    private static string? MarkOf(Element e)
    {
        string? m = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
        return string.IsNullOrWhiteSpace(m) ? null : m;
    }
}
