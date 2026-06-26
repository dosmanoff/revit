using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace WallReinforcement.Engine;

/// <summary>
/// Shared low-level helpers for the rebar builders — pulled out to avoid the
/// same 30-line LookupBarType / CreateFromCurves boilerplate in six places.
/// </summary>
public static class RebarFactory
{
    public static ElementId LookupBarType(Document doc, string name)
    {
        var hit = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarBarType))
            .FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        return hit?.Id ?? ElementId.InvalidElementId;
    }

    /// <summary>
    /// Yield positions in [from, to] such that no two adjacent positions are more than
    /// <paramref name="step"/> apart. Endpoints are always included. Returns nothing if
    /// the range is empty or the step is non-positive.
    /// </summary>
    public static IEnumerable<double> EvenlySpaced(double from, double to, double step)
        => WallReinforcement.Geometry.BarLayout.EvenlySpaced(from, to, step);

    /// <summary>
    /// Create a Rebar from one or more curves with no hooks, then tag its Comments
    /// for idempotent re-runs. Returns the created Rebar; throws if creation fails.
    /// </summary>
    public static Rebar Create(
        Document doc,
        RebarStyle style,
        ElementId barTypeId,
        Element host,
        XYZ normal,
        IList<Curve> curves,
        string tag)
    {
        Rebar rebar = Rebar.CreateFromCurves(
            doc,
            style,
            (RebarBarType)doc.GetElement(barTypeId),
            startHook:        null,
            endHook:          null,
            host:             host,
            norm:             normal,
            curves:           curves,
            startHookOrient:  RebarHookOrientation.Right,
            endHookOrient:    RebarHookOrientation.Right,
            useExistingShapeIfPossible: true,
            createNewShape:   true);

        ExistingRebarCleaner.Tag(rebar, tag);
        return rebar;
    }

    /// <summary>
    /// Create one representative Rebar from <paramref name="curves"/> and, when
    /// <paramref name="count"/> &gt; 1, expand it into a SET distributed along
    /// <paramref name="distributionDir"/> at <paramref name="spacing"/> (feet). Places ONE Rebar
    /// element instead of N loose bars — far faster, and what schedules/tags expect. Falls back
    /// to the single representative bar if Revit rejects the set layout.
    /// </summary>
    public static Rebar CreateSet(
        Document doc,
        RebarStyle style,
        ElementId barTypeId,
        Element host,
        XYZ distributionDir,
        IList<Curve> curves,
        int count,
        double spacing,
        string tag,
        ElementId? startHookId = null,
        ElementId? endHookId = null)
    {
        Rebar rebar = Rebar.CreateFromCurves(
            doc, style, (RebarBarType)doc.GetElement(barTypeId),
            startHook: HookOrNull(doc, startHookId), endHook: HookOrNull(doc, endHookId),
            host: host, norm: distributionDir, curves: curves,
            startHookOrient: RebarHookOrientation.Right, endHookOrient: RebarHookOrientation.Right,
            useExistingShapeIfPossible: true, createNewShape: true);

        if (count > 1 && spacing > 1e-9)
        {
            try { rebar.GetShapeDrivenAccessor().SetLayoutAsNumberWithSpacing(count, spacing, true, true, true); }
            catch { /* leave it as the single representative bar if the layout is rejected */ }
        }

        ExistingRebarCleaner.Tag(rebar, tag);
        return rebar;
    }

    /// <summary>
    /// Uniform set layout spanning [<paramref name="from"/>, <paramref name="to"/>] with steps no
    /// larger than <paramref name="desiredStep"/>: returns the bar <c>count</c> (both endpoints
    /// included), the exact <c>spacing</c> that divides the span evenly, and the <c>first</c>
    /// position. Returns count 0 for an empty or degenerate span.
    /// </summary>
    public static (int count, double spacing, double first) UniformLayout(double from, double to, double desiredStep)
        => WallReinforcement.Geometry.BarLayout.UniformLayout(from, to, desiredStep);

    private static RebarHookType? HookOrNull(Document doc, ElementId? id) =>
        id is not null && id != ElementId.InvalidElementId ? doc.GetElement(id) as RebarHookType : null;

    /// <summary>
    /// Find a <see cref="RebarHookType"/> by name fragment (first match), e.g. "135" for a 135°
    /// tie hook. Returns <see cref="ElementId.InvalidElementId"/> if none is found, in which case
    /// callers place a straight bar with no hook.
    /// </summary>
    public static ElementId LookupHookType(Document doc, params string[] nameFragments)
    {
        var hooks = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarHookType)).Cast<RebarHookType>().ToList();
        foreach (string frag in nameFragments)
        {
            RebarHookType? hit = hooks.FirstOrDefault(h => h.Name.IndexOf(frag, StringComparison.OrdinalIgnoreCase) >= 0);
            if (hit is not null) return hit.Id;
        }
        return ElementId.InvalidElementId;
    }
}
