using SlabReinforcement.Geometry;

namespace SlabReinforcement.Domain;

public enum OpeningSource { Void }

/// <summary>What kind of penetration an opening is — drives whether it gets trim bars.</summary>
public enum OpeningClass
{
    /// <summary>An isolated small/medium penetration → trim bars + diagonals.</summary>
    Trim,
    /// <summary>A large opening (stair/elevator shaft) → edge-reinforced, NOT trimmed.</summary>
    Shaft,
    /// <summary>Close to the slab edge or to a large opening → trim is redundant, skip it.</summary>
    EdgeAdjacent,
}

/// <summary>A penetration through the slab (a hole in the concrete), with its plan boundary,
/// classification and a trim flag.</summary>
public sealed class SlabOpening
{
    public required OpeningSource Source { get; init; }
    public required Loop2 Boundary { get; init; }      // world XY, feet
    public required OpeningClass Class { get; init; }
    public required string ClassReason { get; init; }

    public bool NeedsTrim => Class == OpeningClass.Trim;
    public Bounds2 Bounds => Boundary.Bounds;
    public double AreaSf => Boundary.Area;
}

/// <summary>
/// The slab's openings (from <see cref="SlabGeometry.Openings"/>), each classified so trim is
/// placed only where it's actually useful: large shafts and openings hard against the slab edge
/// or another large opening are NOT trimmed (addresses the "excessive opening reinforcement"
/// feedback). Detection is geometry-based, so it covers openings of any shape/authoring method.
/// </summary>
public static class SlabOpenings
{
    public const double ShaftMinDimFt = 3.5;       // a plan dimension this large ⇒ shaft
    public const double ShaftAreaSf = 16.0;        // …or this much area
    public const double EdgeProximityFt = 1.5;     // gap to slab edge / big opening below which trim is redundant
    public const double BigOpeningSf = 16.0;       // a neighbouring opening "big" enough to suppress trim

    public static IReadOnlyList<SlabOpening> For(SlabGeometry geom)
    {
        IReadOnlyList<Loop2> holes = geom.Openings;
        var result = new List<SlabOpening>(holes.Count);
        for (int i = 0; i < holes.Count; i++)
        {
            (OpeningClass cls, string reason) = Classify(holes[i], geom.Outer, holes, i);
            result.Add(new SlabOpening
            {
                Source = OpeningSource.Void,
                Boundary = holes[i],
                Class = cls,
                ClassReason = reason,
            });
        }
        return result;
    }

    public static int TrimCount(IReadOnlyList<SlabOpening> openings) => openings.Count(o => o.NeedsTrim);

    private static (OpeningClass, string) Classify(Loop2 op, Loop2 outer, IReadOnlyList<Loop2> all, int idx)
    {
        double maxDim = Math.Max(op.Bounds.Width, op.Bounds.Height);
        if (maxDim >= ShaftMinDimFt || op.Area >= ShaftAreaSf)
            return (OpeningClass.Shaft, $"large ({op.Area:0.#} sf, {maxDim:0.#} ft) — shaft, edge-reinforced not trimmed");

        double dEdge = MinGap(op.Points, outer.Points);
        if (dEdge < EdgeProximityFt)
            return (OpeningClass.EdgeAdjacent, $"hard against the slab edge ({dEdge:0.0} ft) — edge bars cover it");

        for (int j = 0; j < all.Count; j++)
        {
            if (j == idx || all[j].Area < BigOpeningSf) continue;
            if (MinGap(op.Points, all[j].Points) < EdgeProximityFt)
                return (OpeningClass.EdgeAdjacent, "adjacent to a large opening — trim would be redundant");
        }

        return (OpeningClass.Trim, "isolated penetration");
    }

    /// <summary>Smallest distance between two polygons, approximated as min point-to-edge distance both ways.</summary>
    private static double MinGap(IReadOnlyList<Pt2> a, IReadOnlyList<Pt2> b)
    {
        double best = double.MaxValue;
        best = Math.Min(best, MinPointToLoop(a, b));
        best = Math.Min(best, MinPointToLoop(b, a));
        return best;
    }

    private static double MinPointToLoop(IReadOnlyList<Pt2> pts, IReadOnlyList<Pt2> loop)
    {
        double best = double.MaxValue;
        int n = loop.Count;
        foreach (Pt2 p in pts)
            for (int i = 0; i < n; i++)
                best = Math.Min(best, Geometry2D.DistancePointToSegment(p, new Seg2(loop[i], loop[(i + 1) % n])));
        return best;
    }
}
