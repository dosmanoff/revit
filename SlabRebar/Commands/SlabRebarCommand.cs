using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SlabRebar.Engine;
using SlabRebar.UI;

namespace SlabRebar.Commands;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class SlabRebarCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document   doc   = uidoc.Document;

        var config = new ClassificationConfig();
        IList<ElementId> selectedIds = GetRebarFromSelection(uidoc);

        while (true)
        {
            if (selectedIds.Count == 0)
            {
                try
                {
                    IList<Reference> refs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new RebarSelectionFilter(),
                        "Select rebar elements in slab, then press Finish");

                    selectedIds = refs.Select(r => r.ElementId).ToList();
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
            }

            var dialog = new SlabRebarDialog(doc, selectedIds, config);
            bool? dlgResult = dialog.ShowDialog();

            if (dialog.NeedReselect)
            {
                selectedIds = [];
                config = dialog.Config;
                continue;
            }

            if (dlgResult != true)
                return Result.Cancelled;

            config = dialog.Config;

            using var tx = new Transaction(doc, "Classify Slab Rebar");
            tx.Start();

            var classifier = new RebarClassifier(doc);
            var (succeeded, failed, errors) = classifier.Apply(dialog.Items, config);

            tx.Commit();

            string summary = $"Classified {succeeded} rebar element(s).";
            if (failed > 0)
                summary += $"\n{failed} element(s) failed:\n" +
                           string.Join("\n", errors.Take(5));
            if (errors.Count > 5)
                summary += $"\n… and {errors.Count - 5} more.";

            TaskDialog.Show("Slab Rebar Classifier", summary);
            return Result.Succeeded;
        }
    }

    private static IList<ElementId> GetRebarFromSelection(UIDocument uidoc) =>
        uidoc.Selection.GetElementIds()
            .Where(id => uidoc.Document.GetElement(id) is Rebar)
            .ToList();
}
