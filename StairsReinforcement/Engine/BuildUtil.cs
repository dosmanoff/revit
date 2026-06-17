using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StairsReinforcement.Config;
using StairsReinforcement.Domain;
using StairsReinforcement.Geometry;

namespace StairsReinforcement.Engine;

/// <summary>Shared sizing / anchorage / hook helpers used by the bar-set builders.</summary>
internal static class BuildUtil
{
    public static XYZ XYZof(Pt3 p) => new(p.X, p.Y, p.Z);

    /// <summary>
    /// The u along the slope at which a bar offset <paramref name="n"/> from the soffit reaches the
    /// TOP of the run's solid. The frame's slope length (derived from run.Height) overshoots the real
    /// run solid on monolithic stairs — the top riser belongs to the landing — so bars clamped to the
    /// slope length stick out above the run ("rebar outside host" + the floating bars). Clamp to this.
    /// </summary>
    public static double RunTopU(FlightComponent f, double n, double zMargin = 0)
    {
        double uz = f.Frame.U.Z;
        if (uz < 1e-6 || f.Bounds.IsEmpty) return f.SlopeLengthFt;
        double u = (f.Bounds.Max.Z - zMargin - (f.Frame.Origin.Z + f.Frame.N.Z * n)) / uz;
        return Math.Clamp(u, 0, f.SlopeLengthFt);
    }

    /// <summary>
    /// Vertical clearance to hold top-side body bars (top main / distribution / nosing) below the
    /// run-solid top when clamping with <see cref="RunTopU"/>. A native run top is irregular — steps
    /// ride on the waist and the top riser belongs to the landing — so a bar clamped to the exact top
    /// surface is flagged "outside host"; ~1.5 risers of up-slope clearance keeps the end inside.
    /// Zero for floor-modelled flights (no risers → a clean sloped box).
    /// </summary>
    public static double BodyTopMarginFt(FlightComponent f) => 1.5 * f.RiserFt;

    /// <summary>
    /// Up-slope inset from the run bottom for transverse / top body bars, clearing the angled
    /// first-riser face: one riser on a native run, else the given cover.
    /// </summary>
    public static double BodyEndInsetFt(FlightComponent f, double coverFt) =>
        f.RiserFt > 1e-6 ? Math.Max(coverFt, f.RiserFt) : coverFt;

    /// <summary>
    /// Cap a top-layer normal offset so a native run keeps it at least ~a third of a riser below the
    /// waist top, absorbing frame-vs-solid mismatch near the steps. No change for floor flights.
    /// </summary>
    public static double CapTopLayerN(FlightComponent f, double n) =>
        f.RiserFt > 1e-6 ? Math.Min(n, f.WaistFt - 0.35 * f.RiserFt) : n;

    /// <summary>Resolve a set to (count, centre-to-centre spacing in feet) over a span.</summary>
    public static (int Count, double SpacingFt) ResolveSet(SpacingMode mode, int count, double spacingFt, double spanFt)
    {
        if (spanFt <= 1e-6) return (1, 0);

        if (mode == SpacingMode.Count)
        {
            int n = Math.Max(1, count);
            return (n, n > 1 ? spanFt / (n - 1) : 0);
        }

        if (spacingFt <= 1e-6) return (1, 0);
        int num = Math.Max(1, (int)Math.Floor(spanFt / spacingFt + 1e-6) + 1);
        return (num, num > 1 ? spanFt / (num - 1) : 0);
    }

    public static double LapFt(StairsReinforcementConfig cfg, double dbFt) =>
        cfg.Lengths.LapMode == LapMode.Factor
            ? cfg.Lengths.LapFactor * dbFt
            : cfg.Ft(cfg.Lengths.LapLength);

    /// <summary>Straight extension to add at an end for a given anchor mode (0 for hooks).</summary>
    public static double AnchorExtFt(AnchorMode mode, double lenFt, StairsReinforcementConfig cfg, double dbFt) =>
        mode switch
        {
            AnchorMode.Hook90 or AnchorMode.Hook180 => 0,
            AnchorMode.IntoSupport => lenFt > 1e-6 ? lenFt : LapFt(cfg, dbFt),
            _ => Math.Max(0, lenFt),   // Straight / BendUp / BendDown (bends approximated as straight for now)
        };

    /// <summary>Whether an end is anchored with an explicit hook (only when the config asks for one).</summary>
    public static bool IsHook(AnchorMode mode) => mode is AnchorMode.Hook90 or AnchorMode.Hook180;

    /// <summary>Hook type for a hooked end: a named hook (strict), else a 90/180 hook by name match.</summary>
    public static RebarHookType? HookFor(Document doc, AnchorMode mode, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name)) return RebarFactory.GetHookType(doc, name);
        return mode switch
        {
            AnchorMode.Hook90 or AnchorMode.IntoSupport => FindHookContaining(doc, "90"),
            AnchorMode.Hook180 => FindHookContaining(doc, "180"),
            _ => null,
        };
    }

    private static RebarHookType? FindHookContaining(Document doc, string substr) =>
        new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
            .FirstOrDefault(h => h.Name.IndexOf(substr, StringComparison.OrdinalIgnoreCase) >= 0);
}
