using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StairsReinforcement.Config;
using StairsReinforcement.Geometry;

namespace StairsReinforcement.Engine;

/// <summary>Shared sizing / anchorage / hook helpers used by the bar-set builders.</summary>
internal static class BuildUtil
{
    public static XYZ XYZof(Pt3 p) => new(p.X, p.Y, p.Z);

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

    /// <summary>
    /// Whether an end is anchored with a hook. <see cref="AnchorMode.IntoSupport"/> counts as a hook
    /// because a native-stair host cannot accept a straight bar running out into the supporting slab
    /// (it throws "internal error") — the development is provided by a hook inside the waist instead.
    /// </summary>
    public static bool IsHook(AnchorMode mode) =>
        mode is AnchorMode.Hook90 or AnchorMode.Hook180 or AnchorMode.IntoSupport;

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
