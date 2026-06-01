namespace SlabReinforcement.Geometry;

/// <summary>
/// Pure layout math for a slab field mat: scan-line clipping of parallel bars to the slab
/// boundary minus openings, and splitting long runs at a max bar length with laps. No Revit
/// dependency — fully unit-tested. The engine maps the results onto a plane and creates rebar.
/// </summary>
public static class FieldLayout
{
    /// <summary>
    /// Split a 1-D run of length <paramref name="length"/> into bar segments (start,end) along
    /// the run, each no longer than <paramref name="maxLen"/>, adjacent segments overlapping by
    /// <paramref name="lap"/>. <paramref name="firstBarLen"/> caps the first segment (pass
    /// <c>maxLen/2</c> to stagger splices on alternating rails); ≤ 0 means use <paramref name="maxLen"/>.
    /// </summary>
    public static List<(double Start, double End)> SplitWithLaps(
        double length, double maxLen, double lap, double firstBarLen = 0)
    {
        var segs = new List<(double, double)>();
        if (length <= 1e-9) return segs;
        if (maxLen <= 1e-9) { segs.Add((0, length)); return segs; }

        double cap0 = firstBarLen <= 1e-9 ? maxLen : Math.Min(firstBarLen, maxLen);

        double s = 0;
        bool first = true;
        int guard = 0;
        while (guard++ < 100_000)
        {
            double cap = first ? cap0 : maxLen;
            double e = Math.Min(s + cap, length);
            segs.Add((s, e));
            first = false;
            if (e >= length - 1e-9) break;

            double next = e - lap;
            if (next <= s + 1e-9) next = e;   // lap ≥ bar (degenerate): butt joint, keep advancing
            s = next;
        }
        return segs;
    }

    /// <summary>
    /// Parallel bars (as world-XY segments) running along <paramref name="dir"/>, spaced
    /// <paramref name="spacing"/> apart across the slab, clipped to <paramref name="outer"/> minus
    /// <paramref name="holes"/>. Scan positions are inset from the perpendicular edges by
    /// <paramref name="sideInset"/>; each bar's ends are pulled in by <paramref name="endInset"/>.
    /// </summary>
    public static List<Seg2> Rails(
        Loop2 outer, IReadOnlyList<Loop2> holes, Pt2 dir,
        double spacing, double sideInset, double endInset)
    {
        var result = new List<Seg2>();
        if (spacing <= 1e-9) return result;

        Pt2 u = dir.Normalized;
        double cos = u.X, sin = u.Y;     // local frame: +X along the bar direction

        var loops = new List<List<Pt2>> { outer.Points.Select(p => ToLocal(p, cos, sin)).ToList() };
        foreach (Loop2 h in holes) loops.Add(h.Points.Select(p => ToLocal(p, cos, sin)).ToList());

        double yMin = double.MaxValue, yMax = double.MinValue;
        foreach (Pt2 p in loops[0]) { yMin = Math.Min(yMin, p.Y); yMax = Math.Max(yMax, p.Y); }

        for (double y = yMin + sideInset; y <= yMax - sideInset + 1e-9; y += spacing)
        {
            List<double> xs = ScanCrossings(loops, y);
            xs.Sort();
            for (int i = 0; i + 1 < xs.Count; i += 2)
            {
                double xa = xs[i] + endInset;
                double xb = xs[i + 1] - endInset;
                if (xb - xa < 1e-6) continue;
                result.Add(new Seg2(ToWorld(new Pt2(xa, y), cos, sin), ToWorld(new Pt2(xb, y), cos, sin)));
            }
        }
        return result;
    }

    private static List<double> ScanCrossings(List<List<Pt2>> loops, double y)
    {
        var xs = new List<double>();
        foreach (List<Pt2> loop in loops)
        {
            int n = loop.Count;
            for (int i = 0; i < n; i++)
            {
                Pt2 a = loop[i];
                Pt2 b = loop[(i + 1) % n];
                bool straddles = (a.Y <= y && b.Y > y) || (b.Y <= y && a.Y > y);
                if (!straddles) continue;
                double t = (y - a.Y) / (b.Y - a.Y);
                xs.Add(a.X + t * (b.X - a.X));
            }
        }
        return xs;
    }

    // Rotate world → local (so the bar direction becomes +X) and back.
    private static Pt2 ToLocal(Pt2 p, double cos, double sin) => new(p.X * cos + p.Y * sin, -p.X * sin + p.Y * cos);
    private static Pt2 ToWorld(Pt2 q, double cos, double sin) => new(q.X * cos - q.Y * sin, q.X * sin + q.Y * cos);
}
