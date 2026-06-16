using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StairsReinforcement.Config;
using StairsReinforcement.Domain;

namespace StairsReinforcement.Engine;

/// <summary>
/// Optional nominal step reinforcement: a transverse bar across the width at each tread, placed in
/// the step zone near the top of the waist and distributed up the slope at the tread spacing.
/// Applies only to flights that actually have steps (native runs with a tread count). Both modes
/// place this set; the exact per-step L-section (NosingBar vs PerStepLBar) following the real tread/
/// riser solids is a later refinement — the geometry here is a sound nominal approximation.
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

        double n = f.WaistFt - cfg.Ft(steps.Cover) - db / 2;
        if (n <= 0) n = f.WaistFt * 0.7;

        double coverSide = cfg.Ft(cfg.Cover.Side);
        double wL = -f.WidthFt / 2 + coverSide + db / 2;
        double wR = f.WidthFt / 2 - coverSide - db / 2;
        if (wR - wL <= 1e-6) return 0;

        int count = f.TreadCount;
        double spacing = f.SlopeLengthFt / Math.Max(1, f.TreadCount);

        // Representative bar across the width at the first tread; the set marches up-slope along U.
        XYZ p0 = BuildUtil.XYZof(f.Frame.At(spacing * 0.5, wL, n));
        XYZ p1 = BuildUtil.XYZof(f.Frame.At(spacing * 0.5, wR, n));
        var curves = new List<Curve> { Line.CreateBound(p0, p1) };

        var U = new XYZ(f.Frame.U.X, f.Frame.U.Y, f.Frame.U.Z);
        XYZ normalU = U.IsZeroLength() ? XYZ.BasisZ : U.Normalize();

        string tag = ExistingRebarCleaner.MakeTag(cfg.Name, stairId, StairLayer.Steps);
        return RebarFactory.CreateSet(_doc, RebarStyle.Standard, bt.Id, f.Host, normalU, curves, tag, count, spacing);
    }
}
