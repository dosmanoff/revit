namespace SlabReinforcement.Geometry;

/// <summary>
/// The plan basis of a slab: <see cref="X"/> along the longest boundary edge,
/// <see cref="Y"/> = 90° CCW from it (equivalent to BasisZ × X in 3-D).
/// </summary>
public readonly struct PlanBasis
{
    public Pt2 X { get; }
    public Pt2 Y { get; }

    public PlanBasis(Pt2 x)
    {
        X = x.Normalized;
        Y = X.Perp;
    }

    /// <summary>Angle of the X axis from world +X, in degrees, range (-180, 180].</summary>
    public double AngleDeg => Math.Atan2(X.Y, X.X) * (180.0 / Math.PI);
}

/// <summary>Pure 2-D geometry helpers — no Revit dependency, fully unit-testable.</summary>
public static class GeometryMath
{
    /// <summary>Shoelace signed area; positive for counter-clockwise winding.</summary>
    public static double SignedArea(IReadOnlyList<Pt2> pts)
    {
        if (pts.Count < 3) return 0;

        double sum = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            Pt2 a = pts[i];
            Pt2 b = pts[(i + 1) % pts.Count];
            sum += a.Cross(b);
        }
        return sum * 0.5;
    }

    /// <summary>
    /// Index of the loop with the largest absolute area, or -1 if the list is empty.
    /// Used to pick the outer boundary among a floor's sketch loops (the rest are openings).
    /// </summary>
    public static int LargestLoopIndex(IReadOnlyList<Loop2> loops)
    {
        int best = -1;
        double bestArea = double.NegativeInfinity;
        for (int i = 0; i < loops.Count; i++)
        {
            double a = loops[i].Area;
            if (a > bestArea) { bestArea = a; best = i; }
        }
        return best;
    }

    /// <summary>
    /// Direction (unit vector) of the longest edge of the closed polygon, including the
    /// wrap edge from the last vertex back to the first. Ties resolve to the first edge
    /// encountered. Returns +X for a degenerate loop.
    /// </summary>
    public static Pt2 LongestEdgeDirection(IReadOnlyList<Pt2> pts)
    {
        if (pts.Count < 2) return new Pt2(1, 0);

        Pt2 longest = new(0, 0);
        double maxLen = -1;
        for (int i = 0; i < pts.Count; i++)
        {
            Pt2 edge = pts[(i + 1) % pts.Count] - pts[i];
            double len = edge.Length;
            if (len > maxLen)
            {
                maxLen = len;
                longest = edge;
            }
        }

        return maxLen < 1e-12 ? new Pt2(1, 0) : longest.Normalized;
    }

    /// <summary>Plan basis whose X axis is the loop's longest edge.</summary>
    public static PlanBasis BasisFromLoop(IReadOnlyList<Pt2> pts) =>
        new(LongestEdgeDirection(pts));
}
