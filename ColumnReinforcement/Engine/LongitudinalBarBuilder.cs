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

    public int Build(ColumnGeometry geom, ColumnReinforcementConfig cfg, string tag)
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

        // Normal for CreateFromCurves: perpendicular to the bar axis. Any horizontal
        // direction works for a straight vertical bar; we use the column's local X for stability.
        XYZ normal = geom.LocalX;

        int created = 0;
        foreach ((double x, double y) in positions)
        {
            XYZ p0 = geom.At(x, y, zBottom);
            XYZ p1 = geom.At(x, y, zTop);
            IList<Curve> curves = new List<Curve> { Line.CreateBound(p0, p1) };

            RebarFactory.Create(
                _doc,
                RebarStyle.Standard,
                longBar,
                geom.Instance,
                normal,
                curves,
                tag,
                startHook: hookBottom,
                endHook:   hookTop);
            created++;
        }

        return created;
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

    private static double[] LinSpace(double from, double to, int n)
    {
        if (n <= 1) return [from];
        var a = new double[n];
        double step = (to - from) / (n - 1);
        for (int i = 0; i < n; i++) a[i] = from + step * i;
        return a;
    }
}
