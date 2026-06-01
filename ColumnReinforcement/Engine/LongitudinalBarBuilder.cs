using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using ColumnReinforcement.Config;
using ColumnReinforcement.Domain;

namespace ColumnReinforcement.Engine;

/// <summary>
/// Places the perimeter longitudinal bars. For rectangular columns: four corners
/// always, plus evenly-spaced intermediates along each face when
/// <see cref="LongitudinalConfig.BarsAlongWidth"/> /
/// <see cref="LongitudinalConfig.BarsAlongDepth"/> exceed 2. For round columns:
/// <see cref="LongitudinalConfig.BarsAround"/> bars evenly spaced around the cage
/// circumference.
///
/// Bars are placed at the geometric centre of each rebar:
/// <c>cover_to_outer_face + d_tie + d_long / 2</c> from the face when ties are
/// enabled, or <c>cover_to_outer_face + d_long / 2</c> when they are not.
/// </summary>
public class LongitudinalBarBuilder
{
    private readonly Document _doc;

    public LongitudinalBarBuilder(Document doc) => _doc = doc;

    public int Build(ColumnGeometry geom, ColumnReinforcementConfig cfg, string tag, Element? slabAbove = null)
    {
        RebarBarType longBar = RebarFactory.GetBarType(_doc, cfg.Longitudinal.BarType);

        RebarHookType? hookTop    = RebarFactory.GetHookType(_doc, cfg.Longitudinal.HookTopType);
        RebarHookType? hookBottom = RebarFactory.GetHookType(_doc, cfg.Longitudinal.HookBottomType);

        double endCover = cfg.Ft(cfg.Cover.Ends);
        double zBottom  = endCover;
        double zTop     = geom.Height - endCover;
        if (zTop - zBottom <= 0)
            throw new InvalidOperationException(
                $"End cover ({UnitConv.FtToIn(endCover):0.###}\" top + bottom) is greater than column height " +
                $"({UnitConv.FtToIn(geom.Height):0.##}\").");

        var positions = ComputeCagePositions(_doc, cfg, geom);
        BarTopMode[] modes = ResolveTopModes(cfg.Longitudinal, positions);

        // Per-bar bent direction (true = outward). Default = the column-wide TopBentOutward.
        bool[] bentOutwardPerBar = SelectorListResolver.Resolve(
            positions,
            cfg.Longitudinal.TopBentDirs,
            defaultValue: cfg.Longitudinal.TopBentOutward,
            parser: SelectorListResolver.ParseOutwardToken);

        // Per-bar crank target in column-local FEET. null = use the inset formula.
        (double xu, double yu)?[] crankTargetPerBar = SelectorListResolver.Resolve<(double xu, double yu)?>(
            positions,
            cfg.Longitudinal.CrankTargets,
            defaultValue: null,
            parser: ParseCrankTargetTokenFeet);

        // Resolve geometry shared by all bars of a given mode, lazily.
        double zLocalSlabBend = 0;
        double bentLeg = 0;
        bool needsSlab = modes.Any(m => m == BarTopMode.BentToSlab);
        if (needsSlab)
        {
            if (slabAbove is null)
                throw new InvalidOperationException(
                    "longitudinal.topModes selects BentToSlab, which requires an OST_Floors slab above the column; none found.");
            BoundingBoxXYZ slabBb = slabAbove.get_BoundingBox(null)
                ?? throw new InvalidOperationException("Slab above the column has no bounding box.");
            zLocalSlabBend = (slabBb.Max.Z - endCover) - geom.BaseCenter.Z;
            bentLeg = cfg.Ft(cfg.Longitudinal.TopBentLeg);
            if (bentLeg <= 0)
                throw new InvalidOperationException("longitudinal.topBentLeg must be positive for BentToSlab bars.");
            if (zLocalSlabBend <= zBottom)
                throw new InvalidOperationException(
                    $"BentToSlab bend point ({UnitConv.FtToIn(zLocalSlabBend):0.##}\") is at or below the bar bottom " +
                    $"({UnitConv.FtToIn(zBottom):0.##}\"). Check end cover and slab elevation.");
        }

        // Cranked geometry (config-driven; doesn't need slab detection).
        double crankInset    = cfg.Ft(cfg.Longitudinal.CrankUpperInset);
        double crankPen      = cfg.Ft(cfg.Longitudinal.CrankPenetration);
        double crankBendLowZ = geom.Height - cfg.Ft(cfg.Longitudinal.CrankLowerBendOffset);
        double crankSlope    = cfg.Longitudinal.CrankSlope;
        bool needsCrank = modes.Any(m => m == BarTopMode.Cranked);
        if (needsCrank)
        {
            if (crankInset <= 0)  throw new InvalidOperationException("longitudinal.crankUpperInset must be positive for Cranked bars.");
            if (crankPen <= 0)    throw new InvalidOperationException("longitudinal.crankPenetration must be positive for Cranked bars.");
            if (crankSlope <= 0)  throw new InvalidOperationException("longitudinal.crankSlope must be positive for Cranked bars.");
            if (crankBendLowZ <= zBottom)
                throw new InvalidOperationException(
                    $"Cranked lower bend ({UnitConv.FtToIn(crankBendLowZ):0.##}\") is at or below the bar bottom " +
                    $"({UnitConv.FtToIn(zBottom):0.##}\"). Reduce crankLowerBendOffset or check the column height.");
        }

        // Per-bar Cranked penetration. Starts uniform from the global config; if the user
        // asks for end-height equalisation, EqualizeEndHeights shortens each bar's
        // penetration so all Cranked bars terminate at the SAME elevation (fixes the
        // ~2·d_b height step between corner offsetMag = inset·√2 and mid offsetMag = inset).
        double[] penetrationPerBar = new double[positions.Count];
        for (int i = 0; i < penetrationPerBar.Length; i++) penetrationPerBar[i] = crankPen;
        if (needsCrank && cfg.Longitudinal.CrankEqualizeEndHeight)
        {
            EqualizeEndHeights(
                positions, modes, crankTargetPerBar, penetrationPerBar,
                crankInset, crankSlope, crankBendLowZ);
        }

        int created = 0;
        for (int i = 0; i < positions.Count; i++)
        {
            (double x, double y) = positions[i];
            BarTopMode mode = modes[i];

            (IList<Curve> curves, XYZ normal, bool hasTopHook) = mode switch
            {
                BarTopMode.Straight   => (StraightBar(geom, x, y, zBottom, zTop), geom.LocalX, true),
                BarTopMode.BentToSlab => (BentBar(geom, x, y, zBottom, zLocalSlabBend, bentLeg, bentOutwardPerBar[i]), geom.NormalForBendAt(x, y), false),
                BarTopMode.Cranked    => CrankedBar(geom, x, y, zBottom, crankBendLowZ, crankInset, crankSlope, penetrationPerBar[i], crankTargetPerBar[i]),
                _ => (StraightBar(geom, x, y, zBottom, zTop), geom.LocalX, true),
            };

            RebarFactory.Create(
                _doc,
                RebarStyle.Standard,
                longBar,
                geom.Instance,
                normal,
                curves,
                tag,
                startHook: hookBottom,
                endHook:   hasTopHook ? hookTop : null);
            created++;
        }

        return created;
    }

