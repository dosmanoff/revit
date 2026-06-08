namespace SlabReinforcement.Geometry;

/// <summary>One clipped bar position in the bar's local frame: a scan line at <see cref="Perp"/>
/// running from <see cref="Start"/> to <see cref="End"/> along the bar direction.</summary>
public readonly struct LocalRail
{
    public double Perp { get; }    // local-Y (scan position, perpendicular to the bars)
    public double Start { get; }   // local-X start
    public double End { get; }     // local-X end

    public LocalRail(double perp, double start, double end) { Perp = perp; Start = start; End = end; }

    public double Length => End - Start;
}

/// <summary>A run of equally-spaced parallel bars of the same length — one Revit rebar set.</summary>
public readonly struct Band
{
    public double Perp0 { get; }   // perpendicular position of the first bar (minimum)
    public int Count { get; }      // number of bar positions
    public double Spacing { get; }
    public double Start { get; }   // local-X extent (shared by every bar)
    public double End { get; }

    public Band(double perp0, int count, double spacing, double start, double end)
    {
        Perp0 = perp0; Count = count; Spacing = spacing; Start = start; End = end;
    }

    public double Length => End - Start;
}

/// <summary>
/// Pure layout math for a slab field mat: scan-line clipping of parallel bars to the slab
/// boundary minus openings, grouping them into uniform bands (one rebar set each), and splitting
/// long runs at a max bar length with laps. No Revit dependency — fully unit-tested.
/// </summary>
public static class FieldLayout
{
    /// <summary>
    /// Split a 1-D run of length <paramref name="length"/> into bar segments (start,end) along
    /// the run, each ≤ <paramref name="maxLen"/>, adjacent segments overlapping by <paramref name="lap"/>.
    /// <paramref name="firstBarLen"/> caps the first segment (pass <c>maxLen/2</c> to stagger).
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
            if (next <= s + 1e-9) next = e;
            s = next;
        }
        return segs;
    }

    /// <summary>
    /// Clipped bar positions in the bar's local frame (local-X = <paramref name="dir"/>), spaced
    /// <paramref name="spacing"/> apart, clipped to <paramref name="outer"/> minus
    /// <paramref name="holes"/>; scan positions inset by <paramref name="sideInset"/>, ends pulled
    /// in by <paramref name="endInset"/>.
    /// </summary>
    public static List<LocalRail> LocalRails(
        Loop2 outer, IReadOnlyList<Loop2> holes, Pt2 dir,
        double spacing, double sideInset, double endInset)
    {
        var result = new List<LocalRail>();
        if (spacing <= 1e-9) return result;

        Pt2 u = dir.Normalized;
        double cos = u.X, sin = u.Y;

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
                result.Add(new LocalRail(y, xa, xb));
            }
        }
        return result;
    }

    /// <summary>Clipped bars as world-XY segments (FieldMode=Bars). Thin wrapper over
    /// <see cref="LocalRails"/>.</summary>
    public static List<Seg2> Rails(
        Loop2 outer, IReadOnlyList<Loop2> holes, Pt2 dir,
        double spacing, double sideInset, double endInset)
    {
        Pt2 u = dir.Normalized;
        double cos = u.X, sin = u.Y;
        var rails = LocalRails(outer, holes, dir, spacing, sideInset, endInset);
        var result = new List<Seg2>(rails.Count);
        foreach (LocalRail r in rails)
            result.Add(new Seg2(ToWorld(new Pt2(r.Start, r.Perp), cos, sin),
                                ToWorld(new Pt2(r.End, r.Perp), cos, sin)));
        return result;
    }

    /// <summary>
    /// Group clipped rails into uniform <see cref="Band"/>s — maximal runs of bars sharing the same
    /// local-X extent at consecutive scan positions (one rebar set each). Around a hole the bars
    /// split into the bands above, below, left and right of it.
    /// </summary>
    public static List<Band> Bands(IReadOnlyList<LocalRail> rails, double spacing)
    {
        var bands = new List<Band>();
        if (rails.Count == 0) return bands;

        const double extentTol = 1e-3;
        double stepTol = Math.Max(1e-3, spacing * 0.1);

        // Bucket by extent (rounded), then split each bucket into runs of consecutive scan lines.
        var byExtent = rails
            .GroupBy(r => (Math.Round(r.Start / extentTol), Math.Round(r.End / extentTol)));

        foreach (var group in byExtent)
        {
            List<LocalRail> g = group.OrderBy(r => r.Perp).ToList();
            int runStart = 0;
            for (int i = 1; i <= g.Count; i++)
            {
                bool breakHere = i == g.Count || Math.Abs(g[i].Perp - g[i - 1].Perp - spacing) > stepTol;
                if (!breakHere) continue;

                int count = i - runStart;
                LocalRail f = g[runStart];
                bands.Add(new Band(f.Perp, count, spacing, f.Start, f.End));
                runStart = i;
            }
        }
        return bands;
    }

    /// <summary>
    /// Clip a world-XY segment to the slab footprint (<paramref name="outer"/> minus
    /// <paramref name="holes"/>), returning the sub-segments that lie on concrete. Used to stop
    /// additional-group and opening-trim bars from projecting past the slab edge or over a void
    /// ("one or more rebar is completely outside its host"). A segment fully outside returns an
    /// empty list; a segment crossing a hole splits into the pieces either side.
    /// </summary>
    public static List<Seg2> ClipToFootprint(Seg2 seg, Loop2 outer, IReadOnlyList<Loop2> holes)
    {
        var result = new List<Seg2>();
        Pt2 a = seg.A;
        Pt2 d = seg.B - seg.A;
        if (d.Length < 1e-9) return result;

        var ts = new List<double> { 0.0, 1.0 };
        AddCrossings(a, d, outer.Points, ts);
        foreach (Loop2 h in holes) AddCrossings(a, d, h.Points, ts);
        ts.Sort();

        for (int i = 0; i + 1 < ts.Count; i++)
        {
            double t0 = ts[i], t1 = ts[i + 1];
            if (t1 - t0 < 1e-9) continue;

            Pt2 mid = a + d * ((t0 + t1) * 0.5);
            if (!Geometry2D.PointInLoop(outer.Points, mid)) continue;
            bool inHole = false;
            foreach (Loop2 h in holes)
                if (Geometry2D.PointInLoop(h.Points, mid)) { inHole = true; break; }
            if (inHole) continue;

            Pt2 pa = a + d * t0, pb = a + d * t1;
            if ((pb - pa).Length > 1e-6) result.Add(new Seg2(pa, pb));
        }
        return result;
    }

    // ── internals ────────────────────────────────────────────────────────────────

    /// <summary>Add to <paramref name="ts"/> the parameters t∈(0,1) at which the ray
    /// <paramref name="a"/>+t·<paramref name="d"/> crosses an edge of <paramref name="loop"/>.</summary>
    private static void AddCrossings(Pt2 a, Pt2 d, IReadOnlyList<Pt2> loop, List<double> ts)
    {
        int n = loop.Count;
        for (int i = 0; i < n; i++)
        {
            Pt2 p = loop[i], q = loop[(i + 1) % n];
            Pt2 e = q - p;
            double denom = d.Cross(e);
            if (Math.Abs(denom) < 1e-12) continue;            // parallel
            double t = (p - a).Cross(e) / denom;
            double s = (p - a).Cross(d) / denom;
            if (s >= -1e-9 && s <= 1 + 1e-9 && t > 1e-9 && t < 1 - 1e-9) ts.Add(t);
        }
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

    private static Pt2 ToLocal(Pt2 p, double cos, double sin) => new(p.X * cos + p.Y * sin, -p.X * sin + p.Y * cos);
    private static Pt2 ToWorld(Pt2 q, double cos, double sin) => new(q.X * cos - q.Y * sin, q.X * sin + q.Y * cos);
}
