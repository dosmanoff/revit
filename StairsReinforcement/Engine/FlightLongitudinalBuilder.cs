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

    public int Build(FlightComponent f, StairsReinforcementConfig cfg, ElementId stairId,
        LandingComponent? lowerLanding, LandingComponent? upperLanding)
    {
        int created = 0;
        if (cfg.Flight.BottomMain.Enabled) created += BuildBottom(f, cfg, stairId, lowerLanding, upperLanding);
        if (cfg.Flight.TopMode != TopMode.None && cfg.Flight.TopMain.Enabled) created += BuildTop(f, cfg, stairId);
        return created;
    }

    /// <summary>
    /// Bottom (soffit) main bar: runs up the run and, at a landing end, bends into the landing for a
    /// development length (the soffit is convex, so this is the safe place to carry continuity). At a
    /// slab/other end it is clamped just inside the run (a straight bar into a separate support element
    /// throws "internal error" on a native-stair host). One planar polyline, distributed across the width.
    /// </summary>
    private int BuildBottom(FlightComponent f, StairsReinforcementConfig cfg, ElementId stairId,
        LandingComponent? lowerLanding, LandingComponent? upperLanding)
    {
        BarSetSpec spec = cfg.Flight.BottomMain;
        RebarBarType bt = RebarFactory.GetBarType(_doc, spec.BarType);
        double db = bt.BarNominalDiameter;
        double n = cfg.FtOr(spec.Cover, cfg.Cover.Bottom) + db / 2;
        double inset = EndInsetFt(cfg);
        double L = f.SlopeLengthFt;
        double dev = LandingDevFt(cfg, spec, db);

        double coverSide = cfg.Ft(cfg.Cover.Side);
        double w0 = -f.WidthFt / 2 + coverSide + db / 2;
        double w1 = f.WidthFt / 2 - coverSide - db / 2;
        double widthSpan = w1 - w0;
        if (widthSpan <= 1e-6) { w0 = 0; widthSpan = 0; }
        (int count, double spacing) = BuildUtil.ResolveSet(spec.SpacingMode, spec.Count, cfg.Ft(spec.Spacing), widthSpan);

        var W = new XYZ(f.Frame.W.X, f.Frame.W.Y, 0);
        XYZ normalW = W.IsZeroLength() ? XYZ.BasisX : W.Normalize();
        var runH = new XYZ(f.Frame.U.X, f.Frame.U.Y, 0);
        runH = runH.IsZeroLength() ? XYZ.BasisY : runH.Normalize();

        // Representative polyline at w0 (all points share the same W coordinate ⇒ planar ⟂ W).
        var pts = new List<XYZ>();
        if (lowerLanding is not null) pts.Add(LandingPt(f, w0, n, db, cfg, runH, atUpper: false, lowerLanding, dev));
        pts.Add(BuildUtil.XYZof(f.Frame.At(lowerLanding is not null ? 0 : inset, w0, n)));
        pts.Add(BuildUtil.XYZof(f.Frame.At(upperLanding is not null ? L : L - inset, w0, n)));
        if (upperLanding is not null) pts.Add(LandingPt(f, w0, n, db, cfg, runH, atUpper: true, upperLanding, dev));

        var curves = new List<Curve>();
        for (int i = 1; i < pts.Count; i++)
            if (pts[i].DistanceTo(pts[i - 1]) > 1e-6) curves.Add(Line.CreateBound(pts[i - 1], pts[i]));
        if (curves.Count == 0) return 0;

        string tag = ExistingRebarCleaner.MakeTag(cfg.Name, stairId, StairLayer.FlightBottomMain);
        return RebarFactory.CreateSet(_doc, RebarStyle.Standard, bt.Id, f.Host, normalW, curves, tag, count, spacing);
    }

    /// <summary>End point of the landing leg: the fold, extended horizontally into the landing at the landing's bottom-bar level.</summary>
    private static XYZ LandingPt(FlightComponent f, double w0, double n, double db,
        StairsReinforcementConfig cfg, XYZ runH, bool atUpper, LandingComponent landing, double dev)
    {
        double uEnd = atUpper ? f.SlopeLengthFt : 0;
        XYZ fold = BuildUtil.XYZof(f.Frame.At(uEnd, w0, n));
        XYZ away = runH * (atUpper ? 1.0 : -1.0);               // out of the flight, into the landing
        double landBotZ = landing.ElevationFt - landing.ThicknessFt + cfg.Ft(cfg.Cover.Bottom) + db / 2;
        return new XYZ(fold.X + away.X * dev, fold.Y + away.Y * dev, landBotZ);
    }

    private static double LandingDevFt(StairsReinforcementConfig cfg, BarSetSpec spec, double db)
    {
        double a = cfg.Ft(spec.EndAnchorLen);
        return a > 1e-6 ? a : BuildUtil.LapFt(cfg, db);
    }

    /// <summary>Small inset from the run ends so a clamped (non-landing) end stays inside the host solid.</summary>
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
