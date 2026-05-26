using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using ColumnReinforcement.Config;
using ColumnReinforcement.Domain;

namespace ColumnReinforcement.Engine;

/// <summary>
/// Places the perimeter longitudinal bars in a rectangular/square column:
/// always the four corners, plus evenly-spaced intermediates along each face
/// when <see cref="LongitudinalConfig.BarsAlongWidth"/> /
/// <see cref="LongitudinalConfig.BarsAlongDepth"/> exceed 2.
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

        var bounds = ComputeCageBounds(_doc, cfg, geom);
        var positions = LayoutPositions(cfg.Longitudinal, bounds.xMin, bounds.xMax, bounds.yMin, bounds.yMax);

        // Each vertical bar runs from base + endCover to top - endCover.
        double zBottom = endCover;
        double zTop    = geom.Height - endCover;
        if (zTop - zBottom <= 0)
            throw new InvalidOperationException(
                $"End cover ({UnitConv.FtToIn(endCover):0.###}\" top + bottom) is greater than column height " +
                $"({UnitConv.FtToIn(geom.Height):0.##}\").");

        // Normal for CreateFromCurves: perpendicular to the bar axis. Any horizontal direction
        // works for a straight vertical bar; we use the column's local X for stability.
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
    /// Cross-section bounding rectangle of the longitudinal cage in the column's
    /// local frame, i.e. the (x, y) extents along which longitudinal bar centres
    /// lie. Encapsulates the cover + d_tie + d_long / 2 inset formula so dowel
    /// and splice builders place bars at the same positions as the cage.
    /// </summary>
    internal static (double xMin, double xMax, double yMin, double yMax) ComputeCageBounds(
        Document doc, ColumnReinforcementConfig cfg, ColumnGeometry geom)
    {
        RebarBarType longBar = RebarFactory.GetBarType(doc, cfg.Longitudinal.BarType);
        double dLong = longBar.BarModelDiameter;

        // Ties shift the longitudinal bars inward by d_tie. If ties are off,
        // no shift — the longitudinal cage hugs the cover directly.
        double dTie = 0;
        if (cfg.Stirrups.Enabled)
            dTie = RebarFactory.GetBarType(doc, cfg.Stirrups.BarType).BarModelDiameter;

        double cover = cfg.Ft(cfg.Cover.Sides);
        double inset = cover + dTie + dLong / 2.0;

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
    /// Compute (x, y) bar centres in the column local frame. Returns four corners
    /// when <see cref="LongitudinalConfig.CornerOnly"/> is set; otherwise a
    /// perimeter ring with evenly-spaced intermediates on each face.
    /// </summary>
    internal static List<(double x, double y)> LayoutPositions(
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

        // Bottom face (y = yMin): every x.
        for (int i = 0; i < nx; i++) pts.Add((xs[i], yMin));
        // Top face (y = yMax): every x.
        for (int i = 0; i < nx; i++) pts.Add((xs[i], yMax));
        // Left and right faces: only the intermediates (corners already placed).
        for (int j = 1; j < ny - 1; j++)
        {
            pts.Add((xMin, ys[j]));
            pts.Add((xMax, ys[j]));
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
