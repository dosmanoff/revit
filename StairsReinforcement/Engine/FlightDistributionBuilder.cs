using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StairsReinforcement.Config;
using StairsReinforcement.Domain;

namespace StairsReinforcement.Engine;

/// <summary>
/// Places the flight transverse distribution bars: each bar runs across the width (along <c>W</c>)
/// and the set marches up the slope (along <c>U</c>) at the configured spacing. Bottom distribution
/// rests on the bottom main layer; top distribution sits under the top main layer.
/// </summary>
public sealed class FlightDistributionBuilder
{
    private readonly Document _doc;
    public FlightDistributionBuilder(Document doc) => _doc = doc;

    public int Build(FlightComponent f, StairsReinforcementConfig cfg, ElementId stairId)
    {
        int created = 0;

        if (cfg.Flight.BottomDist.Enabled)
        {
            double mainDb = DiaOf(cfg.Flight.BottomMain);   // dist rests on the main layer
            double n = cfg.FtOr(cfg.Flight.BottomDist.Cover, cfg.Cover.Bottom) + mainDb;
            created += Place(f, cfg, cfg.Flight.BottomDist, n, fromTop: false,
                ExistingRebarCleaner.MakeTag(cfg.Name, stairId, StairLayer.FlightBottomDist));
        }

        if (cfg.Flight.TopMode != TopMode.None && cfg.Flight.TopDist.Enabled)
        {
            double mainDb = DiaOf(cfg.Flight.TopMain);
            double n = f.WaistFt - cfg.FtOr(cfg.Flight.TopDist.Cover, cfg.Cover.Top) - mainDb;
            created += Place(f, cfg, cfg.Flight.TopDist, n, fromTop: true,
                ExistingRebarCleaner.MakeTag(cfg.Name, stairId, StairLayer.FlightTopDist));
        }

        return created;
    }

    private int Place(FlightComponent f, StairsReinforcementConfig cfg, BarSetSpec spec, double n, bool fromTop, string tag)
    {
        RebarBarType bt = RebarFactory.GetBarType(_doc, spec.BarType);
        double db = bt.BarNominalDiameter;
        if (fromTop) n -= db / 2; else n += db / 2;
        if (n <= 0 || n >= f.WaistFt) n = f.WaistFt * 0.5;   // guard

        double coverSide = cfg.Ft(cfg.Cover.Side);
        double wL = -f.WidthFt / 2 + coverSide + db / 2;
        double wR = f.WidthFt / 2 - coverSide - db / 2;
        if (wR - wL <= 1e-6) return 0;

        // Inset from the run ends so the first/last distribution bars stay inside the host solid.
        double inset = cfg.Ft(cfg.Cover.Bottom);
        double span = Math.Max(0, f.SlopeLengthFt - 2 * inset);
        (int count, double spacing) = BuildUtil.ResolveSet(spec.SpacingMode, spec.Count, cfg.Ft(spec.Spacing), span);

        // Representative bar runs across the width at the lower end; the set marches up-slope along U.
        XYZ p0 = BuildUtil.XYZof(f.Frame.At(inset, wL, n));
        XYZ p1 = BuildUtil.XYZof(f.Frame.At(inset, wR, n));
        var curves = new List<Curve> { Line.CreateBound(p0, p1) };

        var U = new XYZ(f.Frame.U.X, f.Frame.U.Y, f.Frame.U.Z);
        XYZ normalU = U.IsZeroLength() ? XYZ.BasisZ : U.Normalize();

        return RebarFactory.CreateSet(_doc, RebarStyle.Standard, bt.Id, f.Host, normalU, curves, tag, count, spacing);
    }

    private double DiaOf(BarSetSpec spec)
    {
        if (!spec.Enabled) return 0;
        try { return RebarFactory.GetBarType(_doc, spec.BarType).BarNominalDiameter; }
        catch { return 0; }
    }
}
