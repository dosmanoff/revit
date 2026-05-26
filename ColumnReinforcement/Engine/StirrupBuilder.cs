using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using ColumnReinforcement.Config;
using ColumnReinforcement.Domain;

namespace ColumnReinforcement.Engine;

/// <summary>
/// Places the outer transverse tie along the column height. Each tie is a single
/// closed rectangle whose corners are auto-rounded by Revit to the bar type's
/// <c>StirrupTieBendDiameter</c>; both free ends share one corner and turn
/// inward via the configured hook type.
///
/// <para>Phase 2 supports densified confinement zones at the top and bottom
/// (different spacing inside the zones). When neither zone is enabled, the
/// builder behaves exactly as in Phase 1 — uniform spacing top-to-bottom.</para>
/// </summary>
public class StirrupBuilder
{
    private readonly Document _doc;

    public StirrupBuilder(Document doc) => _doc = doc;

    public int Build(ColumnGeometry geom, ColumnReinforcementConfig cfg, string tag)
    {
        StirrupsConfig s = cfg.Stirrups;
        if (!s.Enabled) return 0;

        RebarBarType tieBar = RebarFactory.GetBarType(_doc, s.BarType);
        RebarHookType? hook = RebarFactory.GetHookType(_doc, s.HookType);

        double dTie     = tieBar.BarModelDiameter;
        double cover    = cfg.Ft(cfg.Cover.Sides);
        double endCover = cfg.Ft(cfg.Cover.Ends);
        double inset    = cover + dTie / 2.0;     // tie centreline distance from concrete face

        // Geometry of the tie outline differs by section: rectangle vs circle.
        // The vertical layout (zMin, zMax, confinement zones) is shared below.
        TieShape shape = geom.Section == ColumnSection.Round
            ? TieShape.Round(geom, inset)
            : TieShape.Rectangular(geom, inset);

        // Lowest and highest tie elevations. Explicit offsets override cover.ends so
        // ties can skip a joint zone at the top or bottom of the column.
        double zMin = s.OffsetBottom is { } ob ? cfg.Ft(ob) : endCover;
        double zMax = geom.Height - (s.OffsetTop is { } ot ? cfg.Ft(ot) : endCover);
        if (zMax - zMin <= 0)
            throw new InvalidOperationException(
                $"Tie placement window collapsed: bottom={UnitConv.FtToIn(zMin):0.##}\", top={UnitConv.FtToIn(zMax):0.##}\". " +
                $"Check offsetTop/offsetBottom and cover.ends against the column height ({UnitConv.FtToIn(geom.Height):0.##}\").");

        double mainSpacing = cfg.Ft(s.Spacing);
        if (mainSpacing <= 0)
            throw new InvalidOperationException("Tie spacing must be positive.");

        double zBottomZoneEnd = zMin;
        double zTopZoneStart  = zMax;
        double bottomSpacing  = mainSpacing;
        double topSpacing     = mainSpacing;

        if (s.Confinement.Bottom.Enabled)
        {
            bottomSpacing  = cfg.Ft(s.Confinement.Bottom.Spacing);
            if (bottomSpacing <= 0)
                throw new InvalidOperationException("Bottom confinement spacing must be positive.");
            zBottomZoneEnd = zMin + ResolveZoneLength(cfg, s.Confinement.Bottom, geom.Height, "bottom");
        }
        if (s.Confinement.Top.Enabled)
        {
            topSpacing    = cfg.Ft(s.Confinement.Top.Spacing);
            if (topSpacing <= 0)
                throw new InvalidOperationException("Top confinement spacing must be positive.");
            zTopZoneStart = zMax - ResolveZoneLength(cfg, s.Confinement.Top, geom.Height, "top");
        }

        if (zBottomZoneEnd > zTopZoneStart)
            throw new InvalidOperationException(
                $"Top and bottom confinement zones overlap " +
                $"(bottom ends at {UnitConv.FtToIn(zBottomZoneEnd):0.##}\", top starts at {UnitConv.FtToIn(zTopZoneStart):0.##}\"). " +
                $"Reduce one of the zone lengths.");

        // Build three back-to-back intervals; empty intervals are skipped.
        // Boundary ties (zBottomZoneEnd, zTopZoneStart) appear in two intervals and
        // are de-duplicated by CombineIntervals.
        var intervals = new List<(double from, double to, double step)>
        {
            (zMin,            zBottomZoneEnd, bottomSpacing),
            (zBottomZoneEnd,  zTopZoneStart,  mainSpacing),
            (zTopZoneStart,   zMax,           topSpacing),
        };

        // Normal of the tie plane = world Z (the tie sits horizontally).
        XYZ normal = XYZ.BasisZ;

        int created = 0;
        foreach (double z in CombineIntervals(intervals))
        {
            IList<Curve> curves = shape.CurvesAt(geom, z);

            RebarFactory.Create(
                _doc,
                RebarStyle.StirrupTie,
                tieBar,
                geom.Instance,
                normal,
                curves,
                tag,
                startHook: hook,
                endHook:   hook);
            created++;
        }

        return created;
    }

