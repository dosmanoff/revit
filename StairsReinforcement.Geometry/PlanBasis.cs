namespace StairsReinforcement.Geometry;

/// <summary>
/// A horizontal local frame for a landing (like a small slab): origin + unit X/Y axes in plan,
/// X chosen along the longest boundary edge. Bar-direction tokens (BottomX/TopY) are relative to it.
/// </summary>
public sealed class PlanBasis
{
    public Pt2 Origin { get; }
    public Pt2 X { get; }
    public Pt2 Y { get; }
    public double AngleDeg { get; }

    public PlanBasis(Pt2 origin, Pt2 x)
    {
        Origin = origin;
        X = x.Normalized();
        Y = X.Perp();
        AngleDeg = Math.Atan2(X.Y, X.X) * 180.0 / Math.PI;
    }

    /// <summary>Basis whose X runs along the longest edge of the loop; origin at the loop's min corner.</summary>
    public static PlanBasis FromLongestEdge(IReadOnlyList<Pt2> loop)
    {
        if (loop.Count < 2) return new PlanBasis(loop.Count == 1 ? loop[0] : new Pt2(0, 0), new Pt2(1, 0));

        Pt2 bestDir = new(1, 0);
        double bestLen = -1;
        for (int i = 0; i < loop.Count; i++)
        {
            Pt2 a = loop[i], b = loop[(i + 1) % loop.Count];
            Pt2 d = b - a;
            double len = d.Length;
            if (len > bestLen) { bestLen = len; bestDir = d; }
        }

        double minX = double.MaxValue, minY = double.MaxValue;
        foreach (Pt2 p in loop) { minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); }
        return new PlanBasis(new Pt2(minX, minY), bestDir);
    }

    /// <summary>Shoelace area of a closed loop (absolute value).</summary>
    public static double Area(IReadOnlyList<Pt2> loop)
    {
        if (loop.Count < 3) return 0;
        double sum = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            Pt2 a = loop[i], b = loop[(i + 1) % loop.Count];
            sum += a.Cross(b);
        }
        return Math.Abs(sum) / 2.0;
    }
}
