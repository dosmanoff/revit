using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StairsReinforcement.Config;
using StairsReinforcement.Domain;
using StairsReinforcement.Geometry;

namespace StairsReinforcement.Engine;

/// <summary>
/// Places the flight longitudinal main bars (run up the slope, distributed across the width):
/// bottom (soffit-side) and, per <see cref="TopMode"/>, top (tread-side) over supports / continuous.
/// Each bar's <c>CreateFromCurves</c> normal is the horizontal width axis <c>W</c> (= the bend
/// plane normal for a sloped bar) and the set marches across the width along the same <c>W</c>.
/// </summary>
public sealed class FlightLongitudinalBuilder
{
    private readonly Document _doc;
    public FlightLongitudinalBuilder(Document doc) => _doc = doc;

    public int Build(FlightComponent f, StairsReinforcementConfig cfg, ElementId stairId)
    {
        int created = 0;
        if (cfg.Flight.BottomMain.Enabled) created += BuildBottom(f, cfg, stairId);
        if (cfg.Flight.TopMode != TopMode.None && cfg.Flight.TopMain.Enabled) created += BuildTop(f, cfg, stairId);
        return created;
    }

    private int BuildBottom(FlightComponent f, StairsReinforcementConfig cfg, ElementId stairId)
    {
        BarSetSpec spec = cfg.Flight.BottomMain;
        RebarBarType bt = RebarFactory.GetBarType(_doc, spec.BarType);
        double db = bt.BarNominalDiameter;
        double n = cfg.FtOr(spec.Cover, cfg.Cover.Bottom) + db / 2;

        // Clamp within the host run extent — a bar running out past the run into the support throws
        // "internal error" on a native-stair host. Development is provided by a hook (see BuildUtil.IsHook).
        double inset = EndInsetFt(cfg);
        double uA = inset;
        double uB = f.SlopeLengthFt - inset;

        string tag = ExistingRebarCleaner.MakeTag(cfg.Name, stairId, StairLayer.FlightBottomMain);
        return PlaceSet(f, cfg, spec, bt, db, n, uA, uB, tag,
            hookA: BuildUtil.IsHook(spec.StartAnchor), hookB: BuildUtil.IsHook(spec.EndAnchor));
    }

    /// <summary>Small inset from the run ends so the bar (and any hook) stays inside the host solid.</summary>
    private static double EndInsetFt(StairsReinforcementConfig cfg) => cfg.Ft(cfg.Cover.Bottom);

    private int BuildTop(FlightComponent f, StairsReinforcementConfig cfg, ElementId stairId)
    {
        BarSetSpec spec = cfg.Flight.TopMain;
        RebarBarType bt = RebarFactory.GetBarType(_doc, spec.BarType);
        double db = bt.BarNominalDiameter;
        double n = f.WaistFt - cfg.FtOr(spec.Cover, cfg.Cover.Top) - db / 2;
        if (n <= db) n = f.WaistFt * 0.5;   // guard a thin/unknown waist

        double inset = EndInsetFt(cfg);
        double L = f.SlopeLengthFt;
        double lo = inset, hi = L - inset;                 // clamped within the host run
        double ext = cfg.Ft(cfg.Flight.TopSupportExtent);
        bool hookA = BuildUtil.IsHook(spec.StartAnchor), hookB = BuildUtil.IsHook(spec.EndAnchor);
        string tag = ExistingRebarCleaner.MakeTag(cfg.Name, stairId, StairLayer.FlightTopMain);

        switch (cfg.Flight.TopMode)
        {
            case TopMode.Continuous:
                return PlaceSet(f, cfg, spec, bt, db, n, lo, hi, tag, hookA, hookB);

            case TopMode.OverSupports:
                int c = PlaceSet(f, cfg, spec, bt, db, n, lo, Math.Min(lo + ext, hi), tag, hookA, false);
                c += PlaceSet(f, cfg, spec, bt, db, n, Math.Max(lo, hi - ext), hi, tag, false, hookB);
                return c;

            case TopMode.EndsOnly:
                return PlaceSet(f, cfg, spec, bt, db, n, Math.Max(lo, hi - ext), hi, tag, false, hookB);

            default:
                return 0;
        }
    }

    /// <summary>Distribute one bar-set across the width at normal offset <paramref name="n"/>, over [uA,uB] along the slope, split &amp; lapped.</summary>
    private int PlaceSet(
        FlightComponent f, StairsReinforcementConfig cfg, BarSetSpec spec, RebarBarType bt, double db,
        double n, double uA, double uB, string tag, bool hookA, bool hookB)
    {
        double total = uB - uA;
        if (total <= 1e-6) return 0;

        double coverSide = cfg.Ft(cfg.Cover.Side);
        double w0 = -f.WidthFt / 2 + coverSide + db / 2;
        double w1 = f.WidthFt / 2 - coverSide - db / 2;
        double widthSpan = w1 - w0;
        if (widthSpan <= 1e-6) { w0 = 0; widthSpan = 0; }

        (int count, double spacing) = BuildUtil.ResolveSet(spec.SpacingMode, spec.Count, cfg.Ft(spec.Spacing), widthSpan);

        var W = new XYZ(f.Frame.W.X, f.Frame.W.Y, 0);
        XYZ normalW = W.IsZeroLength() ? XYZ.BasisX : W.Normalize();

        double maxLen = cfg.Ft(cfg.Lengths.MaxBarLength);
        double lap = BuildUtil.LapFt(cfg, db);
        var segs = BarSplitter.Split(total, maxLen, lap);

        RebarHookType? hkA = hookA ? BuildUtil.HookFor(_doc, spec.StartAnchor, spec.StartHook) : null;
        RebarHookType? hkB = hookB ? BuildUtil.HookFor(_doc, spec.EndAnchor, spec.EndHook) : null;

        int created = 0;
        for (int i = 0; i < segs.Count; i++)
        {
            double a = uA + segs[i].Start, b = uA + segs[i].End;
            XYZ p0 = BuildUtil.XYZof(f.Frame.At(a, w0, n));
            XYZ p1 = BuildUtil.XYZof(f.Frame.At(b, w0, n));
            var curves = new List<Curve> { Line.CreateBound(p0, p1) };

            RebarHookType? sh = i == 0 ? hkA : null;
            RebarHookType? eh = i == segs.Count - 1 ? hkB : null;
            created += RebarFactory.CreateSet(_doc, RebarStyle.Standard, bt.Id, f.Host, normalW, curves, tag, count, spacing, sh, eh);
        }
        return created;
    }
}
