using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SlabReinforcement.Commands;

/// <summary>
/// Stage 3 of the pipeline: read the per-slab assignments CSV and generate rebar.
/// PR-01 stub — config/CSV/engine land in Phase 2 (PR-06..08).
/// </summary>
[Transaction(TransactionMode.Manual)]
public class GenerateSlabRebarCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        TaskDialog.Show(
            "Generate Slab Rebar",
            "SlabReinforcement — Generate Slab Rebar.\n\nPR-01 scaffold: command wired to the ribbon. " +
            "Config, CSV loader and the field-bar engine (max-length split + lap) land in PR-06..08.");
        return Result.Succeeded;
    }
}
