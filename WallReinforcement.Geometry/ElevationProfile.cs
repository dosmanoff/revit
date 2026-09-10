namespace WallReinforcement.Geometry;

/// <summary>A point in a wall's elevation (u along length, v up the height), feet.</summary>
public readonly record struct UVPoint(double U, double V);

/// <summary>
/// A wall's elevation outline as a closed polygon in (u,v). Used to clip field bars to the real
/// wall shape so a non-rectangular wall (e.g. a slanted end or top) is reinforced without bars
/// poking outside the concrete. Pure (Revit-free) and unit-tested; scan-line crossings use the
/// even-odd rule so the polygon may be concave.
/// </summary>
public class ElevationProfile
{
    private readonly IReadOnlyList<UVPoint> _poly;   // closed ring (last point implicitly joins first)

    public ElevationProfile(IReadOnlyList<UVPoint> polygon) => _poly = polygon;

    /// <summary>Inside v-interval(s) of a vertical scan line at length <paramref name="u"/>.</summary>
    public List<Interval> VerticalSpansAt(double u) =>
        Spans(u, p => p.U, p => p.V);

    /// <summary>Inside u-interval(s) of a horizontal scan line at height <paramref name="v"/>.</summary>
    public List<Interval> HorizontalSpansAt(double v) =>
        Spans(v, p => p.V, p => p.U);

    /// <summary>True when the outline is an axis-aligned rectangle (the common plumb wall), so the
    /// caller can keep the efficient uniform-set path.</summary>
    public bool IsAxisAlignedRect(double eps = 1e-4)
    {
        double uMin = double.MaxValue, uMax = double.MinValue, vMin = double.MaxValue, vMax = double.MinValue;
        foreach (UVPoint p in _poly)
        {
            uMin = Math.Min(uMin, p.U); uMax = Math.Max(uMax, p.U);
            vMin = Math.Min(vMin, p.V); vMax = Math.Max(vMax, p.V);
        }
        // Every vertex must sit on a corner of the bounding box, and every edge be axis-aligned.
        foreach (UVPoint p in _poly)
        {
            bool onU = Math.Abs(p.U - uMin) < eps || Math.Abs(p.U - uMax) < eps;
            bool onV = Math.Abs(p.V - vMin) < eps || Math.Abs(p.V - vMax) < eps;
            if (!(onU && onV)) return false;
        }
        return true;
    }

    private List<Interval> Spans(double scan, Func<UVPoint, double> axis, Func<UVPoint, double> other)
    {
        var cross = new List<double>();
        int n = _poly.Count;
        for (int i = 0; i < n; i++)
        {
            UVPoint a = _poly[i], b = _poly[(i + 1) % n];
            double aa = axis(a), ba = axis(b);
            // Half-open crossing test so each shared vertex is counted exactly once.
            if ((aa <= scan && ba > scan) || (ba <= scan && aa > scan))
            {
                double t = (scan - aa) / (ba - aa);
                cross.Add(other(a) + t * (other(b) - other(a)));
            }
        }
        cross.Sort();
        var spans = new List<Interval>();
        for (int i = 0; i + 1 < cross.Count; i += 2)
            if (cross[i + 1] - cross[i] > 1e-9)
                spans.Add(new Interval(cross[i], cross[i + 1]));
        return spans;
    }
}