    private static IList<Curve> StraightBar(ColumnGeometry geom, double x, double y, double zBottom, double zTop) =>
        new List<Curve> { Line.CreateBound(geom.At(x, y, zBottom), geom.At(x, y, zTop)) };

    private static IList<Curve> BentBar(
        ColumnGeometry geom, double x, double y, double zBottom, double zBend, double leg, bool outward)
    {
        XYZ p0 = geom.At(x, y, zBottom);
        XYZ pCorner = geom.At(x, y, zBend);
        // Inward = toward the column centre (legs from opposite faces cross in small
        // columns); outward = along the bar's outward face normal, so legs fan into the
        // surrounding slab without clashing. The bend-plane normal is identical either way.
        XYZ legDir = outward ? geom.InwardDirection(x, y).Negate() : geom.InwardDirection(x, y);
        XYZ pLegEnd = pCorner + legDir * leg;
        return new List<Curve> { Line.CreateBound(p0, pCorner), Line.CreateBound(pCorner, pLegEnd) };
    }

    /// <summary>
    /// Cranked main bar: vertical inside this column → diagonal to the upper cage
    /// offset → vertical penetration into the upper column. Returns curves, the
    /// bend-plane normal, and whether a top hook applies (no — the bar ends inside
    /// the upper column).
    ///
    /// <para><paramref name="explicitTarget"/>, when set, overrides the default
    /// inset formula <c>(xu, yu) = (x − sign(x)·inset, y − sign(y)·inset)</c> with a
    /// caller-supplied target — used by <see cref="LongitudinalConfig.CrankTargets"/>
    /// to handle asymmetric upper-column transitions where the upper-cage positions
    /// don't sit on the inset diagonal. Target is in column-local FEET (the resolver
    /// converts from the CSV's inches).</para>
    /// </summary>
    private static (IList<Curve>, XYZ, bool) CrankedBar(
        ColumnGeometry geom, double x, double y, double zBottom, double zBendLow,
        double inset, double slope, double penetration,
        (double xu, double yu)? explicitTarget = null)
    {
        double xu, yu;
        if (explicitTarget is { } t) { xu = t.xu; yu = t.yu; }
        else { xu = x - Math.Sign(x) * inset; yu = y - Math.Sign(y) * inset; }
        double offsetMag = Math.Sqrt((xu - x) * (xu - x) + (yu - y) * (yu - y));

        if (offsetMag < 1e-9)
        {
            // No offset on this bar's axis — degenerate to a straight bar that
            // simply runs up into the upper column for the penetration length.
            XYZ a = geom.At(x, y, zBottom);
            XYZ b = geom.At(x, y, zBendLow + penetration);
            return (new List<Curve> { Line.CreateBound(a, b) }, geom.LocalX, false);
        }

        double diagonalRise = slope * offsetMag;
        double zBendHigh = zBendLow + diagonalRise;
        double zTop = zBendHigh + penetration;

        XYZ p0 = geom.At(x,  y,  zBottom);
        XYZ p1 = geom.At(x,  y,  zBendLow);
        XYZ p2 = geom.At(xu, yu, zBendHigh);
        XYZ p3 = geom.At(xu, yu, zTop);

        // The bar lies in the vertical plane through (x,y) and (xu,yu); its normal is
        // perpendicular to that plane. (xu-x, yu-y) are LOCAL-frame offsets, so project
        // them through LocalX/LocalY into world before taking the cross product —
        // otherwise the normal is wrong for any rotated column and Rebar.CreateFromCurves
        // rejects the bar with "An internal error has occurred."
        XYZ offsetDir = (geom.LocalX * (xu - x) + geom.LocalY * (yu - y)).Normalize();
        XYZ normal = XYZ.BasisZ.CrossProduct(offsetDir).Normalize();

        return (new List<Curve>
        {
            Line.CreateBound(p0, p1),
            Line.CreateBound(p1, p2),
            Line.CreateBound(p2, p3),
        }, normal, false);
    }

