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
        double lap = BuildUtil.LapFt(cfg, db);
        double uTop = BuildUtil.RunTopU(f, n);            // clamp to the real run solid, not the frame

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

        double uLo = lowerLanding is not null ? 0 : inset;
        double uHi = upperLanding is not null ? uTop : uTop - inset;
        if (uHi <= uLo + 1e-6) return 0;

        // Up the soffit, bending into a landing end for a lap with the landing bottom mesh.
        var pts = new List<XYZ>();
        XYZ pLo = BuildUtil.XYZof(f.Frame.At(uLo, w0, n));
        XYZ pHi = BuildUtil.XYZof(f.Frame.At(uHi, w0, n));
        if (lowerLanding is not null) pts.Add(LandingLeg(pLo, runH * -1.0, lowerLanding, db, cfg, lap));
        pts.Add(pLo);
        pts.Add(pHi);
        if (upperLanding is not null) pts.Add(LandingLeg(pHi, runH, upperLanding, db, cfg, lap));

        var curves = new List<Curve>();
        for (int i = 1; i < pts.Count; i++)
            if (pts[i].DistanceTo(pts[i - 1]) > 1e-6) curves.Add(Line.CreateBound(pts[i - 1], pts[i]));
        if (curves.Count == 0) return 0;

        string tag = ExistingRebarCleaner.MakeTag(cfg.Name, stairId, StairLayer.FlightBottomMain);
        return RebarFactory.CreateSet(_doc, RebarStyle.Standard, bt.Id, f.Host, normalW, curves, tag, count, spacing);
    }

    /// <summary>Horizontal leg from the run end into the landing (run-horizontal, dropped to the landing bottom-mesh level) for a lap.</summary>
    private static XYZ LandingLeg(XYZ runEnd, XYZ awayDir, LandingComponent landing, double db, StairsReinforcementConfig cfg, double lap)
    {
        double meshZ = landing.ElevationFt - landing.ThicknessFt + cfg.Ft(cfg.Cover.Bottom) + db / 2;
        return new XYZ(runEnd.X + awayDir.X * lap, runEnd.Y + awayDir.Y * lap, meshZ);
    }

    /// <summary>Small inset from the run ends so a clamped end stays inside the host solid.</summary>
    private static double EndInsetFt(StairsReinforcementConfig cfg) => cfg.Ft(cfg.Cover.Bottom);

    private int BuildTop(FlightComponent f, StairsReinforcementConfig cfg, ElementId stairId)
    {
        BarSetSpec spec = cfg.Flight.TopMain;
        RebarBarType bt = RebarFactory.GetBarType(_doc, spec.BarType);
        double db = bt.BarNominalDiameter;
        double n = f.WaistFt - cfg.FtOr(spec.Cover, cfg.Cover.Top) - db / 2;
        if (n <= db) n = f.WaistFt * 0.5;   // guard a thin/unknown waist
        n = BuildUtil.CapTopLayerN(f, n);   // hold clear of the irregular native-run top

        double inset = BuildUtil.BodyEndInsetFt(f, EndInsetFt(cfg));
        double L = BuildUtil.RunTopU(f, n, BuildUtil.BodyTopMarginFt(f));  // clamp clear of the irregular top
        double lo = inset, hi = L;                          // bottom inset clears the first riser; the margin insets the top
        double ext = cfg.Ft(cfg.Flight.TopSupportExtent);
        bool hookA = BuildUtil.IsHook(spec.StartAnchor), hookB = BuildUtil.IsHook(spec.EndAnchor);
        string tag = ExistingRebarCleaner.MakeTag(cfg.Name, stairId, StairLayer.FlightTopMain);

        switch (cfg.Flight.TopMode)
        {
            case TopMode.Continuous:
                return PlaceSet(f, cfg, spec, bt, db, n, lo, hi, tag, hookA, hookB);

            case TopMode.OverSupports:
                if (lo + ext >= hi - ext)   // the two end bands meet on a short flight → one continuous top bar
                    return PlaceSet(f, cfg, spec, bt, db, n, lo, hi, tag, hookA, hookB);
                int c = PlaceSet(f, cfg, spec, bt, db, n, lo, lo + ext, tag, hookA, false);
                c += PlaceSet(f, cfg, spec, bt, db, n, hi - ext, hi, tag, false, hookB);
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
