namespace WallReinforcement.Geometry;

/// <summary>A closed 1-D interval [<see cref="From"/>, <see cref="To"/>] (feet).</summary>
public readonly record struct Interval(double From, double To)
{
    public double Length => To - From;
    public bool IsEmpty(double eps = 1e-9) => To - From <= eps;
}

/// <summary>
/// Pure (Revit-free) 1-D interval math used to lay rebar around obstructions — chiefly to split a
/// row of ties into clear runs that skip wall openings. Kept in its own assembly so it is
/// unit-testable without a Revit install. See <see cref="Engine.TransverseTieBuilder"/>.
/// </summary>
public static class IntervalMath
{
    /// <summary>
    /// Merge a set of intervals into the minimal sorted set of non-overlapping intervals. Intervals
    /// that touch or overlap (gap ≤ <paramref name="eps"/>) are coalesced. Degenerate inputs
    /// (From ≥ To) are ignored.
    /// </summary>
    public static List<Interval> Merge(IEnumerable<Interval> intervals, double eps = 1e-9)
    {
        var sorted = intervals.Where(i => i.To - i.From > eps)
                              .OrderBy(i => i.From)
                              .ToList();
        var merged = new List<Interval>();
        foreach (Interval iv in sorted)
        {
            if (merged.Count > 0 && iv.From <= merged[^1].To + eps)
            {
                Interval last = merged[^1];
                if (iv.To > last.To) merged[^1] = last with { To = iv.To };
            }
            else
            {
                merged.Add(iv);
            }
        }
        return merged;
    }

    /// <summary>
    /// Subtract <paramref name="blocked"/> from the span [<paramref name="from"/>,
    /// <paramref name="to"/>] and return the clear sub-intervals, left to right. Blocked intervals
    /// need not be sorted or disjoint — they are merged first. Returns the whole span when nothing
    /// is blocked, and an empty list when the span is fully covered or degenerate.
    /// </summary>
    public static List<Interval> Subtract(double from, double to, IEnumerable<Interval> blocked, double eps = 1e-9)
    {
        var clear = new List<Interval>();
        if (to - from <= eps) return clear;

        double cursor = from;
        foreach (Interval b in Merge(blocked, eps))
        {
            if (b.To <= cursor + eps) continue;          // entirely left of the cursor
            if (b.From >= to - eps) break;               // entirely right of the span
            if (b.From > cursor + eps)
                clear.Add(new Interval(cursor, Math.Min(b.From, to)));
            cursor = Math.Max(cursor, b.To);
            if (cursor >= to - eps) break;
        }
        if (cursor < to - eps) clear.Add(new Interval(cursor, to));
        return clear;
    }
}
