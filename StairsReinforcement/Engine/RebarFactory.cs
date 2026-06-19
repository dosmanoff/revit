using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace StairsReinforcement.Engine;

/// <summary>
/// Shared low-level rebar helpers: strict bar/hook lookup (repo convention — no auto-create),
/// even spacing, and a tagged <see cref="Rebar.CreateFromCurves"/> wrapper that also distributes
/// a set via <see cref="RebarShapeDrivenAccessor.SetLayoutAsNumberWithSpacing"/>.
/// Ported from SlabReinforcement.Engine.RebarFactory.
/// </summary>
public static class RebarFactory
{
    /// <summary>Lenient lookup — returns InvalidElementId if the name is missing.</summary>
    public static ElementId LookupBarType(Document doc, string name)
    {
        RebarBarType? hit = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
            .FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        return hit?.Id ?? ElementId.InvalidElementId;
    }

    /// <summary>Strict lookup by exact name — throws with the available list if missing.</summary>
    public static RebarBarType GetBarType(Document doc, string name)
    {
        List<RebarBarType> all = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarBarType)).Cast<RebarBarType>().ToList();

        RebarBarType? hit = all.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) return hit;

        string available = string.Join(", ", all.Select(b => b.Name).OrderBy(n => n));
        throw new InvalidOperationException($"RebarBarType '{name}' not found in document. Available: {available}");
    }

    /// <summary>Strict hook lookup — null for empty name, throws if a named hook is missing.</summary>
    public static RebarHookType? GetHookType(Document doc, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        List<RebarHookType> all = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarHookType)).Cast<RebarHookType>().ToList();

        RebarHookType? hit = all.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) return hit;

        string available = string.Join(", ", all.Select(h => h.Name).OrderBy(n => n));
        throw new InvalidOperationException($"RebarHookType '{name}' not found in document. Available: {available}");
    }

    /// <summary>
    /// Positions in [from, to] no more than <paramref name="step"/> apart, endpoints included.
    /// Empty if the range is non-positive.
    /// </summary>
    public static IEnumerable<double> EvenlySpaced(double from, double to, double step)
    {
        if (to <= from || step <= 0) yield break;
        double span = to - from;
        int n = Math.Max(1, (int)Math.Ceiling(span / step));
        double actualStep = span / n;
        for (int i = 0; i <= n; i++) yield return from + i * actualStep;
    }

    /// <summary>Create a Rebar from curves with no hooks, tagged for idempotent re-runs.</summary>
    public static Rebar Create(
        Document doc, RebarStyle style, ElementId barTypeId, Element host,
        XYZ normal, IList<Curve> curves, string tag) =>
        Create(doc, style, barTypeId, host, normal, curves, tag, null, null,
            RebarHookOrientation.Right, RebarHookOrientation.Right);

    /// <summary>
    /// Create a Rebar from curves with optional hooks, tagged for idempotent re-runs.
    /// <paramref name="reuseShape"/> = true (default) matches an existing parametric RebarShape so bars
    /// share a shape in the bending schedule — every 90° пэшка → shape 17; dowels → a couple of 2-segment
    /// shapes by bend angle (legs free). Verified the match keeps the bend angle EXACT (no snap), so bent
    /// bars use it too; = false only forces a fresh per-bar shape, which proliferates shapes — avoid it.
    /// </summary>
    public static Rebar Create(
        Document doc, RebarStyle style, ElementId barTypeId, Element host,
        XYZ normal, IList<Curve> curves, string tag,
        RebarHookType? startHook, RebarHookType? endHook,
        RebarHookOrientation startOrient, RebarHookOrientation endOrient,
        bool reuseShape = true)
    {
        Rebar rebar = Rebar.CreateFromCurves(
            doc, style,
            (RebarBarType)doc.GetElement(barTypeId),
            startHook, endHook,
            host, normal, curves,
            startOrient, endOrient,
            useExistingShapeIfPossible: reuseShape,
            createNewShape: true);

        ExistingRebarCleaner.Tag(rebar, tag);
        return rebar;
    }

    /// <summary>
    /// Create one representative bar then distribute it as a native Revit set of
    /// <paramref name="count"/> bars at <paramref name="spacingFt"/> on the +normal side.
    /// Falls back to the single bar if the layout cannot be applied. Returns the bar count
    /// actually requested (for reporting); the model holds one <see cref="Rebar"/> set element.
    /// </summary>
    public static int CreateSet(
        Document doc, RebarStyle style, ElementId barTypeId, Element host,
        XYZ normal, IList<Curve> curves, string tag, int count, double spacingFt,
        RebarHookType? startHook = null, RebarHookType? endHook = null, bool reuseShape = true)
    {
        Rebar set = Create(doc, style, barTypeId, host, normal, curves, tag,
            startHook, endHook, RebarHookOrientation.Right, RebarHookOrientation.Right, reuseShape);

        if (count > 1 && spacingFt > 1e-6)
        {
            try
            {
                set.GetShapeDrivenAccessor()
                    .SetLayoutAsNumberWithSpacing(count, spacingFt, true, true, true);
            }
            catch
            {
                // Keep the single representative bar if the set layout is rejected.
            }
        }

        return Math.Max(1, count);
    }
}
