namespace StairsReinforcement.Geometry;

/// <summary>
/// Groups axis-aligned 3-D boxes into connected components: two boxes are connected when their
/// separation on every axis is within a gap (i.e. they overlap or nearly touch). Used to split a
/// set of selected floors into distinct floor-modelled stairs — the flights and landings of one
/// stair overlap (directly or transitively through a shared landing), while separate stairs do not.
/// Pure / unit-testable.
/// </summary>
public static class BoxCluster
{
    public readonly record struct Box(Pt3 Min, Pt3 Max);

    /// <summary>Connected-component grouping; returns index lists, each sorted, ordered by first index.</summary>
    public static List<List<int>> Group(IReadOnlyList<Box> boxes, double gap)
    {
        int n = boxes.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        int Find(int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }
        void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) parent[ra] = rb;
        }

        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (Connected(boxes[i], boxes[j], gap))
                    Union(i, j);

        var groups = new Dictionary<int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            int r = Find(i);
            if (!groups.TryGetValue(r, out List<int>? list)) { list = new List<int>(); groups[r] = list; }
            list.Add(i);
        }

        return groups.Values
            .Select(g => { g.Sort(); return g; })
            .OrderBy(g => g[0])
            .ToList();
    }

    private static bool Connected(Box a, Box b, double gap) =>
        Sep(a.Min.X, a.Max.X, b.Min.X, b.Max.X) <= gap &&
        Sep(a.Min.Y, a.Max.Y, b.Min.Y, b.Max.Y) <= gap &&
        Sep(a.Min.Z, a.Max.Z, b.Min.Z, b.Max.Z) <= gap;

    /// <summary>Gap between two 1-D intervals (0 if they overlap or touch).</summary>
    private static double Sep(double aMin, double aMax, double bMin, double bMax) =>
        Math.Max(0, Math.Max(aMin - bMax, bMin - aMax));
}
