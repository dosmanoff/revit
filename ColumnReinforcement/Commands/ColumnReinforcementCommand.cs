using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace ColumnReinforcement.Commands;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class ColumnReinforcementCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;

        IList<ElementId> columnIds = GetSelectedColumnIds(uidoc);
        if (columnIds.Count == 0)
        {
            try
            {
                IList<Reference> refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new ColumnSelectionFilter(),
                    "Pick structural columns, then press Finish");
                columnIds = refs.Select(r => r.ElementId).ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
        }

        if (columnIds.Count == 0)
        {
            TaskDialog.Show("Column Reinforcement", "No structural columns selected.");
            return Result.Cancelled;
        }

        // Phase-1 scaffold: real dialog and engine arrive in PR-02..PR-05.
        TaskDialog.Show(
            "Column Reinforcement",
            $"Selected {columnIds.Count} column(s).\n\nDialog and engine not yet implemented (PR-01 scaffold).");

        return Result.Succeeded;
    }

    private static IList<ElementId> GetSelectedColumnIds(UIDocument uidoc)
    {
        var filter = new ColumnSelectionFilter();
        return uidoc.Selection.GetElementIds()
            .Where(id => filter.AllowElement(uidoc.Document.GetElement(id)))
            .ToList();
    }
}
