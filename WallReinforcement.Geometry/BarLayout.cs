namespace WallReinforcement.Geometry;

/// <summary>
/// Pure (Revit-free) bar-distribution math shared by the rebar builders. A "set" of equally
/// spaced parallel bars is described by a <c>count</c> and an exact <c>spacing</c>; this is the
/// logic that turns a span + desired pitch into a Revit <c>SetLayoutAsNumberWithSpacing</c> call.
/// Kept in its own assembly so it is unit-testable without a Revit install.
/// </summary>
public static class BarLayout
{
    /// <summary>
    /// Uniform set layout spanning [<paramref name="from"/>, <paramref name="to"/>] with steps no
    /// larger than <paramref name="desiredStep"/>: returns the bar <c>count</c> (both endpoints
    /// included), the exact <c>spacing</c> that divides the span evenly (≤ desiredStep), and the
    /// <c>first</c> position. Returns count 0 for an empty or degenerate span.
    /// </summary>
    public static (int count, double spacing, double first) UniformLayout(double from, double to, double desiredStep)
    {
        if (to <= from || desiredStep <= 0) return (0, 0, from);
        double span = to - from;
        int n = Math.Max(1, (int)Math.Ceiling(span / desiredStep));
        return (n + 1, span / n, from);
    }

    /// <summary>
    /// Positions in [<paramref name="from"/>, <paramref name="to"/>] such that no two adjacent
    /// positions are more than <paramref name="step"/> apart; both endpoints are included. Yields
    /// nothing when the range is empty or the step is non-positive.
    /// </summary>
    public static IEnumerable<double> EvenlySpaced(double from, double to, double step)
    {
        if (to <= from || step <= 0) yield break;
        double span = to - from;
        int n = Math.Max(1, (int)Math.Ceiling(span / step));
        double actualStep = span / n;
        for (int i = 0; i <= n; i++) yield return from + i * actualStep;
    }
}