    /// <summary>
    /// Post-adjusts every <see cref="BarTopMode.Cranked"/> bar's <c>penetration</c>
    /// so all Cranked bars terminate at the same elevation. Target zTop = max over
    /// natural zTop across all Cranked bars (so no penetration becomes longer than
    /// the uniform value — only corners shrink). Bars in other modes are not touched.
    /// Throws when the math would require non-positive penetration for any bar.
    /// </summary>
    private static void EqualizeEndHeights(
        IReadOnlyList<(double x, double y)> positions,
        IReadOnlyList<BarTopMode> modes,
        IReadOnlyList<(double xu, double yu)?> crankTargets,
        double[] penetrationPerBar,
        double crankInset, double crankSlope, double crankBendLowZ)
    {
        int n = positions.Count;
        var diagonalRise = new double[n];
        double maxZTop = double.NegativeInfinity;

        // Pass 1: compute each Cranked bar's natural zTop with its current penetration.
        for (int i = 0; i < n; i++)
        {
            if (modes[i] != BarTopMode.Cranked) continue;
            var (x, y) = positions[i];
            double xu, yu;
            if (crankTargets[i] is { } t) { xu = t.xu; yu = t.yu; }
            else { xu = x - Math.Sign(x) * crankInset; yu = y - Math.Sign(y) * crankInset; }
            double offsetMag = Math.Sqrt((xu - x) * (xu - x) + (yu - y) * (yu - y));
            diagonalRise[i] = crankSlope * offsetMag;
            double naturalZTop = crankBendLowZ + diagonalRise[i] + penetrationPerBar[i];
            if (naturalZTop > maxZTop) maxZTop = naturalZTop;
        }
        if (double.IsNegativeInfinity(maxZTop)) return;     // no Cranked bars

        // Pass 2: shrink each Cranked bar's penetration so its zTop hits the max.
        // (Always ≤ the original uniform penetration: less diagonalRise → MORE penetration
        //  to reach the same maxZTop; more diagonalRise → LESS. The bar with the largest
        //  diagonalRise drives maxZTop and keeps its original penetration.)
        for (int i = 0; i < n; i++)
        {
            if (modes[i] != BarTopMode.Cranked) continue;
            double newPen = maxZTop - crankBendLowZ - diagonalRise[i];
            if (newPen <= 0)
                throw new InvalidOperationException(
                    $"crankEqualizeEndHeight produced a non-positive penetration " +
                    $"({UnitConv.FtToIn(newPen):0.##}\") for bar index {i}; check crankUpperInset / " +
                    $"crankSlope / crankLowerBendOffset / crankPenetration in the config.");
            penetrationPerBar[i] = newPen;
        }
    }

