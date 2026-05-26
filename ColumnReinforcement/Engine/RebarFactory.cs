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
    /// Create a <see cref="Rebar"/> from a list of curves and tag it for
    /// idempotent re-runs. Returns the created Rebar; throws on failure.
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
        RebarHookOrientation endHookOrient = RebarHookOrientation.Right)
    {
        Rebar rebar = Rebar.CreateFromCurves(
            doc,
            style,
            barType,
            startHook,
            endHook,
            host,
            norm:             normal,
            curves:           curves,
            startHookOrient:  startHookOrient,
            endHookOrient:    endHookOrient,
            useExistingShapeIfPossible: true,
            createNewShape:   true);

        ExistingRebarCleaner.Tag(rebar, tag);
        return rebar;
    }
}
