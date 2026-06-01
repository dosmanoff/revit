using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnReinforcement.Engine;

/// <summary>
/// Shared low-level helpers for the rebar builders. Strict lookups by exact
/// <c>.Name</c> match in the active document — no auto-create — per the repo's
/// conventions for both wall and column reinforcement.
/// </summary>
public static class RebarFactory
{
    /// <summary>
    /// Look up a <see cref="RebarBarType"/> by exact <c>.Name</c>. Throws with a
    /// descriptive message listing the available types if not found.
    /// </summary>
    public static RebarBarType GetBarType(Document doc, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Bar type name is empty.", nameof(name));

        var types = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarBarType))
            .Cast<RebarBarType>()
            .ToList();

        var hit = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
        if (hit is not null) return hit;

        string available = string.Join(", ", types.Select(t => t.Name).OrderBy(n => n));
        throw new InvalidOperationException(
            $"RebarBarType '{name}' not found in document. Available: {available}");
    }

    /// <summary>
    /// Look up a <see cref="RebarHookType"/> by exact <c>.Name</c>. Returns
    /// <c>null</c> if <paramref name="name"/> is null or empty (no hook). Throws
    /// with a descriptive message if a non-empty name does not match.
    /// </summary>
    public static RebarHookType? GetHookType(Document doc, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var types = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarHookType))
            .Cast<RebarHookType>()
            .ToList();

        var hit = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
        if (hit is not null) return hit;

        string available = string.Join(", ", types.Select(t => t.Name).OrderBy(n => n));
        throw new InvalidOperationException(
            $"RebarHookType '{name}' not found in document. Available: {available}");
    }

    /// <summary>
    /// Look up a <see cref="RebarShape"/> by exact <c>.Name</c>. Returns <c>null</c>
    /// when <paramref name="name"/> is null/empty (engine uses Revit's curves-driven
    /// auto-match instead). Throws with a descriptive message if a non-empty name
    /// does not match — same strict policy as the bar-type/hook lookups.
    /// </summary>
    public static RebarShape? GetRebarShape(Document doc, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var shapes = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarShape))
            .Cast<RebarShape>()
            .ToList();

        var hit = shapes.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        if (hit is not null) return hit;

        string available = string.Join(", ", shapes.Select(s => s.Name).OrderBy(n => n));
        throw new InvalidOperationException(
            $"RebarShape '{name}' not found in document. Available: {available}");
    }

    /// <summary>
    /// Create a <see cref="Rebar"/> from a list of curves and tag it for
    /// idempotent re-runs. Returns the created Rebar; throws on failure.
    ///
    /// <para>When <paramref name="shape"/> is non-null the bar is created via
    /// <see cref="Rebar.CreateFromCurvesAndShape"/> with that shape pinned —
    /// bypasses Revit's geometry-based auto-match, which non-deterministically
    /// picks among multiple matching shape families in a project (a real-world
    /// problem when the project has both a standard <c>19</c> and a custom
    /// <c>28_Z_Frame</c> loaded). When <paramref name="shape"/> is null the
    /// curves-driven auto-match is used (the original behaviour).</para>
    /// </summary>
    public static Rebar Create(
        Document doc,
        RebarStyle style,
        RebarBarType barType,
        Element host,
        XYZ normal,
        IList<Curve> curves,
        string tag,
        RebarHookType? startHook = null,
        RebarHookType? endHook = null,
        RebarHookOrientation startHookOrient = RebarHookOrientation.Right,
        RebarHookOrientation endHookOrient = RebarHookOrientation.Right,
        RebarShape? shape = null)
    {
        // Always go through CreateFromCurves first — it tolerates null hooks (Cranked
        // main bars and many other cases have no top hook in our configs). Then, if
        // the caller asked for a pinned shape, swap it via the shape-driven accessor.
        // The first attempt with Rebar.CreateFromCurvesAndShape on this overload threw
        // NRE inside Revit when either hook was null (PR #75 → #76 regression).
        Rebar rebar = Rebar.CreateFromCurves(
            doc, style, barType, startHook, endHook, host,
            norm:             normal,
            curves:           curves,
            startHookOrient:  startHookOrient,
            endHookOrient:    endHookOrient,
            useExistingShapeIfPossible: true,
            createNewShape:   true);

        if (shape is not null)
        {
            // CreateFromCurves with useExistingShapeIfPossible=true produces a
            // shape-driven bar, so the accessor is non-null in practice. Defensive
            // null-check anyway: a missing accessor means we can't pin the shape and
            // the user will see whichever shape Revit auto-matched — preferable to a
            // hard crash on an otherwise-valid run.
            RebarShapeDrivenAccessor? accessor = rebar.GetShapeDrivenAccessor();
            accessor?.SetRebarShapeId(shape.Id);
        }

        ExistingRebarCleaner.Tag(rebar, tag);
        return rebar;
    }
}