    /// <summary>
    /// Parser for <see cref="LongitudinalConfig.CrankTargets"/> tokens — the CSV
    /// value is in INCHES; this wrapper converts to column-local FEET so the
    /// resolved tuple is consumable directly by <see cref="CrankedBar"/>.
    /// </summary>
    private static bool ParseCrankTargetTokenFeet(string s, out (double xu, double yu)? v)
    {
        if (SelectorListResolver.ParseXyTokenInches(s, out (double xIn, double yIn) pair))
        {
            v = (pair.xIn / 12.0, pair.yIn / 12.0);
            return true;
        }
        v = null;
        return false;
    }

    /// <summary>
    /// Resolve a <see cref="BarTopMode"/> for every cage position. Starts from
    /// <see cref="LongitudinalConfig.TopDefault"/> and applies the
    /// <see cref="LongitudinalConfig.TopModes"/> override string. Precedence
    /// (low → high): default, group keyword (corners/edges/all), explicit index.
    /// </summary>
    internal static BarTopMode[] ResolveTopModes(LongitudinalConfig cfg, IReadOnlyList<(double x, double y)> positions)
    {
        int n = positions.Count;
        var modes = new BarTopMode[n];
        for (int i = 0; i < n; i++) modes[i] = cfg.TopDefault;

        if (string.IsNullOrWhiteSpace(cfg.TopModes)) return modes;

        bool[] isCorner = SelectorListResolver.ClassifyExtremal(positions, cornersOnly: true);
        bool[] isPerim  = SelectorListResolver.ClassifyExtremal(positions, cornersOnly: false);
        // Per-face membership (a corner bar belongs to two faces). After the
        // canonical short-side orientation in ColumnGeometry, ±X are the long
        // faces and ±Y are the short faces.
        var (onPlusX, onMinusX, onPlusY, onMinusY) = SelectorListResolver.ClassifyFaces(positions);

        // Two passes so that index overrides always beat group/face keywords
        // regardless of token order: pass 1 applies keywords, pass 2 applies indices.
        var indexTokens = new List<(int idx, BarTopMode mode)>();

        foreach (string raw in cfg.TopModes.Split([' ', ';', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            string token = raw.Trim();
            int colon = token.IndexOf(':');
            if (colon <= 0 || colon == token.Length - 1) continue;       // malformed → ignore
            string selector = token.Substring(0, colon).Trim();
            string modeStr  = token.Substring(colon + 1).Trim();
            if (!TryParseMode(modeStr, out BarTopMode mode)) continue;

            if (int.TryParse(selector, out int idx))
            {
                indexTokens.Add((idx, mode));
                continue;
            }

            switch (selector.ToLowerInvariant())
            {
                case "all":     Apply(modes, mode, _ => true); break;
                case "corners": Apply(modes, mode, i => isCorner[i]); break;
                case "edges":   Apply(modes, mode, i => isPerim[i] && !isCorner[i]); break;
                case "+x":      Apply(modes, mode, i => onPlusX[i]); break;
                case "-x":      Apply(modes, mode, i => onMinusX[i]); break;
                case "+y":      Apply(modes, mode, i => onPlusY[i]); break;
                case "-y":      Apply(modes, mode, i => onMinusY[i]); break;
            }
        }

        foreach (var (idx, mode) in indexTokens)
            if (idx >= 0 && idx < n) modes[idx] = mode;

        return modes;
    }

    private static void Apply(BarTopMode[] modes, BarTopMode mode, Func<int, bool> predicate)
    {
        for (int i = 0; i < modes.Length; i++)
            if (predicate(i)) modes[i] = mode;
    }

    /// <summary>
    /// Resolve a subset of cage positions from a selector string, reusing the same
    /// vocabulary as <see cref="LongitudinalConfig.TopModes"/> selectors:
    /// space/<c>;</c>/<c>,</c>-separated tokens, each a 0-based index or one of
    /// <c>all</c> / <c>corners</c> / <c>edges</c> / <c>+x</c> / <c>-x</c> / <c>+y</c> / <c>-y</c>.
    /// A position is included if it matches ANY token (include-only — no exclusion).
    /// A null/empty selector includes every position (the default for builders that
    /// previously always placed at all positions). Used by the dowel builder to place
    /// starters only where the column below has no continuing bar.
    /// </summary>
    internal static bool[] ResolvePositionMask(
        IReadOnlyList<(double x, double y)> positions, string? selector)
    {
        int n = positions.Count;
        var mask = new bool[n];
        if (string.IsNullOrWhiteSpace(selector))
        {
            for (int i = 0; i < n; i++) mask[i] = true;     // empty = every position
            return mask;
        }

        bool[] isCorner = SelectorListResolver.ClassifyExtremal(positions, cornersOnly: true);
        bool[] isPerim  = SelectorListResolver.ClassifyExtremal(positions, cornersOnly: false);
        var (px, mx, py, my) = SelectorListResolver.ClassifyFaces(positions);

        void Mark(Func<int, bool> pred) { for (int i = 0; i < n; i++) if (pred(i)) mask[i] = true; }

        foreach (string raw in selector.Split([' ', ';', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            string token = raw.Trim();
            if (int.TryParse(token, out int idx)) { if (idx >= 0 && idx < n) mask[idx] = true; continue; }
            switch (token.ToLowerInvariant())
            {
                case "all":     Mark(_ => true); break;
                case "corners": Mark(i => isCorner[i]); break;
                case "edges":   Mark(i => isPerim[i] && !isCorner[i]); break;
                case "+x":      Mark(i => px[i]); break;
                case "-x":      Mark(i => mx[i]); break;
                case "+y":      Mark(i => py[i]); break;
                case "-y":      Mark(i => my[i]); break;
            }
        }
        return mask;
    }

    private static bool TryParseMode(string s, out BarTopMode mode)
    {
        switch (s.Trim().ToLowerInvariant())
        {
            case "s": case "straight":    mode = BarTopMode.Straight;   return true;
            case "c": case "cranked":     mode = BarTopMode.Cranked;    return true;
            case "b": case "benttoslab":  mode = BarTopMode.BentToSlab; return true;
            default: mode = BarTopMode.Straight; return false;
        }
    }

    /// <summary>
    /// Local-frame (x, y) bar centres for the entire perimeter cage. Dispatches on
    /// <see cref="ColumnGeometry.Section"/>: rectangular columns use the inset
    /// rectangle from <see cref="ComputeRectangularCageBounds"/> + <see cref="LayoutRectangular"/>;
    /// round columns use the cage radius from <see cref="ComputeRoundCageRadius"/> +
    /// <see cref="LayoutRound"/>. Shared by dowel and splice builders.
    /// </summary>
    internal static List<(double x, double y)> ComputeCagePositions(
        Document doc, ColumnReinforcementConfig cfg, ColumnGeometry geom)
    {
        if (geom.Section == ColumnSection.Round)
        {
            double r = ComputeRoundCageRadius(doc, cfg, geom);
            return LayoutRound(cfg.Longitudinal, r);
        }
        var bounds = ComputeRectangularCageBounds(doc, cfg, geom);
        return LayoutRectangular(cfg.Longitudinal, bounds.xMin, bounds.xMax, bounds.yMin, bounds.yMax);
    }

    /// <summary>
    /// Rectangular cage bounds in the column's local frame — bar-centre extents.
    /// </summary>
    internal static (double xMin, double xMax, double yMin, double yMax) ComputeRectangularCageBounds(
        Document doc, ColumnReinforcementConfig cfg, ColumnGeometry geom)
    {
        double inset = ComputeCageInset(doc, cfg);

        double xMin = -geom.Width / 2.0 + inset;
        double xMax =  geom.Width / 2.0 - inset;
        double yMin = -geom.Depth / 2.0 + inset;
        double yMax =  geom.Depth / 2.0 - inset;

        if (xMax - xMin <= 0 || yMax - yMin <= 0)
            throw new InvalidOperationException(
                $"Cover + tie + bar diameter ({UnitConv.FtToIn(inset):0.###}\") leaves no room inside a " +
                $"{UnitConv.FtToIn(geom.Width):0.##}\"×{UnitConv.FtToIn(geom.Depth):0.##}\" column.");

        return (xMin, xMax, yMin, yMax);
    }

    /// <summary>
    /// Round cage radius (centre-of-bar) in feet.
    /// </summary>
    internal static double ComputeRoundCageRadius(
        Document doc, ColumnReinforcementConfig cfg, ColumnGeometry geom)
    {
        double inset = ComputeCageInset(doc, cfg);
        double cageRadius = geom.Diameter / 2.0 - inset;
        if (cageRadius <= 0)
            throw new InvalidOperationException(
                $"Cover + tie + bar diameter ({UnitConv.FtToIn(inset):0.###}\") leaves no room inside a " +
                $"{UnitConv.FtToIn(geom.Diameter):0.##}\" diameter column.");
        return cageRadius;
    }

    /// <summary>Common inset from the concrete face to the longitudinal-bar centre.</summary>
    private static double ComputeCageInset(Document doc, ColumnReinforcementConfig cfg)
    {
        RebarBarType longBar = RebarFactory.GetBarType(doc, cfg.Longitudinal.BarType);
        double dLong = longBar.BarModelDiameter;

        double dTie = 0;
        if (cfg.Stirrups.Enabled)
            dTie = RebarFactory.GetBarType(doc, cfg.Stirrups.BarType).BarModelDiameter;

        double cover = cfg.Ft(cfg.Cover.Sides);
        return cover + dTie + dLong / 2.0;
    }

    /// <summary>
    /// Rectangular layout: four corners (always), plus evenly-spaced intermediates
    /// on each face when <see cref="LongitudinalConfig.BarsAlongWidth"/> /
    /// <see cref="LongitudinalConfig.BarsAlongDepth"/> exceed 2. Honours
    /// <see cref="LongitudinalConfig.CornerOnly"/>.
    /// </summary>
    internal static List<(double x, double y)> LayoutRectangular(
        LongitudinalConfig cfg, double xMin, double xMax, double yMin, double yMax)
    {
        var pts = new List<(double x, double y)>();

        if (cfg.CornerOnly)
        {
            pts.Add((xMin, yMin));
            pts.Add((xMax, yMin));
            pts.Add((xMax, yMax));
            pts.Add((xMin, yMax));
            return pts;
        }

        int nx = Math.Max(2, cfg.BarsAlongWidth);
        int ny = Math.Max(2, cfg.BarsAlongDepth);

        double[] xs = LinSpace(xMin, xMax, nx);
        double[] ys = LinSpace(yMin, yMax, ny);

        for (int i = 0; i < nx; i++) pts.Add((xs[i], yMin));
        for (int i = 0; i < nx; i++) pts.Add((xs[i], yMax));
        for (int j = 1; j < ny - 1; j++)
        {
            pts.Add((xMin, ys[j]));
            pts.Add((xMax, ys[j]));
        }
        return pts;
    }

    /// <summary>
    /// Round layout: <see cref="LongitudinalConfig.BarsAround"/> bars equally spaced
    /// around the circle of radius <paramref name="radius"/>. First bar at angle 0
    /// (along <see cref="ColumnGeometry.LocalX"/>); subsequent bars walk CCW.
    /// </summary>
    internal static List<(double x, double y)> LayoutRound(LongitudinalConfig cfg, double radius)
    {
        int n = Math.Max(3, cfg.BarsAround);
        var pts = new List<(double x, double y)>(n);
        for (int i = 0; i < n; i++)
        {
            double angle = 2 * Math.PI * i / n;
            pts.Add((radius * Math.Cos(angle), radius * Math.Sin(angle)));
        }
        return pts;
    }

    internal static double[] LinSpace(double from, double to, int n)
    {
        if (n <= 1) return [from];
        var a = new double[n];
        double step = (to - from) / (n - 1);
        for (int i = 0; i < n; i++) a[i] = from + step * i;
        return a;
    }
}
