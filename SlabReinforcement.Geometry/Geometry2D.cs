namespace SlabReinforcement.Geometry;

/// <summary>
/// Pure 2-D predicates used to classify slab boundary edges (does a wall/beam/neighbor
/// run along this edge?) and supports (is a column/wall inside the footprint?). No Revit
/// dependency — fully unit-testable.
/// </summary>
public static class Geometry2D
{
    /// <summary>
    /// Even-odd (ray-casting) point-in-polygon test. Points exactly on the boundary are
    /// unspecified — callers test interior points (segment midpoints, column centers).
    /// </summary>
    public static bool PointInLoop(IReadOnlyList<Pt2> poly, Pt2 p)
    {
        bool inside = false;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            Pt2 a = poly[i];
            Pt2 b = poly[j];
            bool straddlesY = (a.Y > p.Y) != (b.Y > p.Y);
            if (!straddlesY) continue;

            double t = (p.Y - a.Y) / (b.Y - a.Y);
            double xCross = a.X + t * (b.X - a.X);
            if (p.X < xCross) inside = !inside;
        }
        return inside;
    }

    /// <summary>Shortest distance from a point to a finite segment.</summary>
    public static double DistancePointToSegment(Pt2 p, Seg2 s)
    {
        Pt2 ab = s.Delta;
        double len2 = ab.Dot(ab);
        if (len2 < 1e-18) return (p - s.A).Length;

        double t = (p - s.A).Dot(ab) / len2;
        t = Math.Clamp(t, 0, 1);
        Pt2 proj = s.A + ab * t;
        return (p - proj).Length;
    }

    /// <summary>
    /// Length over which <paramref name="other"/> runs along <paramref name="reference"/>:
    /// near-parallel (direction within <paramref name="angTolRad"/>, either orientation),
    /// small perpendicular offset (both endpoints ≤ <paramref name="offsetTol"/> from the
    /// reference line), measured as the overlap of their projections onto the reference
    /// direction. Returns 0 when they are not collinear/overlapping.
    /// </summary>
    public static double CollinearOverlapLength(Seg2 reference, Seg2 other, double angTolRad, double offsetTol)
    {
        double refLen = reference.Length;
        if (refLen < 1e-9 || other.Length < 1e-9) return 0;

        Pt2 dir = reference.Dir;

        // Parallel within tolerance (allow opposite direction).
        double cosang = Math.Abs(dir.Dot(other.Dir));
        if (cosang < Math.Cos(angTolRad)) return 0;

        // Perpendicular offset of other's endpoints from the reference line.
        if (Math.Abs(PerpDistance(reference.A, dir, other.A)) > offsetTol) return 0;
        if (Math.Abs(PerpDistance(reference.A, dir, other.B)) > offsetTol) return 0;

        // Overlap of the projections onto the reference direction.
        double o0 = (other.A - reference.A).Dot(dir);
        double o1 = (other.B - reference.A).Dot(dir);
        double oLo = Math.Min(o0, o1);
        double oHi = Math.Max(o0, o1);

        double lo = Math.Max(0, oLo);
        double hi = Math.Min(refLen, oHi);
        return Math.Max(0, hi - lo);
    }

    /// <summary>Signed perpendicular distance from point <paramref name="p"/> to the line
    /// through <paramref name="origin"/> with unit direction <paramref name="unitDir"/>.</summary>
    private static double PerpDistance(Pt2 origin, Pt2 unitDir, Pt2 p) =>
        (p - origin).Cross(unitDir);
}
