using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SlabReinforcement.Commands;

/// <summary>
/// Stage 4 of the pipeline: create Layer 1-4 plan views, rebar schedules and sheets.
/// PR-01 stub — views/schedules/sheets engines land in Phase 4 (PR-13..14).
/// </summary>
[Transaction(TransactionMode.Manual)]
public class SlabViewsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        TaskDialog.Show(
            "Slab Views",
            "SlabReinforcement — Slab Views.\n\nPR-01 scaffold: command wired to the ribbon. " +
            "Layer 1-4 views, schedules and sheet layout land in PR-13..14.");
        return Result.Succeeded;
    }
}
