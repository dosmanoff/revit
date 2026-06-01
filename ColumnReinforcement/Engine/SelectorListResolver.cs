using System.Globalization;

namespace ColumnReinforcement.Engine;

/// <summary>
/// Generic per-cage-position resolver for selector-list CSV fields. Reuses the same
/// selector vocabulary as <see cref="LongitudinalBarBuilder.ResolveTopModes"/>:
/// space/<c>;</c>-separated <c>selector:value</c> tokens where the selector is a
/// 0-based bar index or one of <c>all</c>, <c>corners</c>, <c>edges</c>,
/// <c>+x</c>, <c>-x</c>, <c>+y</c>, <c>-y</c>. Precedence: explicit index &gt; face/group keyword
/// &gt; default.
///
/// <para>Reads the same face/corner classification as <c>ResolveTopModes</c> via
/// <see cref="LongitudinalBarBuilder.ClassifyExtremal"/> and
/// <see cref="LongitudinalBarBuilder.ClassifyFaces"/>. Note: tokens are split by
/// space/<c>;</c> ONLY — comma is NOT a separator here, so values may contain commas
/// (e.g. <c>0:(-3.56,-3.56)</c>). The owning CSV field must therefore be quoted in
/// the CSV row when the value embeds a comma.</para>
/// </summary>
public static class SelectorListResolver
{
    public delegate bool TokenParser<T>(string token, out T value);

