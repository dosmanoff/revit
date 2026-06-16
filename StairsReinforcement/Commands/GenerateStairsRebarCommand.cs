using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace StairsReinforcement.Commands;

/// <summary>
/// Generates stair rebar from a per-stair assignments CSV (or one JSON config for all).
/// (Phase 2 — the config/CSV loader, dialog and rebar engine are wired in subsequent PRs;
/// this scaffold validates selection and the ribbon binding.)
/// </summary>
[Transaction(TransactionMode.Manual)]
public class GenerateStairsRebarCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        var filter = new StairsSelectionFilter();
        List<Element> picked = uidoc.Selection.GetElementIds()
            .Select(id => doc.GetElement(id))
            .Where(filter.AllowElement)
            .ToList();

        if (picked.Count == 0)
        {
            try
            {
                IList<Reference> refs = uidoc.Selection.PickObjects(
                    ObjectType.Element, filter,
                    "Select stairs to reinforce, then click Finish");
                picked = refs.Select(r => doc.GetElement(r.ElementId)).ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        }

        if (picked.Count == 0)
        {
            TaskDialog.Show("Generate Stair Rebar", "No stairs or structural floors selected.");
            return Result.Cancelled;
        }

        TaskDialog.Show("Generate Stair Rebar",
            $"{picked.Count} element(s) selected.\n\nThe rebar engine is implemented in the generation phase (PR-06…PR-12).");
        return Result.Succeeded;
    }
}
