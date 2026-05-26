using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using ColumnReinforcement.Config;
using ColumnReinforcement.Domain;

namespace ColumnReinforcement.Engine;

/// <summary>
/// Places the outer transverse tie (one closed rectangle per spacing step
/// along the column height). The four sharp corners are auto-rounded to the
/// tie bar's <c>StirrupTieBendDiameter</c> by Revit; both free ends share the
/// same corner of the rectangle and turn inward via the configured hook type.
///
/// Confinement zones, inner cross-ties, 45° rotation, and round-column ties
/// arrive in Phase 2 / 3.
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

        double xMin = -geom.Width / 2.0 + inset;
        double xMax =  geom.Width / 2.0 - inset;
        double yMin = -geom.Depth / 2.0 + inset;
        double yMax =  geom.Depth / 2.0 - inset;
        if (xMax - xMin <= 0 || yMax - yMin <= 0)
            throw new InvalidOperationException(
                $"Cover + tie diameter ({UnitConv.FtToIn(inset):0.###}\") leaves no room for a tie inside a " +
                $"{UnitConv.FtToIn(geom.Width):0.##}\"×{UnitConv.FtToIn(geom.Depth):0.##}\" column.");

        double spacing = cfg.Ft(s.Spacing);
        if (spacing <= 0)
            throw new InvalidOperationException("Tie spacing must be positive.");

        double zMin = endCover;
        double zMax = geom.Height - endCover;
        if (zMax - zMin <= 0)
            throw new InvalidOperationException(
                $"End cover ({UnitConv.FtToIn(endCover):0.###}\" top + bottom) is greater than column height " +
                $"({UnitConv.FtToIn(geom.Height):0.##}\").");

        // Normal of the tie plane = world Z (the tie sits horizontally).
        XYZ normal = XYZ.BasisZ;

        int created = 0;
        foreach (double z in EvenlySpaced(zMin, zMax, spacing))
        {
            XYZ p1 = geom.At(xMin, yMin, z);
            XYZ p2 = geom.At(xMax, yMin, z);
            XYZ p3 = geom.At(xMax, yMax, z);
            XYZ p4 = geom.At(xMin, yMax, z);

            // Four chained segments forming a closed rectangle. Hooks at the
            // start of the first segment and end of the last meet at p1 — the
            // single corner of the tie where the bar opens.
            IList<Curve> curves = new List<Curve>
            {
                Line.CreateBound(p1, p2),
                Line.CreateBound(p2, p3),
                Line.CreateBound(p3, p4),
                Line.CreateBound(p4, p1),
            };

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
    /// Inclusive evenly-spaced positions from <paramref name="from"/> to
    /// <paramref name="to"/>: endpoints always included, and the actual step
    /// is rounded down from the requested <paramref name="step"/> so the row
    /// fits exactly between the endpoints.
    /// </summary>
    private static IEnumerable<double> EvenlySpaced(double from, double to, double step)
    {
        if (to <= from || step <= 0) yield break;
        double span = to - from;
        int n = Math.Max(1, (int)Math.Ceiling(span / step));
        double actualStep = span / n;
        for (int i = 0; i <= n; i++) yield return from + i * actualStep;
    }
}