    /// <summary>
    /// Resolve a confinement-zone length: absolute <see cref="ConfinementZoneConfig.ZoneLength"/>
    /// wins when set; otherwise the <see cref="ConfinementZoneConfig.ZoneFraction"/> times
    /// the column height. Throws if neither is set or if the result is non-positive.
    /// </summary>
    private static double ResolveZoneLength(
        ColumnReinforcementConfig cfg, ConfinementZoneConfig zone, double columnHeight, string which)
    {
        double len;
        if (zone.ZoneLength is { } abs)
        {
            len = cfg.Ft(abs);
        }
        else if (zone.ZoneFraction is { } frac)
        {
            if (frac <= 0 || frac >= 1)
                throw new InvalidOperationException(
                    $"{which} confinement zoneFraction must be between 0 and 1 exclusive (got {frac}).");
            len = frac * columnHeight;
        }
        else
        {
            throw new InvalidOperationException(
                $"{which} confinement is enabled but neither zoneLength nor zoneFraction is set.");
        }

        if (len <= 0)
            throw new InvalidOperationException($"{which} confinement zone length is non-positive.");
        if (len > columnHeight)
            throw new InvalidOperationException(
                $"{which} confinement zone length ({UnitConv.FtToIn(len):0.##}\") exceeds column height " +
                $"({UnitConv.FtToIn(columnHeight):0.##}\").");

        return len;
    }

    /// <summary>
    /// Walk each <c>(from, to, step)</c> interval, emit evenly-spaced positions with
    /// endpoints included, then sort and deduplicate (a single boundary position
    /// between two adjacent intervals collapses to one tie).
    /// </summary>
    internal static IReadOnlyList<double> CombineIntervals(
        IEnumerable<(double from, double to, double step)> intervals)
    {
        var all = new List<double>();
        foreach (var (from, to, step) in intervals)
        {
            if (to - from < 1e-9 || step <= 0) continue;
            double span = to - from;
            int n = Math.Max(1, (int)Math.Ceiling(span / step));
            double actualStep = span / n;
            for (int i = 0; i <= n; i++) all.Add(from + i * actualStep);
        }
        all.Sort();

        const double tolerance = 1e-6;
        var result = new List<double>(all.Count);
        foreach (double z in all)
        {
            if (result.Count == 0 || z - result[^1] > tolerance)
                result.Add(z);
        }
        return result;
    }

    /// <summary>
    /// Section-specific tie outline at a given elevation. Rectangular ties are four
    /// chained line segments forming a closed rectangle; round ties are two
    /// 180° arcs joined into a closed circle. Both share Z layout and hook handling.
    /// </summary>
    private abstract class TieShape
    {
        public abstract IList<Curve> CurvesAt(ColumnGeometry geom, double z);

        public static TieShape Rectangular(ColumnGeometry geom, double inset)
        {
            double xMin = -geom.Width / 2.0 + inset;
            double xMax =  geom.Width / 2.0 - inset;
            double yMin = -geom.Depth / 2.0 + inset;
            double yMax =  geom.Depth / 2.0 - inset;
            if (xMax - xMin <= 0 || yMax - yMin <= 0)
                throw new InvalidOperationException(
                    $"Cover + tie diameter ({UnitConv.FtToIn(inset):0.###}\") leaves no room for a tie inside a " +
                    $"{UnitConv.FtToIn(geom.Width):0.##}\"×{UnitConv.FtToIn(geom.Depth):0.##}\" column.");
            return new Rect(xMin, xMax, yMin, yMax);
        }

        public static TieShape Round(ColumnGeometry geom, double inset)
        {
            double r = geom.Diameter / 2.0 - inset;
            if (r <= 0)
                throw new InvalidOperationException(
                    $"Cover + tie diameter ({UnitConv.FtToIn(inset):0.###}\") leaves no room for a tie inside a " +
                    $"{UnitConv.FtToIn(geom.Diameter):0.##}\" diameter column.");
            return new Circle(r);
        }

        private sealed class Rect : TieShape
        {
            private readonly double _xMin, _xMax, _yMin, _yMax;
            public Rect(double xMin, double xMax, double yMin, double yMax)
            { _xMin = xMin; _xMax = xMax; _yMin = yMin; _yMax = yMax; }

            public override IList<Curve> CurvesAt(ColumnGeometry geom, double z)
            {
                XYZ p1 = geom.At(_xMin, _yMin, z);
                XYZ p2 = geom.At(_xMax, _yMin, z);
                XYZ p3 = geom.At(_xMax, _yMax, z);
                XYZ p4 = geom.At(_xMin, _yMax, z);
                return new List<Curve>
                {
                    Line.CreateBound(p1, p2),
                    Line.CreateBound(p2, p3),
                    Line.CreateBound(p3, p4),
                    Line.CreateBound(p4, p1),
                };
            }
        }

        private sealed class Circle : TieShape
        {
            private readonly double _radius;
            public Circle(double radius) { _radius = radius; }

            public override IList<Curve> CurvesAt(ColumnGeometry geom, double z)
            {
                XYZ center = geom.At(0, 0, z);
                Arc arc1 = Arc.Create(center, _radius, 0,         Math.PI,     geom.LocalX, geom.LocalY);
                Arc arc2 = Arc.Create(center, _radius, Math.PI,   2 * Math.PI, geom.LocalX, geom.LocalY);
                return new List<Curve> { arc1, arc2 };
            }
        }
    }
}
