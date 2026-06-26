using Autodesk.Revit.DB;

namespace WallReinforcement.Engine;

/// <summary>
/// Deletes warning-level failures during a transaction commit so a batch run never stops on a
/// modal warning dialog (e.g. "rebar is outside its host", "bars slightly overlap"). Errors are
/// left for Revit to surface — the per-wall transaction then rolls back and the wall is reported
/// as failed, rather than the whole batch freezing on a dialog.
/// </summary>
public sealed class WarningSwallower : IFailuresPreprocessor
{
    public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
    {
        a.DeleteAllWarnings();

        // Anything left after clearing warnings is an error (e.g. a "cannot be ignored" circular-
        // reference error from an invalid bar). Roll this wall's transaction back instead of letting
        // Revit raise a modal dialog — the orchestrator records the wall as failed and the batch
        // continues. Without this a single bad wall freezes an unattended run on a dialog.
        if (a.GetFailureMessages().Count > 0)
            return FailureProcessingResult.ProceedWithRollBack;

        return FailureProcessingResult.Continue;
    }
}
