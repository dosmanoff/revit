namespace StairsReinforcement.Geometry;

/// <summary>
/// Splits a long 1-D run (e.g. a flight longitudinal bar measured along the slope) into
/// segments no longer than the maximum bar length, overlapping by a lap. Pure / testable.
/// </summary>
public static class BarSplitter
{
    public readonly record struct Segment(double Start, double End)
    {
        public double Length => End - Start;
    }

    private const double Eps = 1e-9;

    /// <summary>
    /// Split <paramref name="length"/> into N equal overlapping segments, each ≤
    /// <paramref name="maxLen"/>, consecutive segments sharing a <paramref name="lap"/> overlap.
    /// Returns a single full-length segment when no split is needed.
    /// </summary>
    public static IReadOnlyList<Segment> Split(double length, double maxLen, double lap)
    {
        if (length <= Eps) return [];
        if (maxLen <= Eps || length <= maxLen + Eps) return [new Segment(0, length)];

        // Guard: a lap >= maxLen can never advance — clamp so we still terminate.
        double effLap = Math.Min(lap, maxLen * 0.5);
        double advance = maxLen - effLap;

        int n = Math.Max(1, (int)Math.Ceiling((length - effLap) / advance));
        // Equal segments: n*segLen - (n-1)*lap = length  ⇒  segLen = (length + (n-1)*lap) / n
        double segLen = (length + (n - 1) * effLap) / n;

        var segs = new List<Segment>(n);
        for (int i = 0; i < n; i++)
        {
            double start = i * (segLen - effLap);
            double end = Math.Min(start + segLen, length);
            segs.Add(new Segment(start, end));
        }
        return segs;
    }
}
