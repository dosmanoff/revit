using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SlabReinforcement.Commands;

/// <summary>
/// Stage 1 of the pipeline: dump a JSON description of the selected slabs for the
/// external reinforcement agent. PR-01 stub — real export lands in PR-02..04.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ExportSlabsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        TaskDialog.Show(
            "Export Slabs",
            "SlabReinforcement — Export Slabs.\n\nPR-01 scaffold: command wired to the ribbon. " +
            "JSON export of slab geometry, edges, openings and supports lands in PR-02..04.");
        return Result.Succeeded;
    }
}
