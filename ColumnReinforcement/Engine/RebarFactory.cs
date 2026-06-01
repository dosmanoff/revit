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
        // Shape pinning route: CreateFromCurvesAndShape PRESERVES the curves (the bar
        // matches our parametric definition AND lands in the requested shape family).
        // Post-creation rebar.GetShapeDrivenAccessor().SetRebarShapeId(...) was the
        // earlier attempt (#76) — but that re-fits the bar to the new shape's DEFAULT
        // parameters and ends up with the segments reshuffled (Z with diagonals at the
        // ends instead of in the middle for shape 19). So we go direct here.
        //
        // The try/catch is for the rare case where the supplied curves violate the
        // shape family's parametric constraints (a slope/angle/length outside its
        // allowed range — manifests as an internal Revit NRE on specific bars). On
        // failure we fall through to the curves-driven auto-match: geometry stays
        // correct, the user just gets whichever shape Revit auto-picks for THAT bar
        // (which is the pre-#75 behaviour for all bars).
        Rebar? rebar = null;
        if (shape is not null)
        {
            try
            {
                rebar = Rebar.CreateFromCurvesAndShape(
                    doc, shape, barType, startHook, endHook, host,
                    norm:                       normal,
                    curves:                     curves,
                    startHookOrient:            startHookOrient,
                    endHookOrient:              endHookOrient,
                    hookRotationAngleAtStart:   0.0,
                    hookRotationAngleAtEnd:     0.0,
                    endTreatmentTypeIdAtStart:  ElementId.InvalidElementId,
                    endTreatmentTypeIdAtEnd:    ElementId.InvalidElementId);
            }
            catch (Exception ex)
            {
                // Shape-pin failed — fall through to auto-match below. Capture the
                // exception so the caller can surface it (otherwise the bar silently
                // lands in whichever shape Revit auto-matches, which in projects with
                // a competing custom Z family is often the wrong one).
                RecordShapePinFailure(host, shape, barType, ex);
                rebar = null;
            }
        }
        if (rebar is null)
        {
            rebar = Rebar.CreateFromCurves(
                doc, style, barType, startHook, endHook, host,
                norm:             normal,
                curves:           curves,
                startHookOrient:  startHookOrient,
                endHookOrient:    endHookOrient,
                useExistingShapeIfPossible: true,
                createNewShape:   true);

            // If shape pin was requested AND fallback created the bar, try to
            // re-stamp the shape via the shape-driven accessor. Prior testing
            // (PR #76) showed SetRebarShapeId can re-fit a bar to the shape's
            // DEFAULT parameters, scrambling segment correspondence — so this
            // is best-effort: a wrong shape with right geometry is preferable to
            // a wrong-shape-and-Revit-auto-picks-the-other-custom-family bar.
            // We swallow exceptions here intentionally: the bar already exists,
            // and any failure leaves it with the auto-matched shape (logged above).
            if (shape is not null)
            {
                try
                {
                    var acc = rebar.GetShapeDrivenAccessor();
                    if (acc is not null && rebar.GetShapeId() != shape.Id)
                        acc.SetRebarShapeId(shape.Id);
                }
                catch { /* leave it auto-matched */ }
            }
        }

        // Post-creation verification: even when CreateFromCurvesAndShape returns
        // without throwing, in practice Revit sometimes silently overrides the
        // requested shape (it appears the API treats the shape argument as a hint
        // when the curves don't satisfy the family's parametric constraints
        // — different behaviour than rejecting with an exception). Compare the
        // actual GetShapeId() against the requested shape and record a mismatch
        // so the user knows the pin didn't stick.
        if (shape is not null && rebar.GetShapeId() != shape.Id)
        {
            ElementId actualId = rebar.GetShapeId();
            string actualName = actualId == ElementId.InvalidElementId
                ? "<invalid>"
                : doc.GetElement(actualId)?.Name ?? "<unknown>";
            RecordShapeMismatch(host, shape, barType, actualName);
        }

        ExistingRebarCleaner.Tag(rebar, tag);
        return rebar;
    }

    // ── Shape-pin diagnostics ───────────────────────────────────────────
    // When CreateFromCurvesAndShape rejects a bar (the shape family's parametric
    // constraints don't accept the curves), the engine silently falls back to the
    // auto-match path — which, in projects with multiple matching shape families,
    // can pick the WRONG one. We accumulate failures into a static list so the
    // command layer can drain it after a run and show the user what Revit
    // complained about.

    private static readonly object _failureLock = new();
    private static readonly List<ShapePinFailure> _failures = new();

    /// <summary>One captured CreateFromCurvesAndShape rejection.</summary>
    public record ShapePinFailure(
        long HostId,
        string HostCategory,
        string ShapeName,
        string BarTypeName,
        string ExceptionType,
        string Message);

    private static void RecordShapePinFailure(Element host, RebarShape shape, RebarBarType barType, Exception ex)
    {
        lock (_failureLock)
        {
            // Cap at 50 to bound memory if a run has thousands of failures.
            if (_failures.Count >= 50) return;
            _failures.Add(new ShapePinFailure(
                HostId:        host.Id.Value,
                HostCategory:  host.Category?.Name ?? "?",
                ShapeName:     shape.Name,
                BarTypeName:   barType.Name,
                ExceptionType: ex.GetType().Name,
                Message:       ex.Message));
        }
    }

    private static void RecordShapeMismatch(Element host, RebarShape requested, RebarBarType barType, string actualShapeName)
    {
        lock (_failureLock)
        {
            if (_failures.Count >= 50) return;
            _failures.Add(new ShapePinFailure(
                HostId:        host.Id.Value,
                HostCategory:  host.Category?.Name ?? "?",
                ShapeName:     requested.Name,
                BarTypeName:   barType.Name,
                ExceptionType: "ShapeOverride",
                Message:       $"API accepted shape '{requested.Name}' (id {requested.Id.Value}) without throwing, but the resulting Rebar.GetShapeId() resolves to '{actualShapeName}' — Revit silently re-matched the shape based on the curves."));
        }
    }

    /// <summary>
    /// Take all shape-pin failures recorded since the last drain and clear the
    /// accumulator. Call at the end of a run; show non-empty results to the user.
    /// </summary>
    public static IReadOnlyList<ShapePinFailure> DrainShapePinFailures()
    {
        lock (_failureLock)
        {
            var copy = _failures.ToArray();
            _failures.Clear();
            return copy;
        }
    }
}
