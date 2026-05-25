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
    {
        if (to <= from || step <= 0) yield break;
        double span = to - from;
        int n = Math.Max(1, (int)Math.Ceiling(span / step));
        double actualStep = span / n;
        for (int i = 0; i <= n; i++) yield return from + i * actualStep;
    }

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
}
