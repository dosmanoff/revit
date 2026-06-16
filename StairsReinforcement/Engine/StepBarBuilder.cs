using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StairsReinforcement.Config;
using StairsReinforcement.Domain;

namespace StairsReinforcement.Engine;

/// <summary>
/// Optional step reinforcement, for flights that actually have steps (native runs with a tread
/// count):
/// <list type="bullet">
/// <item><c>NosingBar</c> — one transverse bar across the width at each tread, near the top of the
/// waist, distributed up the slope.</item>
/// <item><c>PerStepLBar</c> — per tread, an L-bar in the section (a leg along the waist + a leg
/// turning up into the step), distributed across the width.</item>
/// </list>
/// Both are sound nominal approximations placed off the waist frame; following the exact tread/riser
/// solids is a later refinement.
/// </summary>
public sealed class StepBarBuilder
{
    private readonly Document _doc;
    public StepBarBuilder(Document doc) => _doc = doc;

    public int Build(FlightComponent f, StairsReinforcementConfig cfg, ElementId stairId)
    {
        StepConfig steps = cfg.Flight.Steps;
        if (steps.Mode == StepMode.None || f.TreadCount <= 0) return 0;

        RebarBarType bt = RebarFactory.GetBarType(_doc, steps.BarType);
        double db = bt.BarNominalDiameter;
        string tag = ExistingRebarCleaner.MakeTag(cfg.Name, stairId, StairLayer.Steps);

        return steps.Mode == StepMode.PerStepLBar
            ? BuildPerStepL(f, cfg, steps, bt, db, tag)
            : BuildNosing(f, cfg, steps, bt, db, tag);
    }

    /// <summary>One transverse bar across the width per tread; the set marches up the slope.</summary>
    private int BuildNosing(FlightComponent f, StairsReinforcementConfig cfg, StepConfig steps, RebarBarType bt, double db, string tag)
    {
        double n = TopN(f, cfg, steps, db);
        (double wL, double wR) = WidthRange(f, cfg, db);
        if (wR - wL <= 1e-6) return 0;

        int count = f.TreadCount;
        double spacing = f.SlopeLengthFt / Math.Max(1, f.TreadCount);

        XYZ p0 = BuildUtil.XYZof(f.Frame.At(spacing * 0.5, wL, n));
        XYZ p1 = BuildUtil.XYZof(f.Frame.At(spacing * 0.5, wR, n));
        var curves = new List<Curve> { Line.CreateBound(p0, p1) };

        return RebarFactory.CreateSet(_doc, RebarStyle.Standard, bt.Id, f.Host, UpSlope(f), curves, tag, count, spacing);
    }

    /// <summary>Per tread, an L-bar (waist leg + upstand into the step), distributed across the width.</summary>
    private int BuildPerStepL(FlightComponent f, StairsReinforcementConfig cfg, StepConfig steps, RebarBarType bt, double db, string tag)
    {
        double n = TopN(f, cfg, steps, db);
        double leg = cfg.Ft(steps.Leg);
        (double wL, double wR) = WidthRange(f, cfg, db);
        double widthSpan = wR - wL;
        if (widthSpan <= 1e-6) return 0;

        // Distribute the per-step Ls across the width at the bottom-distribution spacing (fallback 12").
        double acrossSpacing = cfg.Flight.BottomDist.Enabled ? cfg.Ft(cfg.Flight.BottomDist.Spacing) : 1.0;
        (int across, double spacing) = BuildUtil.ResolveSet(SpacingMode.Spacing, 0, acrossSpacing, widthSpan);

        var Wv = new XYZ(f.Frame.W.X, f.Frame.W.Y, 0);
        XYZ normalW = Wv.IsZeroLength() ? XYZ.BasisX : Wv.Normalize();

        double stepSlope = f.SlopeLengthFt / Math.Max(1, f.TreadCount);
        int created = 0;
        for (int i = 0; i < f.TreadCount; i++)
        {
            double u = (i + 0.5) * stepSlope;
            XYZ treadStart = BuildUtil.XYZof(f.Frame.At(Math.Max(0, u - leg), wL, n));
            XYZ nosing = BuildUtil.XYZof(f.Frame.At(u, wL, n));
            XYZ upstand = BuildUtil.XYZof(f.Frame.At(u, wL, n + leg));   // turns up into the step
            var curves = new List<Curve> { Line.CreateBound(treadStart, nosing), Line.CreateBound(nosing, upstand) };
            created += RebarFactory.CreateSet(_doc, RebarStyle.Standard, bt.Id, f.Host, normalW, curves, tag, across, spacing);
        }
        return created;
    }

    private static double TopN(FlightComponent f, StairsReinforcementConfig cfg, StepConfig steps, double db)
    {
        double n = f.WaistFt - cfg.Ft(steps.Cover) - db / 2;
        return n <= 0 ? f.WaistFt * 0.7 : n;
    }

    private static (double wL, double wR) WidthRange(FlightComponent f, StairsReinforcementConfig cfg, double db)
    {
        double coverSide = cfg.Ft(cfg.Cover.Side);
        return (-f.WidthFt / 2 + coverSide + db / 2, f.WidthFt / 2 - coverSide - db / 2);
    }

    private static XYZ UpSlope(FlightComponent f)
    {
        var U = new XYZ(f.Frame.U.X, f.Frame.U.Y, f.Frame.U.Z);
        return U.IsZeroLength() ? XYZ.BasisZ : U.Normalize();
    }
}