    /// <summary>
    /// Resolve a selector-list spec into a per-position array, sized to
    /// <paramref name="positions"/>.Count. Every element starts as <paramref name="defaultValue"/>;
    /// matching tokens override. A null/empty <paramref name="spec"/> returns an
    /// all-default array. Token values that <paramref name="parser"/> rejects are
    /// silently skipped (the corresponding position stays at the default), so a
    /// typo in one token never aborts the whole row.
    /// </summary>
    public static T[] Resolve<T>(
        IReadOnlyList<(double x, double y)> positions,
        string? spec,
        T defaultValue,
        TokenParser<T> parser)
    {
        int n = positions.Count;
        var result = new T[n];
        for (int i = 0; i < n; i++) result[i] = defaultValue;
        if (string.IsNullOrWhiteSpace(spec)) return result;

        bool[] isCorner = ClassifyExtremal(positions, cornersOnly: true);
        bool[] isPerim  = ClassifyExtremal(positions, cornersOnly: false);
        var (px, mx, py, my) = ClassifyFaces(positions);

        // Two passes: keyword/face tokens first, then explicit indices so index wins.
        var indexOverrides = new List<(int idx, T val)>();

        foreach (string raw in spec.Split([' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = raw.IndexOf(':');
            if (colon <= 0 || colon == raw.Length - 1) continue;
            string sel    = raw[..colon].Trim();
            string valStr = raw[(colon + 1)..].Trim();
            if (!parser(valStr, out T val)) continue;

            if (int.TryParse(sel, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
            {
                indexOverrides.Add((idx, val));
                continue;
            }

            switch (sel.ToLowerInvariant())
            {
                case "all":     Apply(result, val, _ => true); break;
                case "corners": Apply(result, val, i => isCorner[i]); break;
                case "edges":   Apply(result, val, i => isPerim[i] && !isCorner[i]); break;
                case "+x":      Apply(result, val, i => px[i]); break;
                case "-x":      Apply(result, val, i => mx[i]); break;
                case "+y":      Apply(result, val, i => py[i]); break;
                case "-y":      Apply(result, val, i => my[i]); break;
            }
        }

        foreach (var (idx, val) in indexOverrides)
            if (idx >= 0 && idx < n) result[idx] = val;

        return result;
    }

    private static void Apply<T>(T[] arr, T val, Func<int, bool> pred)
    {
        for (int i = 0; i < arr.Length; i++) if (pred(i)) arr[i] = val;
    }

    // ── Built-in token parsers ───────────────────────────────────────────────

    /// <summary>
    /// Accepts <c>true</c>/<c>false</c>, <c>outward</c>/<c>inward</c>, <c>out</c>/<c>in</c>,
    /// <c>yes</c>/<c>no</c>, <c>y</c>/<c>n</c>, <c>1</c>/<c>0</c> (case-insensitive).
    /// Used by <c>LongTopBentDirs</c>.
    /// </summary>
    public static bool ParseOutwardToken(string s, out bool v)
    {
        switch (s.Trim().ToLowerInvariant())
        {
            case "true": case "outward": case "out": case "yes": case "y": case "1":
                v = true; return true;
            case "false": case "inward": case "in": case "no": case "n": case "0":
                v = false; return true;
            default:
                v = false; return false;
        }
    }

    /// <summary>
    /// Parses <c>(x,y)</c> or <c>x,y</c> (with optional outer parens) — the two
    /// numbers in INCHES (local-frame coordinates). Used by <c>LongCrankTargets</c>.
    /// Returns the pair as a non-null tuple on success; the caller wraps as
    /// <c>(double, double)?</c> so a missing token leaves the per-bar default
    /// (i.e. <c>null</c> = "use the inset formula").
    /// </summary>
    public static bool ParseXyTokenInches(string s, out (double xIn, double yIn) v)
    {
        v = default;
        string trimmed = s.Trim().TrimStart('(').TrimEnd(')').Trim();
        var parts = trimmed.Split(',');
        if (parts.Length != 2) return false;
        if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double xIn)) return false;
        if (!double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double yIn)) return false;
        v = (xIn, yIn);
        return true;
    }

    /// <summary>
    /// Wrapper for <see cref="ParseXyTokenInches"/> producing the nullable tuple
    /// directly. The default per-bar value of <c>null</c> means "no override".
    /// </summary>
    public static bool ParseXyTokenInchesNullable(string s, out (double xIn, double yIn)? v)
    {
        if (ParseXyTokenInches(s, out (double, double) pair)) { v = pair; return true; }
        v = null;
        return false;
    }

    // ── Position classification (Revit-free; pure geometry on local-frame tuples) ───
    // Moved here from LongitudinalBarBuilder so the test project can call into the
    // resolver without dragging in Revit-API-dependent types.

    /// <summary>
    /// Bars on the face <c>+X</c> (x == max), <c>-X</c> (x == min), <c>+Y</c>, <c>-Y</c>.
    /// A corner sits on two faces. Returns four parallel <c>bool[]</c> arrays.
    /// </summary>
    internal static (bool[] px, bool[] mx, bool[] py, bool[] my) ClassifyFaces(
        IReadOnlyList<(double x, double y)> positions)
    {
        int n = positions.Count;
        var px = new bool[n]; var mx = new bool[n]; var py = new bool[n]; var my = new bool[n];
        if (n == 0) return (px, mx, py, my);

        double xMax = positions.Max(p => p.x), xMin = positions.Min(p => p.x);
        double yMax = positions.Max(p => p.y), yMin = positions.Min(p => p.y);
        const double tol = 1e-9;
        for (int i = 0; i < n; i++)
        {
            var (x, y) = positions[i];
            px[i] = Math.Abs(x - xMax) < tol;
            mx[i] = Math.Abs(x - xMin) < tol;
            py[i] = Math.Abs(y - yMax) < tol;
            my[i] = Math.Abs(y - yMin) < tol;
        }
        return (px, mx, py, my);
    }

    /// <summary>
    /// Corners (<paramref name="cornersOnly"/>=true) or perimeter bars
    /// (<paramref name="cornersOnly"/>=false) by extremal absolute coordinate.
    /// </summary>
    internal static bool[] ClassifyExtremal(IReadOnlyList<(double x, double y)> positions, bool cornersOnly)
    {
        var flags = new bool[positions.Count];
        if (positions.Count == 0) return flags;
        double maxAbsX = positions.Max(p => Math.Abs(p.x));
        double maxAbsY = positions.Max(p => Math.Abs(p.y));
        const double tol = 1e-9;
        for (int i = 0; i < positions.Count; i++)
        {
            var (x, y) = positions[i];
            bool onMaxX = Math.Abs(Math.Abs(x) - maxAbsX) < tol;
            bool onMaxY = Math.Abs(Math.Abs(y) - maxAbsY) < tol;
            flags[i] = cornersOnly ? (onMaxX && onMaxY) : (onMaxX || onMaxY);
        }
        return flags;
    }
}
