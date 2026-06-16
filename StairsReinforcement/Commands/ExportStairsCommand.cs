using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace StairsReinforcement.Commands;

/// <summary>
/// Exports a JSON geometry description of the selected stairs for the reinforcement agent.
/// (Phase 1 — geometry extraction and the JSON writer are wired in subsequent PRs; this
/// scaffold validates selection and the ribbon binding.)
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ExportStairsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        List<Element> picked = GetSelected(uidoc);
        if (picked.Count == 0)
        {
            try
            {
                IList<Reference> refs = uidoc.Selection.PickObjects(
                    ObjectType.Element, new StairsSelectionFilter(),
                    "Select stairs (or floor-modelled flights/landings) to export, then click Finish");
                picked = refs.Select(r => doc.GetElement(r.ElementId)).ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        }

        if (picked.Count == 0)
        {
            TaskDialog.Show("Export Stairs", "No stairs or structural floors selected.");
            return Result.Cancelled;
        }

        TaskDialog.Show("Export Stairs",
            $"{picked.Count} element(s) selected.\n\nJSON export is implemented in the geometry phase (PR-02…PR-04).");
        return Result.Succeeded;
    }

    private static List<Element> GetSelected(UIDocument uidoc)
    {
        var filter = new StairsSelectionFilter();
        return uidoc.Selection.GetElementIds()
            .Select(id => uidoc.Document.GetElement(id))
            .Where(filter.AllowElement)
            .ToList();
    }
}
